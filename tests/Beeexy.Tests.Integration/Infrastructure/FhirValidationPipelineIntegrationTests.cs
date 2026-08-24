using System.Text;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Interoperability;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class FhirValidationPipelineIntegrationTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private const string TestRelease = "controlled-test-release";
    private const string TestMapping = "phase-6.6-controlled-test";
    private readonly string artifactRoot = Path.Combine(
        Path.GetTempPath(),
        $"beeexy-fhir-validation-integration-{Guid.NewGuid():N}");
    private readonly List<ValidationGraph> persistedGraphs = [];

    [Fact]
    public async Task CurrentSnapshot_RepeatedValidationRemainsBlockedAndGenerated()
    {
        await EnsureMigratedAsync();
        var fixture = await PersistGeneratedExportAsync(releaseNeutral: true);
        var validator = new ControlledValidator(_ =>
            FhirValidatorExecutionResult.Valid(TestValidatorMetadata()));

        var first = await ValidateAsync(
            fixture,
            new CurrentFhirValidationPrerequisiteEvaluator(),
            validator);
        var second = await ValidateAsync(
            fixture,
            new CurrentFhirValidationPrerequisiteEvaluator(),
            validator);

        Assert.Equal(FhirValidationPipelineStatus.Blocked, first.PipelineStatus);
        Assert.Equal(FhirValidationPipelineStatus.Blocked, second.PipelineStatus);
        Assert.Contains(
            FhirValidationBlocker.ReleaseNeutralArtifact,
            first.Eligibility!.Blockers);
        Assert.Equal(0, validator.InvocationCount);
        await AssertPersistedAsync(
            fixture,
            FhirExportStatus.Generated,
            expectedResultCount: 0);
        Assert.Equal(
            fixture.Bytes,
            await new FileSystemFhirArtifactStore(artifactRoot)
                .ReadAsync(fixture.Reference));
    }

    [Fact]
    public async Task TamperedBytes_FailIntegrityBeforeEligibilityOrValidator()
    {
        await EnsureMigratedAsync();
        var fixture = await PersistGeneratedExportAsync();
        var eligibility = new CountingEligibility(Eligible());
        var validator = new ControlledValidator(_ =>
            FhirValidatorExecutionResult.Valid(TestValidatorMetadata()));
        var tampered = Encoding.UTF8.GetBytes("tampered private artifact bytes");

        var result = await ValidateAsync(
            fixture,
            eligibility,
            validator,
            new FixedReadArtifactStore(tampered));

        Assert.Equal(FhirValidationPipelineStatus.IntegrityFailed, result.PipelineStatus);
        Assert.Equal(0, eligibility.InvocationCount);
        Assert.Equal(0, validator.InvocationCount);
        await AssertPersistedAsync(
            fixture,
            FhirExportStatus.Generated,
            expectedResultCount: 0);
        Assert.Equal(
            fixture.Bytes,
            await new FileSystemFhirArtifactStore(artifactRoot)
                .ReadAsync(fixture.Reference));
    }

    [Theory]
    [InlineData(true, FhirExportStatus.Validated, FhirValidationOutcome.Passed)]
    [InlineData(false, FhirExportStatus.ValidationFailed, FhirValidationOutcome.Failed)]
    public async Task ControlledValidatorResult_PersistsAtomicFinalLifecycle(
        bool isValid,
        FhirExportStatus expectedStatus,
        FhirValidationOutcome expectedOutcome)
    {
        await EnsureMigratedAsync();
        var fixture = await PersistGeneratedExportAsync();
        var validator = new ControlledValidator(_ => isValid
            ? FhirValidatorExecutionResult.Valid(
                TestValidatorMetadata(),
                [new(FhirValidationDiagnosticSeverity.Warning, "TEST.warning", "private")])
            : FhirValidatorExecutionResult.Invalid(
                TestValidatorMetadata(),
                [new(FhirValidationDiagnosticSeverity.Error, "TEST.error", "private")]
            ));

        var result = await ValidateAsync(
            fixture,
            new CountingEligibility(Eligible()),
            validator);

        Assert.True(result.NewlyCompleted);
        Assert.Equal(1, validator.InvocationCount);
        Assert.Equal(fixture.Bytes, validator.LastRequest!.ArtifactBytes.ToArray());
        Assert.Equal(fixture.Export.Checksum, validator.LastRequest.ArtifactChecksum);
        Assert.Equal(expectedOutcome, result.ValidationResult!.Outcome);
        Assert.Equal(TestRelease, result.Eligibility!.Specification!.FhirRelease);
        Assert.Equal(TestMapping, result.Eligibility.Specification.MappingVersion);
        await AssertPersistedAsync(fixture, expectedStatus, expectedResultCount: 1);
    }

    [Fact]
    public async Task ValidatorFailure_RemainsRetryableAndLaterSuccessCompletes()
    {
        await EnsureMigratedAsync();
        var fixture = await PersistGeneratedExportAsync();
        var unavailable = new ControlledValidator(_ =>
            FhirValidatorExecutionResult.Unavailable());

        var first = await ValidateAsync(
            fixture,
            new CountingEligibility(Eligible()),
            unavailable);

        Assert.Equal(FhirValidationPipelineStatus.ValidatorUnavailable, first.PipelineStatus);
        await AssertPersistedAsync(
            fixture,
            FhirExportStatus.Generated,
            expectedResultCount: 0);

        var available = new ControlledValidator(_ =>
            FhirValidatorExecutionResult.Valid(TestValidatorMetadata()));
        var retry = await ValidateAsync(
            fixture,
            new CountingEligibility(Eligible()),
            available);

        Assert.Equal(FhirValidationPipelineStatus.Validated, retry.PipelineStatus);
        Assert.True(retry.NewlyCompleted);
        await AssertPersistedAsync(
            fixture,
            FhirExportStatus.Validated,
            expectedResultCount: 1);
    }

    [Fact]
    public async Task ConcurrentValidation_IsSerializedAndCreatesOneFinalResult()
    {
        await EnsureMigratedAsync();
        var fixture = await PersistGeneratedExportAsync();
        var validator = new ControlledValidator(
            _ => FhirValidatorExecutionResult.Valid(TestValidatorMetadata()),
            delay: TimeSpan.FromMilliseconds(150));

        var results = await Task.WhenAll(
            ValidateAsync(fixture, new CountingEligibility(Eligible()), validator),
            ValidateAsync(fixture, new CountingEligibility(Eligible()), validator));

        Assert.Equal(1, validator.InvocationCount);
        Assert.Single(results.Where(result => result.NewlyCompleted));
        Assert.Single(results.Where(result => !result.NewlyCompleted));
        Assert.All(results, result =>
            Assert.Equal(FhirValidationPipelineStatus.Validated, result.PipelineStatus));
        await AssertPersistedAsync(
            fixture,
            FhirExportStatus.Validated,
            expectedResultCount: 1);
    }

    private async Task<ValidateFhirExportResult> ValidateAsync(
        ValidationFixture fixture,
        IFhirValidationPrerequisiteEvaluator eligibility,
        IFhirValidator validator,
        IFhirArtifactStore? artifactStore = null)
    {
        await using var context = CreateDbContext();
        await using var transaction = new FhirExportValidationTransaction(context);
        var useCase = new ValidateFhirExport(
            new FixedClock(Utc(19)),
            transaction,
            artifactStore ?? new FileSystemFhirArtifactStore(artifactRoot),
            new FhirArtifactChecksumCalculator(),
            eligibility,
            validator,
            new FhirValidationDiagnosticSanitizer());
        return await useCase.ExecuteAsync(new ValidateFhirExportCommand(
            fixture.Graph.Patient.Id,
            fixture.Export.Id));
    }

    private async Task<ValidationFixture> PersistGeneratedExportAsync(
        bool releaseNeutral = false)
    {
        var graph = await PersistGraphAsync();
        var bytes = Encoding.UTF8.GetBytes(
            releaseNeutral
                ? "release-neutral Phase 6.5 snapshot"
                : "controlled test-only official FHIR artifact");
        var reference = FhirArtifactStorageReference.CreateNew();
        var store = new FileSystemFhirArtifactStore(artifactRoot);
        await store.StoreImmutableAsync(reference, bytes);
        var checksum = new FhirArtifactChecksumCalculator().Calculate(bytes);
        var export = FhirExport.CreatePending(
            graph.HistoryEvent,
            releaseNeutral
                ? FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker
                : TestRelease,
            releaseNeutral
                ? TestMapping
                : TestMapping,
            EntityId.New(),
            Utc(17));
        export.MarkGenerated(
            FhirArtifactMetadata.Create(
                FhirArtifactChecksumCalculator.Algorithm,
                checksum,
                reference.PrivateUri),
            Utc(18));
        await using var context = CreateDbContext();
        context.FhirExports.Add(export);
        await context.SaveChangesAsync();
        return new ValidationFixture(graph, export, reference, bytes);
    }

    private static FhirValidationEligibility Eligible() =>
        FhirValidationEligibility.Eligible(FhirValidationSpecification.Create(
            TestRelease,
            TestMapping,
            FhirProfileResolution.NotApplicable()));

    private static FhirValidatorMetadata TestValidatorMetadata() =>
        FhirValidatorMetadata.Create("controlled-test-validator", "6.6-test");

    private async Task AssertPersistedAsync(
        ValidationFixture fixture,
        FhirExportStatus expectedStatus,
        int expectedResultCount)
    {
        await using var verify = CreateDbContext();
        var export = await verify.FhirExports.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == fixture.Export.Id);
        Assert.Equal(expectedStatus, export.Status);
        Assert.Equal(fixture.Export.ChecksumAlgorithm, export.ChecksumAlgorithm);
        Assert.Equal(fixture.Export.Checksum, export.Checksum);
        Assert.Equal(
            fixture.Export.PrivateArtifactStorageUri,
            export.PrivateArtifactStorageUri);
        Assert.Equal(expectedResultCount, await verify.FhirValidationResults.CountAsync(
            candidate => candidate.FhirExportId == fixture.Export.Id));
    }

    private async Task<ValidationGraph> PersistGraphAsync()
    {
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-FHIR66-{Guid.NewGuid():N}"),
            Utc(10));
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"fhir66-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("historical-v1"),
            DefinitionHash.FromHash(new string('a', 64)),
            Utc(10),
            Utc(11),
            questions:
            [
                new TriageQuestionInput(
                    QuestionCode.Create("SYMPTOM_TEXT"),
                    "Describe the symptom",
                    1,
                    "{\"type\":\"string\"}",
                    Id: EntityId.New())
            ]);
        var ruleSet = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"fhir66-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("historical-v1"),
            DefinitionHash.FromHash(new string('b', 64)),
            Utc(10),
            Utc(11));
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            Utc(20),
            Utc(12));
        session.RecordAnswer(
            Assert.Single(questionnaire.Questions),
            "\"historical answer\"",
            1,
            Utc(13));
        var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, Utc(14));
        var assessment = ClinicalAssessment.CreateNeutral(episode, Utc(15));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(16));
        var graph = new ValidationGraph(
            patient,
            questionnaire,
            ruleSet,
            session,
            episode,
            assessment,
            historyEvent);
        await using var context = CreateDbContext();
        context.AddRange(patient, questionnaire, ruleSet, session, episode,
            assessment, historyEvent);
        await context.SaveChangesAsync();
        persistedGraphs.Add(graph);
        return graph;
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (persistedGraphs.Count != 0)
        {
            await using var context = CreateDbContext();
            foreach (var graph in persistedGraphs)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM interoperability.fhir_validation_results
                    WHERE fhir_export_id IN (
                        SELECT id FROM interoperability.fhir_exports
                        WHERE patient_profile_id = {graph.Patient.Id.Value});
                    DELETE FROM interoperability.fhir_exports
                    WHERE patient_profile_id = {graph.Patient.Id.Value};
                    DELETE FROM history.pre_triage_projection_records
                    WHERE patient_profile_id = {graph.Patient.Id.Value};
                    DELETE FROM history.clinical_history_events
                    WHERE patient_profile_id = {graph.Patient.Id.Value};
                    DELETE FROM triage.clinical_findings
                    WHERE assessment_id = {graph.Assessment.Id.Value};
                    DELETE FROM triage.clinical_assessments
                    WHERE episode_id = {graph.Episode.Id.Value};
                    DELETE FROM triage.answers
                    WHERE episode_id = {graph.Episode.Id.Value}
                       OR session_id = {graph.Session.Id.Value};
                    DELETE FROM triage.reported_symptoms
                    WHERE episode_id = {graph.Episode.Id.Value}
                       OR session_id = {graph.Session.Id.Value};
                    DELETE FROM triage.pre_triage_episodes
                    WHERE id = {graph.Episode.Id.Value};
                    DELETE FROM triage.pre_triage_sessions
                    WHERE id = {graph.Session.Id.Value};
                    DELETE FROM triage.questions
                    WHERE questionnaire_version_id = {graph.Questionnaire.Id.Value};
                    DELETE FROM triage.questionnaire_versions
                    WHERE id = {graph.Questionnaire.Id.Value};
                    DELETE FROM triage.clinical_rule_set_versions
                    WHERE id = {graph.RuleSet.Id.Value};
                    DELETE FROM patients.patient_profiles
                    WHERE id = {graph.Patient.Id.Value};
                    """);
            }
        }

        if (Directory.Exists(artifactRoot))
        {
            Directory.Delete(artifactRoot, recursive: true);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class ControlledValidator(
        Func<FhirValidatorRequest, FhirValidatorExecutionResult> result,
        TimeSpan? delay = null) : IFhirValidator
    {
        private int invocationCount;

        public int InvocationCount => Volatile.Read(ref invocationCount);

        public FhirValidatorRequest? LastRequest { get; private set; }

        public async Task<FhirValidatorExecutionResult> ValidateAsync(
            FhirValidatorRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref invocationCount);
            LastRequest = request;
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return result(request);
        }
    }

    private sealed class CountingEligibility(FhirValidationEligibility result)
        : IFhirValidationPrerequisiteEvaluator
    {
        public int InvocationCount { get; private set; }

        public FhirValidationEligibility Evaluate(FhirExport export)
        {
            InvocationCount++;
            return result;
        }
    }

    private sealed class FixedReadArtifactStore(byte[] bytes) : IFhirArtifactStore
    {
        public Task StoreImmutableAsync(
            FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(bytes.ToArray());

        public Task<bool> DeleteAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record ValidationFixture(
        ValidationGraph Graph,
        FhirExport Export,
        FhirArtifactStorageReference Reference,
        byte[] Bytes);

    private sealed record ValidationGraph(
        PatientProfile Patient,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);
}
