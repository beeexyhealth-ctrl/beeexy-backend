using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ClinicalDefinitionPersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task AbdominalPackage_ImportsIdempotentlyPersistsAndResolvesWithoutCrossMapping()
    {
        await EnsureMigratedAsync();
        await DeletePackageFixtureAsync();
        var package = AbdominalPainProvisionalPackage.Create();

        await using (var dbContext = CreateDbContext())
        {
            var importer = CreateImporter(dbContext);
            var first = await importer.ImportAsync(package);
            var second = await importer.ImportAsync(package);

            Assert.Equal(ClinicalDefinitionImportOutcome.Imported, first.Outcome);
            Assert.Equal(ClinicalDefinitionImportOutcome.AlreadyImported, second.Outcome);

            var changedQuestionnaire = QuestionnaireDefinitionVersion.Import(
                package.Pathway,
                package.Questionnaire.QuestionnaireCode,
                package.Version,
                DefinitionHash.FromHash(new string('f', 64)),
                package.ContentStatus,
                package.Questionnaire.ImportedAt,
                activatedAt: package.Questionnaire.ActivatedAt,
                sourceReference: package.Questionnaire.SourceReference,
                questions: package.Questionnaire.Questions.Select(value =>
                    new TriageQuestionInput(
                        value.Code,
                        value.PromptText,
                        value.DisplayOrder,
                        value.AnswerSchemaJson,
                        value.BranchingMetadataJson)));
            var changedPackage = new ClinicalDefinitionPackage(
                package.Pathway,
                changedQuestionnaire,
                package.RuleSet,
                package.Questions,
                package.Branches,
                package.RuleDefinitions);
            await Assert.ThrowsAsync<ClinicalDefinitionValidationException>(() =>
                importer.ImportAsync(changedPackage));
        }

        await using (var verify = CreateDbContext())
        {
            var questionnaire = await verify.QuestionnaireVersions
                .AsNoTracking()
                .Include(value => value.Questions)
                .SingleAsync(value =>
                    value.QuestionnaireCode == package.Questionnaire.QuestionnaireCode &&
                    value.Version == package.Version);
            var ruleSet = await verify.ClinicalRuleSetVersions
                .AsNoTracking()
                .SingleAsync(value =>
                    value.RuleSetCode == package.RuleSet.RuleSetCode &&
                    value.Version == package.Version);

            Assert.Equal(41, questionnaire.Questions.Count);
            Assert.Equal(41, questionnaire.Questions.Select(value => value.Code).Distinct().Count());
            Assert.Equal(ClinicalPathways.AbdominalPain, questionnaire.Pathway);
            Assert.Equal(
                ClinicalContentSource.ReferencePlatformDerived,
                questionnaire.ContentSource);
            Assert.Equal(ClinicalReviewStatus.Provisional, questionnaire.ReviewStatus);
            Assert.Equal(
                ClinicalApprovalStatus.PendingFormalReview,
                questionnaire.ApprovalStatus);
            Assert.Null(questionnaire.ApprovedAt);
            Assert.NotNull(questionnaire.ActivatedAt);
            Assert.Equal(questionnaire.ContentStatus, ruleSet.ContentStatus);
            Assert.Equal(ClinicalPathways.AbdominalPain, ruleSet.Pathway);
            Assert.StartsWith("{", ruleSet.DefinitionMetadataJson, StringComparison.Ordinal);
        }

        await using (var providerContext = CreateDbContext())
        {
            var provider = new ClinicalDefinitionProvider(
                providerContext,
                new ClinicalDefinitionPackageValidator());
            var registry = new ClinicalPathwayRegistry(provider);
            var active = await provider.GetActiveDefinitionAsync(ClinicalPathways.AbdominalPain);

            Assert.NotNull(active);
            Assert.Equal(package.Version, active.Version);
            Assert.Equal(41, active.Questions.Count);
            Assert.Equal(13, active.RuleDefinitions.RedFlags.Count);
            Assert.DoesNotContain(
                active.RuleDefinitions.Rules,
                value => value.MinimumUrgency == ClinicalUrgencies.Critical);

            foreach (var unsupported in ClinicalPathways.Recognized.Except(
                ClinicalPathways.Supported))
            {
                var resolution = await registry.ResolveAsync(unsupported.Value);
                Assert.Equal(
                    ClinicalPathwayResolutionStatus.RecognizedButUnsupported,
                    resolution.Status);
                Assert.Null(resolution.ActiveDefinition);
            }

            Assert.Equal(
                ClinicalPathwayResolutionStatus.Unknown,
                (await registry.ResolveAsync("UNKNOWN_SYMPTOM")).Status);
        }
    }

    [Fact]
    public async Task HistoricalVersions_CanCoexistAndQuestionnaireDeleteIsRestricted()
    {
        await EnsureMigratedAsync();
        await DeletePackageFixtureAsync();
        var package = AbdominalPainProvisionalPackage.Create();

        await using (var dbContext = CreateDbContext())
        {
            await CreateImporter(dbContext).ImportAsync(package);
        }

        var syntheticVersion = DefinitionVersion.Create("synthetic-v2-metadata-only");
        var syntheticQuestionnaire = QuestionnaireDefinitionVersion.Import(
            package.Pathway,
            package.Questionnaire.QuestionnaireCode,
            syntheticVersion,
            package.Questionnaire.ContentHash,
            package.ContentStatus,
            package.Questionnaire.ImportedAt.AddDays(1),
            sourceReference: "test-only-metadata-coexistence-fixture");
        var syntheticRuleSet = ClinicalRuleSetVersion.Import(
            package.Pathway,
            package.RuleSet.RuleSetCode,
            syntheticVersion,
            package.RuleSet.ContentHash,
            package.ContentStatus,
            package.RuleSet.DefinitionMetadataJson,
            package.RuleSet.ImportedAt.AddDays(1),
            sourceReference: "test-only-metadata-coexistence-fixture");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(syntheticQuestionnaire, syntheticRuleSet);
            await dbContext.SaveChangesAsync();
        }

        await using (var verify = CreateDbContext())
        {
            Assert.Equal(2, await verify.QuestionnaireVersions.CountAsync(value =>
                value.QuestionnaireCode == package.Questionnaire.QuestionnaireCode));
            Assert.Equal(2, await verify.ClinicalRuleSetVersions.CountAsync(value =>
                value.RuleSetCode == package.RuleSet.RuleSetCode));

            var persistedV1 = await verify.QuestionnaireVersions.SingleAsync(value =>
                value.QuestionnaireCode == package.Questionnaire.QuestionnaireCode &&
                value.Version == package.Version);
            verify.QuestionnaireVersions.Remove(persistedV1);
            await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
        }
    }

    private ClinicalDefinitionImporter CreateImporter(BeeexyDbContext dbContext)
    {
        return new ClinicalDefinitionImporter(
            dbContext,
            new ClinicalDefinitionPackageValidator(),
            NullLogger<ClinicalDefinitionImporter>.Instance);
    }

    private async Task DeletePackageFixtureAsync()
    {
        await using var dbContext = CreateDbContext();
        var questionnaireCodes = await dbContext.QuestionnaireVersions
            .Where(value => value.QuestionnaireCode ==
                QuestionnaireCode.Create(
                    AbdominalPainProvisionalPackage.QuestionnaireIdentifier))
            .Select(value => value.Id)
            .ToArrayAsync();
        await dbContext.TriageQuestions
            .Where(value => questionnaireCodes.Contains(value.QuestionnaireVersionId))
            .ExecuteDeleteAsync();
        await dbContext.QuestionnaireVersions
            .Where(value => value.QuestionnaireCode ==
                QuestionnaireCode.Create(
                    AbdominalPainProvisionalPackage.QuestionnaireIdentifier))
            .ExecuteDeleteAsync();
        await dbContext.ClinicalRuleSetVersions
            .Where(value => value.RuleSetCode ==
                RuleSetCode.Create(AbdominalPainProvisionalPackage.RuleSetIdentifier))
            .ExecuteDeleteAsync();
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }
}
