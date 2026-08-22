using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class ClinicalDefinitionImporter(
    BeeexyDbContext dbContext,
    ClinicalDefinitionPackageValidator validator,
    ILogger<ClinicalDefinitionImporter> logger) : IClinicalDefinitionImporter
{
    public async Task<ClinicalDefinitionImportResult> ImportAsync(
        ClinicalDefinitionPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        validator.Validate(package);
        EnsureContentHashesMatch(package);

        var questionnaire = await dbContext.QuestionnaireVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.QuestionnaireCode == package.Questionnaire.QuestionnaireCode &&
                    value.Version == package.Version,
                cancellationToken);
        var ruleSet = await dbContext.ClinicalRuleSetVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.RuleSetCode == package.RuleSet.RuleSetCode &&
                    value.Version == package.Version,
                cancellationToken);

        if (questionnaire is not null || ruleSet is not null)
        {
            EnsureExistingVersionMatches(package, questionnaire, ruleSet);
            logger.LogInformation(
                "Clinical definition package already imported for {Pathway} version {Version} " +
                "with source {Source} and review status {ReviewStatus}",
                package.Pathway.Value,
                package.Version.Value,
                package.ContentStatus.Source,
                package.ContentStatus.ReviewStatus);
            return new ClinicalDefinitionImportResult(
                ClinicalDefinitionImportOutcome.AlreadyImported,
                package.Pathway,
                package.Version);
        }

        dbContext.QuestionnaireVersions.Add(package.Questionnaire);
        dbContext.ClinicalRuleSetVersions.Add(package.RuleSet);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Imported clinical definition package for {Pathway} version {Version} with source " +
            "{Source}, review status {ReviewStatus}, and approval status {ApprovalStatus}",
            package.Pathway.Value,
            package.Version.Value,
            package.ContentStatus.Source,
            package.ContentStatus.ReviewStatus,
            package.ContentStatus.ApprovalStatus);
        return new ClinicalDefinitionImportResult(
            ClinicalDefinitionImportOutcome.Imported,
            package.Pathway,
            package.Version);
    }

    private static void EnsureExistingVersionMatches(
        ClinicalDefinitionPackage package,
        QuestionnaireDefinitionVersion? questionnaire,
        ClinicalRuleSetVersion? ruleSet)
    {
        if (questionnaire is null || ruleSet is null ||
            questionnaire.Pathway != package.Pathway ||
            ruleSet.Pathway != package.Pathway ||
            questionnaire.ContentHash != package.Questionnaire.ContentHash ||
            ruleSet.ContentHash != package.RuleSet.ContentHash ||
            questionnaire.ContentStatus != package.ContentStatus ||
            ruleSet.ContentStatus != package.ContentStatus)
        {
            throw new ClinicalDefinitionValidationException(
                "An immutable clinical definition version already exists with different or " +
                "incomplete content. Import a new version instead of mutating it.");
        }
    }

    private static void EnsureContentHashesMatch(ClinicalDefinitionPackage package)
    {
        if (ClinicalDefinitionIntegrity.QuestionnaireHash(package.Questionnaire.Questions) !=
                package.Questionnaire.ContentHash ||
            ClinicalDefinitionIntegrity.RulePackageHash(package.RuleSet.DefinitionMetadataJson) !=
                package.RuleSet.ContentHash)
        {
            throw new ClinicalDefinitionValidationException(
                "Clinical definition content does not match its immutable content hashes.");
        }
    }
}
