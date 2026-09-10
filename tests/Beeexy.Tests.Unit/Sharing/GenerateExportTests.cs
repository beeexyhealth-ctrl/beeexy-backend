using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class GenerateExportTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task BeeexyJson_CreatesAvailableImmutableArtifactFromCanonicalFullProfile()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(fixture.Command());

        Assert.True(result.NewlyCreated);
        Assert.Equal(ShareScope.FullProfile, fixture.Snapshots.Selection!.Scope);
        Assert.True(fixture.Snapshots.Selection.IncludeFullProfile);
        Assert.Empty(fixture.Snapshots.Selection.Items);
        Assert.Equal(ExportArtifactStatus.Available, result.Artifact.Status);
        Assert.Equal(BeeexyJsonExportRenderer.MediaType, result.Artifact.MediaType);
        Assert.Equal(ExportArtifactChecksumCalculator.Algorithm,
            result.Artifact.ChecksumAlgorithm);
        Assert.Equal(Now.AddDays(30), result.Artifact.RetentionEligibleAt);
        Assert.Equal(
            fixture.Checksums.Calculate(fixture.Storage.StoredBytes!),
            result.Artifact.Checksum);
        Assert.Equal(fixture.Storage.Reference.PrivateStorageIdentity,
            result.Artifact.PrivateStorageIdentity);
        Assert.True(fixture.Transaction.Committed);
        Assert.False(fixture.Transaction.RolledBack);

        using var document = JsonDocument.Parse(fixture.Storage.StoredBytes!);
        var root = document.RootElement;
        Assert.Equal("BeeexyJson", root.GetProperty("format").GetString());
        Assert.Equal("1.0", root.GetProperty("formatVersion").GetString());
        Assert.Equal("canonical-shared-health-v1",
            root.GetProperty("snapshotVersion").GetString());
        Assert.EndsWith("Z", root.GetProperty("generatedAt").GetString());
        Assert.Equal("BXY-EXPORT-UNIT",
            root.GetProperty("profile").GetProperty("demographics")
                .GetProperty("beeexyId").GetString());
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task ExactReplay_ReturnsSameArtifactWithoutSnapshotOrStorageWork()
    {
        var fixture = new Fixture();
        var first = await fixture.UseCase.ExecuteAsync(fixture.Command());
        fixture.Transaction.Existing = first.Artifact;
        fixture.Transaction.ResetOperationFlags();
        fixture.Storage.StoredBytes = null;
        fixture.Snapshots.Calls = 0;

        var replay = await fixture.UseCase.ExecuteAsync(fixture.Command());

        Assert.False(replay.NewlyCreated);
        Assert.Same(first.Artifact, replay.Artifact);
        Assert.Equal(0, fixture.Snapshots.Calls);
        Assert.Null(fixture.Storage.StoredBytes);
        Assert.True(fixture.Transaction.Committed);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task SameKeyWithDifferentFormat_IsConflictWithoutRendering()
    {
        var fixture = new Fixture();
        var first = await fixture.UseCase.ExecuteAsync(fixture.Command());
        fixture.Transaction.Existing = first.Artifact;
        fixture.Snapshots.Calls = 0;

        await Assert.ThrowsAsync<ExportIdempotencyConflictException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Command(ExportArtifactFormat.Pdf)));

        Assert.Equal(0, fixture.Snapshots.Calls);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task StorageFailure_RollsBackMetadataAndCompensatesPossibleBytes()
    {
        var fixture = new Fixture();
        fixture.Storage.Failure = new IOException("provider path must stay private");

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Command()));

        Assert.True(fixture.Transaction.RolledBack);
        Assert.False(fixture.Transaction.Committed);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Null(fixture.Storage.StoredBytes);
        Assert.Null(fixture.Transaction.DurableArtifact);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task PersistenceFailureAfterStorage_DeletesWrittenBytesAndLeavesNoArtifact()
    {
        var fixture = new Fixture();
        fixture.Transaction.CommitFailure = new IOException("database commit failed");

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Command()));

        Assert.True(fixture.Transaction.RolledBack);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Null(fixture.Storage.StoredBytes);
        Assert.Null(fixture.Transaction.DurableArtifact);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task ForeignPatient_FailsBeforeSnapshotAndStorage()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ExportPatientNotFoundException>(() =>
            fixture.UseCase.ExecuteAsync(new GenerateExportCommand(
                EntityId.New(),
                ExportArtifactFormat.BeeexyJson,
                EntityId.New())));
        Assert.Equal(0, fixture.Snapshots.Calls);
        Assert.Null(fixture.Storage.StoredBytes);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task Pdf_UsesCanonicalSnapshotAndPersistsExactRenderedBytes()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Command(ExportArtifactFormat.Pdf));

        Assert.True(result.NewlyCreated);
        Assert.Equal(1, fixture.Snapshots.Calls);
        Assert.Equal(1, fixture.Pdf.Calls);
        Assert.Equal(fixture.Pdf.Bytes, fixture.Storage.StoredBytes);
        Assert.Equal(PdfExportContract.MediaType, result.Artifact.MediaType);
        Assert.Equal(fixture.Checksums.Calculate(fixture.Pdf.Bytes), result.Artifact.Checksum);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task Fhir_UsesOnlyValidatedPhase6BytesWithoutCanonicalSnapshot()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Command(ExportArtifactFormat.FhirJson));

        Assert.True(result.NewlyCreated);
        Assert.Equal(0, fixture.Snapshots.Calls);
        Assert.Equal(1, fixture.Fhir.Calls);
        Assert.Equal(fixture.Fhir.Artifact.ArtifactBytes, fixture.Storage.StoredBytes);
        Assert.Equal("application/fhir+json", result.Artifact.MediaType);
        Assert.Equal(fixture.Fhir.Artifact.SnapshotId, result.Artifact.SnapshotId);
        Assert.Equal(fixture.Checksums.Calculate(fixture.Fhir.Artifact.ArtifactBytes),
            result.Artifact.Checksum);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task PdfRendererFailure_RollsBackWithoutCompletedArtifact()
    {
        var fixture = new Fixture();
        fixture.Pdf.Failure = new PdfExportRenderException(new InvalidOperationException());

        await Assert.ThrowsAsync<ExportGenerationUnavailableException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Command(ExportArtifactFormat.Pdf)));

        Assert.True(fixture.Transaction.RolledBack);
        Assert.Null(fixture.Transaction.DurableArtifact);
        Assert.Null(fixture.Storage.StoredBytes);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task Phase6Unavailable_ReturnsSafeGenerationFailureWithoutArtifact()
    {
        var fixture = new Fixture();
        fixture.Fhir.Failure = new Phase6ValidatedFhirExportUnavailableException();

        await Assert.ThrowsAsync<ExportGenerationUnavailableException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Command(ExportArtifactFormat.FhirJson)));

        Assert.Null(fixture.Transaction.DurableArtifact);
        Assert.Null(fixture.Storage.StoredBytes);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public void Renderer_IsByteDeterministicAndCanonicalizesNestedJsonObjects()
    {
        using var submitted = JsonDocument.Parse("{\"z\":1,\"a\":{\"y\":2,\"b\":3}}");
        var snapshot = Snapshot() with
        {
            ClinicalHistory =
            [
                new SharedClinicalHistoryEvent(
                    EntityId.New(),
                    "COMPLETED_PRE_TRIAGE",
                    Now.AddHours(-3),
                    Now.AddHours(-3),
                    new SharedClinicalProvenance(
                        "PRE_TRIAGE_EPISODE",
                        EntityId.New(),
                        EntityId.New(),
                        EntityId.New()))
            ],
            PreTriage =
            [
                new SharedPreTriageRecord(
                    EntityId.New(),
                    Now.AddHours(-3),
                    new SharedPreTriagePrimarySymptom("HEADACHE", "Headache"),
                    new SharedPreTriageDuration(2, "hours"),
                    4,
                    ["NAUSEA"],
                    EntityId.New(),
                    EntityId.New())
            ],
            SymptomDiaryEntries =
            [
                new SharedSymptomDiaryEntry(
                    EntityId.New(),
                    EntityId.New(),
                    Now.AddHours(-1),
                    "HEADACHE",
                    Package(),
                    [new SharedSymptomDiaryAnswer("details", "Details", submitted.RootElement.Clone())])
            ],
            SymptomDiaryContent =
            [
                new SharedSymptomDiaryContent(
                    Package(),
                    "HEADACHE",
                    "Information",
                    "Approved content",
                    [new SharedSymptomWarningSign("URGENT", "Seek care", 1)])
            ],
            SecondOpinions =
            [
                new SharedSecondOpinionResult(
                    EntityId.New(),
                    Now.AddHours(-2),
                    "1.0",
                    "Patient-visible summary",
                    ["Point"],
                    ["Question"],
                    ["Missing"],
                    "Informational only")
            ]
        };
        var renderer = new BeeexyJsonExportRenderer();
        var snapshotId = EntityId.New();

        var first = renderer.Render(snapshot, snapshotId, Now);
        var second = renderer.Render(snapshot, snapshotId, Now);

        Assert.Equal(first, second);
        var json = Encoding.UTF8.GetString(first);
        Assert.Contains("\"a\":{\"b\":3,\"y\":2},\"z\":1", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(first);
        var profile = document.RootElement.GetProperty("profile");
        Assert.Single(profile.GetProperty("clinicalHistory").EnumerateArray());
        Assert.Single(profile.GetProperty("preTriage").EnumerateArray());
        Assert.Single(profile.GetProperty("symptomDiaryEntries").EnumerateArray());
        Assert.Single(profile.GetProperty("symptomDiaryContent").EnumerateArray());
        Assert.Equal("Patient-visible summary",
            Assert.Single(profile.GetProperty("secondOpinions").EnumerateArray())
                .GetProperty("summary").GetString());
        Assert.DoesNotContain("accountId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capability", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conversation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public void Checksum_IsOverExactBytesAndDetectsMutation()
    {
        var calculator = new ExportArtifactChecksumCalculator();
        var bytes = Encoding.UTF8.GetBytes("{\"safe\":true}");
        var changed = Encoding.UTF8.GetBytes("{\"safe\":false}");

        Assert.Equal(64, calculator.Calculate(bytes).Length);
        Assert.NotEqual(calculator.Calculate(bytes), calculator.Calculate(changed));
    }

    private static CanonicalSharedHealthSnapshot Snapshot() => new(
        new SharedPatientDemographics(
            "BXY-EXPORT-UNIT",
            "Ada",
            "Lovelace",
            new DateOnly(1990, 1, 2),
            "Female",
            "Lima",
            3),
        [],
        [],
        [],
        [],
        []);

    private static SharedSymptomDiaryPackageVersion Package() => new(
        EntityId.New(),
        "HEADACHE",
        "1.0",
        new string('a', 64),
        "questions",
        "1.0",
        "information",
        "1.0",
        "MEDICAL_TEAM_PROVIDED",
        "REVIEWED",
        "APPROVED",
        Now.AddDays(-1));

    private sealed class Fixture
    {
        public Fixture()
        {
            Account = Account.Create(
                NormalizedEmail.Create($"export-{Guid.NewGuid():N}@example.com"),
                Now.AddDays(-2));
            Patient = PatientProfile.Create(
                BeeexyId.Create("BXY-EXPORT-UNIT"),
                Now.AddDays(-2),
                Account.Id);
            var preference = UserPreference.Create(
                Account.Id,
                UserTimeZone.Create("UTC"),
                Now.AddDays(-2));
            var resolver = new CurrentAccountProfileResolver(
                new SessionIdentity(Account.Id),
                new AccountProfileRepository(Account, Patient, preference),
                new AuditLogger());
            Snapshots = new SnapshotBuilder();
            Pdf = new PdfRenderer();
            Fhir = new FhirProvider();
            Storage = new Storage();
            Transaction = new Transaction();
            Checksums = new ExportArtifactChecksumCalculator();
            UseCase = new GenerateExport(
                new Clock(),
                resolver,
                Snapshots,
                new BeeexyJsonExportRenderer(),
                Pdf,
                Fhir,
                Checksums,
                Storage,
                Transaction,
                new ExportGenerationOptions(TimeSpan.FromDays(30)));
        }

        public Account Account { get; }
        public PatientProfile Patient { get; }
        public SnapshotBuilder Snapshots { get; }
        public PdfRenderer Pdf { get; }
        public FhirProvider Fhir { get; }
        public Storage Storage { get; }
        public Transaction Transaction { get; }
        public ExportArtifactChecksumCalculator Checksums { get; }
        public GenerateExport UseCase { get; }

        public GenerateExportCommand Command(
            ExportArtifactFormat format = ExportArtifactFormat.BeeexyJson) => new(
            Patient.Id,
            format,
            EntityId.From(Guid.Parse("a6d0d9bb-c366-41e3-91f4-13b397467f2f")));
    }

    private sealed class PdfRenderer : IPdfExportRenderer
    {
        public byte[] Bytes { get; } = Encoding.ASCII.GetBytes("%PDF-1.7 test");
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }

        public byte[] Render(
            CanonicalSharedHealthSnapshot snapshot,
            EntityId snapshotId,
            DateTimeOffset generatedAt)
        {
            Calls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Bytes;
        }
    }

    private sealed class FhirProvider : IPhase6ValidatedFhirExportProvider
    {
        public FhirProvider()
        {
            Artifact = new ValidatedFhirExportArtifact(
                EntityId.New(),
                "fhir-r4-4.0.1/beeexy-fhir-r4-base-mvp-v1",
                "application/fhir+json",
                Encoding.UTF8.GetBytes("{\"resourceType\":\"Bundle\"}"));
        }

        public ValidatedFhirExportArtifact Artifact { get; }
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }

        public Task<ValidatedFhirExportArtifact> GenerateAsync(
            EntityId patientProfileId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure is not null)
            {
                return Task.FromException<ValidatedFhirExportArtifact>(Failure);
            }

            return Task.FromResult(Artifact);
        }
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class SessionIdentity(EntityId accountId) : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class AccountProfileRepository(
        Account account,
        PatientProfile patient,
        UserPreference preference) : ICurrentAccountProfileRepository
    {
        public Task<CurrentAccountProfileState> LoadAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CurrentAccountProfileState(account, [patient], [preference]));

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class AuditLogger : IAccountProfileAuditLogger
    {
        public void InvariantViolation(EntityId accountId, string invariant)
        {
        }

        public void ProfileUpdateSucceeded(
            EntityId accountId,
            EntityId profileId,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt)
        {
        }

        public void ProfileUpdateConflict(EntityId accountId, EntityId profileId)
        {
        }
    }

    private sealed class SnapshotBuilder : ICanonicalSharedHealthSnapshotBuilder
    {
        public int Calls { get; set; }
        public SharedProfileSelection? Selection { get; private set; }

        public Task<CanonicalSharedHealthSnapshot> BuildAsync(
            EntityId patientProfileId,
            SharedProfileSelection selection,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Selection = selection;
            return Task.FromResult(Snapshot());
        }
    }

    private sealed class Storage : IPrivateArtifactStorage
    {
        public PrivateArtifactStorageReference Reference { get; } = new(
            new string('a', 64),
            "beeexy-private-export://test/opaque");
        public byte[]? StoredBytes { get; set; }
        public Exception? Failure { get; set; }
        public int DeleteCalls { get; private set; }

        public PrivateArtifactStorageReference CreateReference() => Reference;

        public Task StoreImmutableAsync(
            PrivateArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            StoredBytes = artifactBytes.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(
            string privateStorageIdentity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredBytes ?? throw new FileNotFoundException());

        public Task<bool> DeleteAsync(
            PrivateArtifactStorageReference reference,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            StoredBytes = null;
            return Task.FromResult(true);
        }
    }

    private sealed class Transaction : IExportArtifactTransaction
    {
        private ExportArtifact? added;

        public ExportArtifact? Existing { get; set; }
        public ExportArtifact? DurableArtifact { get; private set; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public Exception? CommitFailure { get; set; }

        public Task BeginAsync(
            EntityId patientProfileId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ExportArtifact?> FindExistingAsync(
            EntityId patientProfileId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default) => Task.FromResult(Existing);

        public void Add(ExportArtifact artifact) => added = artifact;

        public Task SaveAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (CommitFailure is not null)
            {
                return Task.FromException(CommitFailure);
            }

            Committed = true;
            DurableArtifact = added;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RolledBack = true;
            added = null;
            DurableArtifact = null;
            return Task.CompletedTask;
        }

        public void ResetOperationFlags()
        {
            Committed = false;
            RolledBack = false;
        }
    }
}
