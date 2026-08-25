using Beeexy.Application.Interoperability;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Triage;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class FhirExportAccessTests
{
    [Fact]
    public async Task Get_ReturnsSafeValidatedMetadataForAuthorizedSourcePatient()
    {
        var fixture = new Fixture();
        var (export, validation, _) = fixture.ValidatedExport();
        fixture.Repository.State = new FhirExportReadState(export, validation);

        var result = await fixture.Get().ExecuteAsync(export.Id);

        Assert.Equal(FhirExportStatus.Validated, result.Status);
        Assert.Equal(FhirR4BaseMvp.FhirRelease, result.FhirVersion);
        Assert.Equal(FhirR4BaseMvp.MappingVersion, result.MappingVersion);
        Assert.Equal(FhirValidationOutcome.Passed, result.Validation!.Outcome);
        Assert.DoesNotContain(
            typeof(FhirExportMetadata).GetProperties(),
            property => property.Name.Contains("Checksum", StringComparison.Ordinal) ||
                property.Name.Contains("Storage", StringComparison.Ordinal) ||
                property.Name.Contains("Artifact", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_AbsentAndUnauthorizedExportsUseSameConcealedFailure()
    {
        var fixture = new Fixture();
        var absent = await Assert.ThrowsAsync<FhirExportNotFoundException>(
            () => fixture.Get().ExecuteAsync(EntityId.New()));
        var (export, _, _) = fixture.ValidatedExport(EntityId.New());
        fixture.Repository.State = new FhirExportReadState(export, null);

        var denied = await Assert.ThrowsAsync<FhirExportNotFoundException>(
            () => fixture.Get().ExecuteAsync(export.Id));

        Assert.Equal(absent.Message, denied.Message);
    }

    [Fact]
    public async Task Download_ReturnsExactVerifiedBytesAndAuditsAuthorizedAccess()
    {
        var fixture = new Fixture();
        var (export, validation, bytes) = fixture.ValidatedExport();
        fixture.Repository.State = new FhirExportReadState(export, validation);

        var result = await fixture.Download().ExecuteAsync(export.Id);

        Assert.Equal(bytes, result.ArtifactBytes);
        Assert.Equal(FhirR4BaseMvp.MediaType, result.MediaType);
        Assert.Equal(
            $"beeexy-fhir-export-{export.Id.Value:D}.json",
            result.FileName);
        Assert.Equal([export.Id], fixture.Audit.Downloads);
        Assert.Empty(fixture.Audit.IntegrityFailures);
    }

    [Fact]
    public async Task Download_TamperedArtifactIsRejectedWithoutRegeneration()
    {
        var fixture = new Fixture();
        var (export, validation, _) = fixture.ValidatedExport();
        fixture.Repository.State = new FhirExportReadState(export, validation);
        fixture.Store.Bytes = "tampered"u8.ToArray();

        await Assert.ThrowsAsync<FhirExportArtifactIntegrityException>(
            () => fixture.Download().ExecuteAsync(export.Id));

        Assert.Equal("tampered"u8.ToArray(), fixture.Store.Bytes);
        Assert.Empty(fixture.Audit.Downloads);
        Assert.Equal([export.Id], fixture.Audit.IntegrityFailures);
        Assert.Equal(1, fixture.Store.ReadCount);
        Assert.Equal(0, fixture.Store.WriteCount);
    }

    [Fact]
    public async Task Download_NonValidatedAndLegacyArtifactsAreConflictsBeforeStorageRead()
    {
        var fixture = new Fixture();
        var generated = fixture.GeneratedExport(
            FhirR4BaseMvp.FhirRelease,
            FhirR4BaseMvp.MappingVersion);
        fixture.Repository.State = new FhirExportReadState(generated, null);
        await Assert.ThrowsAsync<FhirExportDownloadStateConflictException>(
            () => fixture.Download().ExecuteAsync(generated.Id));

        var legacy = fixture.GeneratedExport(
            FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
            "legacy-release-neutral");
        var validation = legacy.RecordValidation(
            FhirValidationOutcome.Passed,
            FhirValidatorMetadata.Create("historical-test", "1"),
            0,
            0,
            Utc(19));
        fixture.Repository.State = new FhirExportReadState(legacy, validation);
        await Assert.ThrowsAsync<FhirExportDownloadStateConflictException>(
            () => fixture.Download().ExecuteAsync(legacy.Id));

        Assert.Equal(0, fixture.Store.ReadCount);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Authorizer = new AuthorizePatientAccess(
                new FixedClock(),
                Profiles.Resolver,
                AuthorizationRepository,
                Profiles.MyCircleAudit);
        }

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public AuthorizePatientAccess Authorizer { get; }

        public FakeReadRepository Repository { get; } = new();

        public FakeStore Store { get; } = new();

        public FakeAudit Audit { get; } = new();

        public GetFhirExport Get() => new(
            Profiles.Resolver,
            Authorizer,
            Repository);

        public DownloadFhirExport Download() => new(
            new FixedClock(),
            Profiles.Resolver,
            Authorizer,
            Repository,
            Store,
            new FhirArtifactChecksumCalculator(),
            Audit);

        public (FhirExport Export, FhirValidationResult Validation, byte[] Bytes)
            ValidatedExport(EntityId? patientId = null)
        {
            var export = GeneratedExport(
                FhirR4BaseMvp.FhirRelease,
                FhirR4BaseMvp.MappingVersion,
                patientId);
            var validation = export.RecordValidation(
                FhirValidationOutcome.Passed,
                FhirValidatorMetadata.Create("Firely test", "6.4.0"),
                0,
                1,
                Utc(19));
            return (export, validation, Store.Bytes);
        }

        public FhirExport GeneratedExport(
            string fhirVersion,
            string mappingVersion,
            EntityId? patientId = null)
        {
            var bytes = "{\"resourceType\":\"Bundle\",\"type\":\"collection\"}"u8
                .ToArray();
            Store.Bytes = bytes;
            var export = FhirExport.CreatePending(
                HistoryEvent(patientId ?? Profiles.PrimaryProfile.Id),
                FhirExportVersionMetadata.Create(fhirVersion, mappingVersion),
                EntityId.New(),
                Utc(17));
            export.MarkGenerated(
                FhirArtifactMetadata.Create(
                    FhirArtifactChecksumCalculator.Algorithm,
                    new FhirArtifactChecksumCalculator().Calculate(bytes),
                    Store.Reference.PrivateUri),
                Utc(18));
            return export;
        }
    }

    private sealed class FakeReadRepository : IFhirExportReadRepository
    {
        public FhirExportReadState? State { get; set; }

        public Task<FhirExportReadState?> FindAsync(
            EntityId fhirExportId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State?.Export.Id == fhirExportId ? State : null);
    }

    private sealed class FakeAuthorizationRepository
        : IPatientAccessAuthorizationRepository
    {
        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientAccessAuthorizationLookup(false, null));
    }

    private sealed class FakeStore : IFhirArtifactStore
    {
        public FhirArtifactStorageReference Reference { get; } =
            FhirArtifactStorageReference.CreateNew();

        public byte[] Bytes { get; set; } = [];

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public Task StoreImmutableAsync(
            FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            Bytes = artifactBytes.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(Bytes.ToArray());
        }

        public Task<bool> DeleteAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeAudit : IFhirExportAuditLogger
    {
        public List<EntityId> Downloads { get; } = [];

        public List<EntityId> IntegrityFailures { get; } = [];

        public void Created(EntityId actorAccountId, EntityId patientProfileId,
            EntityId fhirExportId, PatientAccessReason accessReason,
            DateTimeOffset occurredAt)
        {
        }

        public void ValidationCompleted(EntityId patientProfileId,
            EntityId fhirExportId, FhirExportStatus status,
            DateTimeOffset occurredAt)
        {
        }

        public void Downloaded(EntityId actorAccountId, EntityId patientProfileId,
            EntityId fhirExportId, PatientAccessReason accessReason,
            DateTimeOffset occurredAt) => Downloads.Add(fhirExportId);

        public void IntegrityRejected(EntityId actorAccountId,
            EntityId patientProfileId, EntityId fhirExportId,
            PatientAccessReason accessReason, DateTimeOffset occurredAt) =>
            IntegrityFailures.Add(fhirExportId);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Utc(20);
    }

    private static ClinicalHistoryEvent HistoryEvent(EntityId patientId)
    {
        var session = PreTriageSession.CreateForPatient(
            patientId,
            EntityId.New(),
            Utc(20),
            Utc(12));
        var episode = PreTriageEpisode.CreateFrom(session, EntityId.New(), Utc(14));
        return ClinicalHistoryEvent.CreateCompletedPreTriage(episode, Utc(15));
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);
}
