using System.Text;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class DownloadExportTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task BearerPatientAuthority_ReturnsExactStoredBytes()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            new DownloadExportCommand(fixture.Artifact.Id, null));

        Assert.Equal(fixture.Bytes, result.ArtifactBytes);
        Assert.Equal(BeeexyJsonExportRenderer.MediaType, result.MediaType);
        Assert.EndsWith(".json", result.FileName, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Repository.EventCalls);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task ExplicitArtifactShare_ReturnsBytesAndRecordsOneSafeEventIdentity()
    {
        var fixture = new Fixture();
        var identity = fixture.AuthorizeArtifactShare();

        var first = await fixture.UseCase.ExecuteAsync(
            new DownloadExportCommand(fixture.Artifact.Id, identity));
        var second = await fixture.UseCase.ExecuteAsync(
            new DownloadExportCommand(fixture.Artifact.Id, identity));

        Assert.Equal(fixture.Bytes, first.ArtifactBytes);
        Assert.Equal(first.ArtifactBytes, second.ArtifactBytes);
        Assert.Equal(2, fixture.Repository.EventCalls);
        Assert.Single(fixture.Repository.EventIds.Distinct());
        Assert.True(fixture.Repository.Committed);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task FullProfileAndUnlistedSamePatientArtifact_AreForbidden()
    {
        var fullProfile = new Fixture();
        var fullIdentity = fullProfile.AuthorizeShare(ShareScope.FullProfile, []);
        await Assert.ThrowsAsync<ExportArtifactAccessForbiddenException>(() =>
            fullProfile.UseCase.ExecuteAsync(
                new DownloadExportCommand(fullProfile.Artifact.Id, fullIdentity)));

        var unlisted = new Fixture();
        var grant = unlisted.Grant(ShareScope.SpecificRecords);
        var item = ShareItemReference(grant, EntityId.New());
        var identity = unlisted.AuthorizeShare(
            ShareScope.SpecificRecords,
            [item],
            grant);
        await Assert.ThrowsAsync<ExportArtifactAccessForbiddenException>(() =>
            unlisted.UseCase.ExecuteAsync(
                new DownloadExportCommand(unlisted.Artifact.Id, identity)));

        Assert.Equal(0, fullProfile.Repository.EventCalls);
        Assert.Equal(0, unlisted.Repository.EventCalls);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task RevokedGrantAndIntegrityFailure_FailWithoutSuccessEvent()
    {
        var revoked = new Fixture();
        var grant = revoked.Grant(ShareScope.SpecificRecords);
        grant.Revoke(revoked.Account.Id, Now.AddMinutes(-1));
        revoked.Repository.GrantState = new SharedProfileGrantState(
            grant,
            [ShareItemReference(grant, revoked.Artifact.Id)]);
        var identity = new ShareAccessIdentity(grant.Id, grant.Scope, EntityId.New());
        await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            revoked.UseCase.ExecuteAsync(
                new DownloadExportCommand(revoked.Artifact.Id, identity)));

        var corrupt = new Fixture();
        corrupt.Storage.Bytes = Encoding.UTF8.GetBytes("changed bytes");
        await Assert.ThrowsAsync<ExportArtifactIntegrityException>(() =>
            corrupt.UseCase.ExecuteAsync(
                new DownloadExportCommand(corrupt.Artifact.Id, null)));

        Assert.Equal(0, revoked.Repository.EventCalls);
        Assert.Equal(0, corrupt.Repository.EventCalls);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task ExpiredOrTokenScopeMismatchedGrant_IsDeniedBeforeStorage()
    {
        var expired = new Fixture();
        var expiredGrant = ShareGrant.Create(
            expired.Patient.Id,
            expired.Account.Id,
            EntityId.New(),
            ShareRequestFingerprint.Create(new string('c', 64)),
            ShareScope.SpecificRecords,
            TokenHash.FromHash("sha256:" + new string('d', 64)),
            Now.AddHours(-2),
            Now.AddMinutes(-1));
        expired.Repository.GrantState = new SharedProfileGrantState(
            expiredGrant,
            [ShareItemReference(expiredGrant, expired.Artifact.Id)]);
        await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            expired.UseCase.ExecuteAsync(new DownloadExportCommand(
                expired.Artifact.Id,
                new ShareAccessIdentity(
                    expiredGrant.Id,
                    ShareScope.SpecificRecords,
                    EntityId.New()))));

        var mismatch = new Fixture();
        var grant = mismatch.Grant(ShareScope.SpecificRecords);
        mismatch.Repository.GrantState = new SharedProfileGrantState(
            grant,
            [ShareItemReference(grant, mismatch.Artifact.Id)]);
        await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            mismatch.UseCase.ExecuteAsync(new DownloadExportCommand(
                mismatch.Artifact.Id,
                new ShareAccessIdentity(grant.Id, ShareScope.FullProfile, EntityId.New()))));

        Assert.Equal(0, expired.Repository.EventCalls);
        Assert.Equal(0, mismatch.Repository.EventCalls);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task CrossPatientArtifactItem_FailsClosed()
    {
        var fixture = new Fixture();
        var foreignArtifact = ExportArtifact.CreatePending(
            EntityId.New(),
            fixture.Account.Id,
            EntityId.New(),
            ExportArtifactFormat.BeeexyJson,
            BeeexyJsonExportRenderer.MediaType,
            EntityId.New(),
            BeeexyJsonExportRenderer.SnapshotVersion,
            Now.AddMinutes(-10),
            Now.AddDays(30));
        foreignArtifact.MarkAvailable(
            ExportArtifactContentMetadata.Create(
                ExportArtifactChecksumCalculator.Algorithm,
                new ExportArtifactChecksumCalculator().Calculate(fixture.Bytes),
                "beeexy-private-export://test/foreign"),
            Now.AddMinutes(-10));
        fixture.Repository.Artifact = foreignArtifact;
        var grant = fixture.Grant(ShareScope.SpecificRecords);
        fixture.Repository.GrantState = new SharedProfileGrantState(
            grant,
            [ShareItemReference(grant, foreignArtifact.Id)]);

        await Assert.ThrowsAsync<ExportArtifactAccessForbiddenException>(() =>
            fixture.UseCase.ExecuteAsync(new DownloadExportCommand(
                foreignArtifact.Id,
                new ShareAccessIdentity(grant.Id, grant.Scope, EntityId.New()))));

        Assert.Equal(0, fixture.Repository.EventCalls);
    }

    private static ShareGrantItem ShareItemReference(ShareGrant grant, EntityId artifactId) =>
        ShareGrantItem.Create(
            grant,
            ShareResourceType.Create(SupportedShareResourceTypes.ExportArtifact),
            artifactId,
            Now.AddMinutes(-5));

    private sealed class Fixture
    {
        public Fixture()
        {
            Account = Account.Create(
                NormalizedEmail.Create($"download-{Guid.NewGuid():N}@example.com"),
                Now.AddDays(-2));
            Patient = PatientProfile.Create(
                BeeexyId.Create("BXY-DOWNLOAD-UNIT"),
                Now.AddDays(-2),
                Account.Id);
            var preference = UserPreference.Create(
                Account.Id,
                UserTimeZone.Create("UTC"),
                Now.AddDays(-2));
            var resolver = new CurrentAccountProfileResolver(
                new SessionIdentity(Account.Id),
                new AccountProfileRepository(Account, Patient, preference),
                new AccountAuditLogger());
            var patientAuthorization = new AuthorizePatientAccess(
                new Clock(),
                resolver,
                new PatientAuthorizationRepository(),
                new CircleAuditLogger());
            Bytes = Encoding.UTF8.GetBytes("{\"format\":\"BeeexyJson\"}");
            Artifact = ExportArtifact.CreatePending(
                Patient.Id,
                Account.Id,
                EntityId.New(),
                ExportArtifactFormat.BeeexyJson,
                BeeexyJsonExportRenderer.MediaType,
                EntityId.New(),
                BeeexyJsonExportRenderer.SnapshotVersion,
                Now.AddMinutes(-10),
                Now.AddDays(30));
            var checksum = new ExportArtifactChecksumCalculator();
            Artifact.MarkAvailable(
                ExportArtifactContentMetadata.Create(
                    ExportArtifactChecksumCalculator.Algorithm,
                    checksum.Calculate(Bytes),
                    "beeexy-private-export://test/unit"),
                Now.AddMinutes(-10));
            Repository = new Repository { Artifact = Artifact };
            Storage = new Storage { Bytes = Bytes };
            UseCase = new DownloadExport(
                new Clock(),
                resolver,
                patientAuthorization,
                Repository,
                Storage,
                checksum,
                new ExportGenerationOptions(TimeSpan.FromDays(30)));
        }

        public Account Account { get; }
        public PatientProfile Patient { get; }
        public byte[] Bytes { get; }
        public ExportArtifact Artifact { get; }
        public Repository Repository { get; }
        public Storage Storage { get; }
        public DownloadExport UseCase { get; }

        public ShareAccessIdentity AuthorizeArtifactShare()
        {
            var grant = Grant(ShareScope.SpecificRecords);
            return AuthorizeShare(
                ShareScope.SpecificRecords,
                [ShareItemReference(grant, Artifact.Id)],
                grant);
        }

        public ShareAccessIdentity AuthorizeShare(
            ShareScope scope,
            IReadOnlyList<ShareGrantItem> items,
            ShareGrant? grant = null)
        {
            grant ??= Grant(scope);
            Repository.GrantState = new SharedProfileGrantState(grant, items);
            return new ShareAccessIdentity(grant.Id, scope, EntityId.New());
        }

        public ShareGrant Grant(ShareScope scope) => ShareGrant.Create(
            Patient.Id,
            Account.Id,
            EntityId.New(),
            ShareRequestFingerprint.Create(new string('a', 64)),
            scope,
            TokenHash.FromHash("sha256:" + new string('b', 64)),
            Now.AddMinutes(-5),
            Now.AddHours(1));
    }

    private sealed class Repository : IExportDownloadRepository
    {
        public ExportArtifact? Artifact { get; set; }
        public SharedProfileGrantState? GrantState { get; set; }
        public int EventCalls { get; private set; }
        public List<EntityId> EventIds { get; } = [];
        public bool Committed { get; private set; }

        public Task<ExportArtifact?> FindArtifactAsync(
            EntityId exportArtifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifact?.Id == exportArtifactId ? Artifact : null);

        public Task<SharedProfileGrantState?> FindGrantForDownloadAsync(
            EntityId shareGrantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GrantState?.Grant.Id == shareGrantId ? GrantState : null);

        public Task RecordSuccessfulDownloadAsync(
            ShareGrant grant,
            EntityId eventId,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default)
        {
            EventCalls++;
            EventIds.Add(eventId);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Storage : IPrivateArtifactStorage
    {
        public byte[] Bytes { get; set; } = [];
        public PrivateArtifactStorageReference CreateReference() => throw new NotSupportedException();
        public Task StoreImmutableAsync(
            PrivateArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(
            string privateStorageIdentity,
            CancellationToken cancellationToken = default) => Task.FromResult(Bytes);
        public Task<bool> DeleteAsync(
            PrivateArtifactStorageReference reference,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class PatientAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientAccessAuthorizationLookup(false, null));
    }

    private sealed class AccountAuditLogger : IAccountProfileAuditLogger
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

        public void ProfileUpdateConflict(
            EntityId accountId,
            EntityId profileId)
        {
        }
    }

    private sealed class CircleAuditLogger : IMyCircleAuditLogger
    {
        public void DuplicateAccessiblePatientDetected(
            EntityId accountId,
            EntityId managerProfileId,
            EntityId subjectProfileId)
        {
        }

        public void PatientAccessDenied(
            EntityId accountId,
            EntityId managerProfileId,
            EntityId targetProfileId,
            PatientAccessDenialCategory category,
            DateTimeOffset occurredAt)
        {
        }
    }
}
