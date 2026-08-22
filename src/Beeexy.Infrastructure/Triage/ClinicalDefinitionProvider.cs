using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class ClinicalDefinitionProvider(
    BeeexyDbContext dbContext,
    ClinicalDefinitionPackageValidator validator) : IClinicalDefinitionProvider
{
    public async Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
        ClinicalPathwayCode pathway,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        var questionnaire = await dbContext.QuestionnaireVersions
            .AsNoTracking()
            .Include(value => value.Questions)
            .Where(value => value.Pathway == pathway && value.ActivatedAt != null)
            .OrderByDescending(value => value.ActivatedAt)
            .ThenByDescending(value => value.ImportedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return questionnaire is null
            ? null
            : await BuildPackageAsync(questionnaire, cancellationToken);
    }

    public async Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
        ClinicalPathwayCode pathway,
        ClinicalDefinitionPackageProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        var questionnaires = await dbContext.QuestionnaireVersions
            .AsNoTracking()
            .Include(value => value.Questions)
            .Where(value => value.Pathway == pathway && value.ActivatedAt != null)
            .OrderByDescending(value => value.ActivatedAt)
            .ThenByDescending(value => value.ImportedAt)
            .ToArrayAsync(cancellationToken);
        foreach (var questionnaire in questionnaires)
        {
            var package = await BuildPackageAsync(questionnaire, cancellationToken);
            if (package.Profile == profile)
            {
                return package;
            }
        }

        return null;
    }

    public async Task<ClinicalDefinitionPackage?> GetDefinitionAsync(
        ClinicalPathwayCode pathway,
        DefinitionVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        ArgumentNullException.ThrowIfNull(version);
        var questionnaire = await dbContext.QuestionnaireVersions
            .AsNoTracking()
            .Include(value => value.Questions)
            .SingleOrDefaultAsync(
                value => value.Pathway == pathway && value.Version == version,
                cancellationToken);
        return questionnaire is null
            ? null
            : await BuildPackageAsync(questionnaire, cancellationToken);
    }

    private async Task<ClinicalDefinitionPackage> BuildPackageAsync(
        QuestionnaireDefinitionVersion questionnaire,
        CancellationToken cancellationToken)
    {
        var ruleSet = await dbContext.ClinicalRuleSetVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Pathway == questionnaire.Pathway &&
                    value.Version == questionnaire.Version,
                cancellationToken) ?? throw new InvalidOperationException(
                    $"Clinical package '{questionnaire.Pathway}/{questionnaire.Version}' " +
                    "is missing its matching rule set.");
        var orderedQuestions = questionnaire.Questions
            .OrderBy(value => value.DisplayOrder)
            .ToArray();
        if (ClinicalDefinitionIntegrity.QuestionnaireHash(orderedQuestions) !=
                questionnaire.ContentHash ||
            ClinicalDefinitionIntegrity.RulePackageHash(ruleSet.DefinitionMetadataJson) !=
                ruleSet.ContentHash)
        {
            throw new InvalidOperationException(
                $"Clinical package '{questionnaire.Pathway}/{questionnaire.Version}' " +
                "failed its immutable content-hash validation.");
        }

        var package = new ClinicalDefinitionPackage(
            questionnaire.Pathway,
            questionnaire,
            ruleSet,
            orderedQuestions.Select(ClinicalDefinitionSerialization.DeserializeQuestion).ToArray(),
            ClinicalDefinitionSerialization.DeserializeBranches(orderedQuestions),
            ClinicalDefinitionSerialization.DeserializeRulePackage(
                ruleSet.DefinitionMetadataJson));
        validator.Validate(package);
        return package;
    }
}
