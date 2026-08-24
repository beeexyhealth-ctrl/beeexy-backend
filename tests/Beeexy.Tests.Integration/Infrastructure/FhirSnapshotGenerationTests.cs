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
public sealed class FhirSnapshotGenerationTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private readonly string artifactRoot = Path.Combine(
        Path.GetTempPath(),
        $"beeexy-fhir-integration-{Guid.NewGuid():N}");
    private readonly List<GenerationGraph> persistedGraphs = [];

    [Fact]
    public async Task Generate_PersistsGeneratedExportAndChecksumOfExactPrivateBytes()
    {
        await EnsureMigratedAsync();
        var graph = await PersistGraphAsync();
        var command = Command(graph, EntityId.New());

        var result = await GenerateAsync(command);

        await using var verify = CreateDbContext();
        var saved = await verify.FhirExports.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == result.Export.Id);
        var reference = FhirArtifactStorageReference.FromPrivateUri(
            saved.PrivateArtifactStorageUri!);
        var bytes = await new FileSystemFhirArtifactStore(artifactRoot)
            .ReadAsync(reference);
        Assert.Equal(FhirExportStatus.Generated, saved.Status);
        Assert.Equal(FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
            saved.FhirVersion);
        Assert.Null(saved.ProfileCanonical);
        Assert.Null(saved.ProfileVersion);
        Assert.Equal(new FhirArtifactChecksumCalculator().Calculate(bytes), saved.Checksum);
        Assert.Empty(await verify.FhirValidationResults
            .Where(candidate => candidate.FhirExportId == saved.Id)
            .ToListAsync());
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(FhirSnapshotArtifactFormat.ArtifactKind, json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("application/fhir+json", json,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_IdempotencyIsPerPatientAndDifferentKeysCreateDistinctArtifacts()
    {
        await EnsureMigratedAsync();
        var firstGraph = await PersistGraphAsync();
        var secondGraph = await PersistGraphAsync();
        var sharedKey = EntityId.New();
        var first = await GenerateAsync(Command(firstGraph, sharedKey));
        var repeated = await GenerateAsync(Command(firstGraph, sharedKey));
        var differentKey = await GenerateAsync(Command(firstGraph, EntityId.New()));
        var otherPatient = await GenerateAsync(Command(secondGraph, sharedKey));

        Assert.True(first.NewlyGenerated);
        Assert.False(repeated.NewlyGenerated);
        Assert.Equal(first.Export.Id, repeated.Export.Id);
        Assert.NotEqual(first.Export.Id, differentKey.Export.Id);
        Assert.NotEqual(first.Export.Id, otherPatient.Export.Id);
        await using var verify = CreateDbContext();
        Assert.Equal(2, await verify.FhirExports.CountAsync(candidate =>
            candidate.PatientProfileId == firstGraph.Patient.Id));
        Assert.Single(await verify.FhirExports.Where(candidate =>
            candidate.PatientProfileId == secondGraph.Patient.Id).ToListAsync());
        Assert.Equal(3, Directory.GetFiles(artifactRoot, "*.snapshot").Length);
    }

    [Fact]
    public async Task Generate_ConcurrentSameIdempotencyProducesOneExportAndArtifact()
    {
        await EnsureMigratedAsync();
        var graph = await PersistGraphAsync();
        var command = Command(graph, EntityId.New());

        var results = await Task.WhenAll(
            GenerateAsync(command),
            GenerateAsync(command));

        Assert.Single(results.Select(result => result.Export.Id).Distinct());
        Assert.Single(results.Where(result => result.NewlyGenerated));
        Assert.Single(results.Where(result => !result.NewlyGenerated));
        await using var verify = CreateDbContext();
        Assert.Single(await verify.FhirExports.Where(candidate =>
            candidate.PatientProfileId == graph.Patient.Id &&
            candidate.IdempotencyKey == command.IdempotencyKey).ToListAsync());
        Assert.Single(Directory.GetFiles(artifactRoot, "*.snapshot"));
    }

    [Fact]
    public async Task Generate_StorageFailureRollsBackPendingExport()
    {
        await EnsureMigratedAsync();
        var graph = await PersistGraphAsync();
        var command = Command(graph, EntityId.New());
        await using var context = CreateDbContext();
        await using var transaction = new FhirExportGenerationTransaction(context);
        var generator = new GenerateFhirExport(
            new FixedClock(Utc(18)),
            transaction,
            new ThrowingArtifactStore(),
            new FhirSnapshotSerializer(),
            new FhirArtifactChecksumCalculator());

        await Assert.ThrowsAsync<IOException>(() => generator.ExecuteAsync(command));

        await transaction.DisposeAsync();
        await using var verify = CreateDbContext();
        Assert.Empty(await verify.FhirExports.Where(candidate =>
            candidate.PatientProfileId == graph.Patient.Id &&
            candidate.IdempotencyKey == command.IdempotencyKey).ToListAsync());
    }

    private async Task<GenerateFhirExportResult> GenerateAsync(
        GenerateFhirExportCommand command)
    {
        await using var context = CreateDbContext();
        await using var transaction = new FhirExportGenerationTransaction(context);
        var generator = new GenerateFhirExport(
            new FixedClock(Utc(18)),
            transaction,
            new FileSystemFhirArtifactStore(artifactRoot),
            new FhirSnapshotSerializer(),
            new FhirArtifactChecksumCalculator());
        return await generator.ExecuteAsync(command);
    }

    private static GenerateFhirExportCommand Command(
        GenerationGraph graph,
        EntityId idempotencyKey) => new(
            graph.Patient.Id,
            graph.HistoryEvent.Id,
            idempotencyKey,
            FhirMappingSpecificationIdentity.Create("phase-6.5-integration"),
            "6.5-integration-runtime");

    private async Task<GenerationGraph> PersistGraphAsync()
    {
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-FHIR65-{Guid.NewGuid():N}"),
            Utc(10));
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"fhir65-{Guid.NewGuid():N}"),
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
            RuleSetCode.Create($"fhir65-{Guid.NewGuid():N}"),
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
            "\"persistent historical answer\"",
            1,
            Utc(13));
        var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, Utc(14));
        var assessment = ClinicalAssessment.CreateNeutral(episode, Utc(15));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(16));
        var graph = new GenerationGraph(
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

    private sealed class ThrowingArtifactStore : IFhirArtifactStore
    {
        public Task StoreImmutableAsync(FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default) =>
            throw new IOException("simulated private storage failure");

        public Task<byte[]> ReadAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record GenerationGraph(
        PatientProfile Patient,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);
}
