using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ClinicalAiBoundaryIntegrationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task ImportedDemoDefinition_ValidatesCandidatesWithoutCreatingClinicalState()
    {
        await EnsurePackageImportedAsync();
        await using var dbContext = CreateDbContext();
        var before = await WorkflowCountsAsync(dbContext);
        var output = ValidOutput(
        [
            new ClinicalAiFactCandidate(
                QuestionCode.Create("INTENSITY"),
                new ClinicalAiIntegerValue(4),
                ClinicalAiConfidenceSignal.Sufficient),
            new ClinicalAiFactCandidate(
                QuestionCode.Create("DURATION"),
                new ClinicalAiDurationValue(1, ClinicalDurationUnit.Days),
                ClinicalAiConfidenceSignal.Sufficient)
        ]);
        var interpreter = CreateInterpreter(dbContext, output);

        var result = await interpreter.ExecuteAsync(new ClinicalAiInterpretationRequest(
            "My stomach pain started gradually and is four out of ten.",
            ClinicalPathways.AbdominalPain,
            allowedFactCodes:
            [
                QuestionCode.Create("INTENSITY"),
                QuestionCode.Create("DURATION")
            ]));

        Assert.Equal(ClinicalAiInterpretationOutcome.Accepted, result.Outcome);
        Assert.Equal(ClinicalAiValidationOutcome.Accepted, result.Validation!.Outcome);
        Assert.Equal(ClinicalPathways.AbdominalPain, result.Validation.Pathway);
        Assert.All(result.Validation.Facts, fact =>
            Assert.Equal(ClinicalAiCandidateStatus.AcceptedCandidate, fact.Status));
        Assert.Equal(before, await WorkflowCountsAsync(dbContext));
    }

    [Fact]
    public async Task RealRegistry_RefusesUnsupportedAndUnknownPathwaysWithoutCrossMapping()
    {
        await EnsurePackageImportedAsync();
        await using var dbContext = CreateDbContext();
        var before = await WorkflowCountsAsync(dbContext);
        var unsupported = await CreateInterpreter(
            dbContext,
            ValidOutput(pathway: "RESPIRATORY_SYMPTOMS")).ExecuteAsync(
                new ClinicalAiInterpretationRequest("I have respiratory symptoms."));
        var unknown = await CreateInterpreter(
            dbContext,
            ValidOutput(pathway: "AI_INVENTED_PATHWAY")).ExecuteAsync(
                new ClinicalAiInterpretationRequest("I have an unusual symptom."));

        Assert.Equal(ClinicalAiInterpretationOutcome.Unsupported, unsupported.Outcome);
        Assert.Equal(
            ClinicalPathwayResolutionStatus.RecognizedButUnsupported,
            unsupported.Validation!.PathwayStatus);
        Assert.Empty(unsupported.Validation.Facts);
        Assert.Equal(
            ClinicalAiInterpretationOutcome.InvalidProviderOutput,
            unknown.Outcome);
        Assert.Equal(
            ClinicalPathwayResolutionStatus.Unknown,
            unknown.Validation!.PathwayStatus);
        Assert.Empty(unknown.Validation.Facts);
        Assert.Equal(before, await WorkflowCountsAsync(dbContext));
    }

    [Fact]
    public async Task RealDefinitionProvider_RejectsUnknownFactAndForbiddenAuthorityWithoutWrites()
    {
        await EnsurePackageImportedAsync();
        await using var dbContext = CreateDbContext();
        var before = await WorkflowCountsAsync(dbContext);
        var unknownFact = await CreateInterpreter(
            dbContext,
            ValidOutput(
            [
                new ClinicalAiFactCandidate(
                    QuestionCode.Create("AI_INVENTED_FACT"),
                    new ClinicalAiTextValue("invented"),
                    ClinicalAiConfidenceSignal.Sufficient)
            ])).ExecuteAsync(new ClinicalAiInterpretationRequest(
                "I have stomach pain.",
                ClinicalPathways.AbdominalPain));
        var forbiddenAuthority = await CreateInterpreter(
            dbContext,
            ValidOutput() with
            {
                SchemaViolations = [ClinicalAiOutputViolation.ForbiddenClinicalAuthority]
            }).ExecuteAsync(new ClinicalAiInterpretationRequest(
                "I have stomach pain.",
                ClinicalPathways.AbdominalPain));

        Assert.Equal(
            ClinicalAiInterpretationOutcome.InvalidProviderOutput,
            unknownFact.Outcome);
        Assert.Contains(
            ClinicalAiValidationIssue.UnknownFactCode,
            unknownFact.Validation!.Issues);
        Assert.Equal(
            ClinicalAiInterpretationOutcome.InvalidProviderOutput,
            forbiddenAuthority.Outcome);
        Assert.Contains(
            ClinicalAiValidationIssue.ForbiddenClinicalAuthority,
            forbiddenAuthority.Validation!.Issues);
        Assert.Equal(before, await WorkflowCountsAsync(dbContext));
    }

    [Theory]
    [InlineData("CHEST_PAIN")]
    [InlineData("OTHER_SYMPTOMS")]
    public async Task ExpandedDemoDefinition_RejectsAiValueOutsideControlledVocabulary(
        string pathway)
    {
        await EnsurePackageImportedAsync();
        await using var dbContext = CreateDbContext();
        var before = await WorkflowCountsAsync(dbContext);
        var output = ValidOutput(
        [
            new ClinicalAiFactCandidate(
                QuestionCode.Create("ADDITIONAL_SYMPTOMS"),
                new ClinicalAiMultipleChoiceValue(["COUGH"]),
                ClinicalAiConfidenceSignal.Sufficient)
        ], pathway);

        var result = await CreateInterpreter(dbContext, output).ExecuteAsync(
            new ClinicalAiInterpretationRequest(
                "A provider candidate outside the controlled demo vocabulary.",
                ClinicalPathwayCode.Create(pathway),
                allowedFactCodes: [QuestionCode.Create("ADDITIONAL_SYMPTOMS")]));

        Assert.Equal(ClinicalAiInterpretationOutcome.InvalidProviderOutput, result.Outcome);
        Assert.Contains(ClinicalAiValidationIssue.InvalidChoice, result.Validation!.Issues);
        Assert.Empty(result.Validation.Facts.Where(fact =>
            fact.Status == ClinicalAiCandidateStatus.AcceptedCandidate));
        Assert.Equal(before, await WorkflowCountsAsync(dbContext));
    }

    private InterpretClinicalInput CreateInterpreter(
        BeeexyDbContext dbContext,
        ClinicalAiProviderOutput output)
    {
        var definitionProvider = new ClinicalDefinitionProvider(
            dbContext,
            new ClinicalDefinitionPackageValidator());
        var registry = new ClinicalPathwayRegistry(definitionProvider);
        return new InterpretClinicalInput(
            new DeterministicProvider(output),
            new ClinicalSafetyPolicy(),
            new ClinicalAiOutputValidator(registry));
    }

    private async Task EnsurePackageImportedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var importer = new ClinicalDefinitionImporter(
            dbContext,
            new ClinicalDefinitionPackageValidator(),
            NullLogger<ClinicalDefinitionImporter>.Instance);
        await importer.ImportAsync(AbdominalPainProvisionalPackage.Create());
        foreach (var package in SimplifiedDemoDefinitionPackages.CreateAll())
        {
            await importer.ImportAsync(package);
        }
    }

    private static async Task<(int Sessions, int Episodes, int Assessments)> WorkflowCountsAsync(
        BeeexyDbContext dbContext)
    {
        return (
            await dbContext.PreTriageSessions.CountAsync(),
            await dbContext.PreTriageEpisodes.CountAsync(),
            await dbContext.ClinicalAssessments.CountAsync());
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private static ClinicalAiProviderOutput ValidOutput(
        IReadOnlyList<ClinicalAiFactCandidate>? facts = null,
        string pathway = "ABDOMINAL_PAIN")
    {
        return new ClinicalAiProviderOutput(
            ClinicalAiProviderOutput.CurrentSchemaVersion,
            ClinicalIntentClassification.PreTriageInput,
            pathway,
            facts ?? [],
            [],
            [],
            false,
            []);
    }

    private sealed class DeterministicProvider(ClinicalAiProviderOutput output)
        : IClinicalAiProvider
    {
        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(output);
        }
    }
}
