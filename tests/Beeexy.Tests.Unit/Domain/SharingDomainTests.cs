using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Sharing;

namespace Beeexy.Tests.Unit.Domain;

public sealed class SharingDomainTests
{
    [Fact]
    [Trait("Category", "Phase111")]
    public void ShareScope_RetainsExactExecutableAndReservedVocabulary()
    {
        Assert.Equal(
            [
                ShareScope.FullProfile,
                ShareScope.Case,
                ShareScope.PreTriage,
                ShareScope.Visit,
                ShareScope.SpecificRecords
            ],
            Enum.GetValues<ShareScope>());
    }

    [Theory]
    [InlineData(ShareScope.FullProfile)]
    [InlineData(ShareScope.Case)]
    [InlineData(ShareScope.PreTriage)]
    [InlineData(ShareScope.Visit)]
    [InlineData(ShareScope.SpecificRecords)]
    [Trait("Category", "Phase111")]
    public void ShareGrant_CreatesFiniteGrantForEveryStructuralScope(ShareScope scope)
    {
        var patientId = EntityId.New();
        var creatorId = EntityId.New();
        var hash = Hash('a');

        var grant = ShareGrant.Create(
            patientId,
            creatorId,
            scope,
            hash,
            Utc(10),
            Utc(11));

        Assert.NotEqual(Guid.Empty, grant.Id.Value);
        Assert.Equal(patientId, grant.PatientProfileId);
        Assert.Equal(creatorId, grant.CreatorAccountId);
        Assert.Equal(scope, grant.Scope);
        Assert.Equal(hash, grant.CapabilityHash);
        Assert.Equal(Utc(10), grant.CreatedAt);
        Assert.Equal(Utc(11), grant.ExpiresAt);
        Assert.Equal(ShareGrantStatus.Active, grant.GetStatus(Utc(10)));
        Assert.Equal(1, grant.Version);
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void ShareGrant_RejectsMissingIdentityWeakHashAndInvalidExpiry()
    {
        Assert.Throws<ArgumentException>(() => ShareGrant.Create(
            default,
            EntityId.New(),
            ShareScope.FullProfile,
            Hash('a'),
            Utc(10),
            Utc(11)));
        Assert.Throws<ArgumentException>(() => ShareGrant.Create(
            EntityId.New(),
            default,
            ShareScope.FullProfile,
            Hash('a'),
            Utc(10),
            Utc(11)));
        Assert.Throws<ArgumentException>(() => ShareGrant.Create(
            EntityId.New(),
            EntityId.New(),
            ShareScope.FullProfile,
            TokenHash.FromHash("too-short"),
            Utc(10),
            Utc(11)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareGrant.Create(
            EntityId.New(),
            EntityId.New(),
            ShareScope.FullProfile,
            Hash('a'),
            Utc(10),
            Utc(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareGrant.Create(
            EntityId.New(),
            EntityId.New(),
            ShareScope.FullProfile,
            Hash('a'),
            Utc(10),
            Utc(9)));
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void ShareGrant_RevocationIsIrreversibleIdempotentAndDistinctFromExpiry()
    {
        var grant = CreateGrant(expiresAt: Utc(12));
        var revokerId = EntityId.New();

        Assert.Equal(ShareGrantStatus.Active, grant.GetStatus(Utc(11)));
        Assert.True(grant.Revoke(revokerId, Utc(11)));
        Assert.Equal(ShareGrantStatus.Revoked, grant.GetStatus(Utc(11)));
        Assert.Equal(Utc(11), grant.RevokedAt);
        Assert.Equal(revokerId, grant.RevokedByAccountId);
        Assert.Equal(2, grant.Version);

        Assert.False(grant.Revoke(EntityId.New(), Utc(12)));
        Assert.Equal(Utc(11), grant.RevokedAt);
        Assert.Equal(revokerId, grant.RevokedByAccountId);
        Assert.Equal(2, grant.Version);

        var expired = CreateGrant(expiresAt: Utc(11));
        Assert.Equal(ShareGrantStatus.Expired, expired.GetStatus(Utc(11)));
        Assert.Null(expired.RevokedAt);
        Assert.False(expired.IsActiveAt(Utc(11)));
        Assert.DoesNotContain(
            typeof(ShareGrant).GetMethods(),
            method => method.Name.Contains("Reactivate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void ShareGrantItem_RetainsExactGrantResourceIdentityWithoutClinicalSemantics()
    {
        var grant = CreateGrant();
        var resourceType = ShareResourceType.Create("pre_triage_episode");
        var resourceId = EntityId.New();

        var item = ShareGrantItem.Create(
            grant,
            resourceType,
            resourceId,
            Utc(10).AddMinutes(1));

        Assert.Equal(grant.Id, item.ShareGrantId);
        Assert.Equal(resourceType, item.ResourceType);
        Assert.Equal(resourceId, item.ResourceId);
        Assert.DoesNotContain(
            typeof(ShareGrantItem).GetProperties(),
            property => property.Name is "Case" or "Visit");
    }

    [Theory]
    [InlineData("")]
    [InlineData("PreTriage")]
    [InlineData("visit-id")]
    [InlineData("visit id")]
    [Trait("Category", "Phase111")]
    public void ShareResourceType_RejectsUnstableOrEmptyRepresentations(string value)
    {
        Assert.Throws<ArgumentException>(() => ShareResourceType.Create(value));
    }

    [Theory]
    [InlineData(ShareAccessEventType.ShareCreated)]
    [InlineData(ShareAccessEventType.ShareAccessed)]
    [InlineData(ShareAccessEventType.ShareDownloaded)]
    [InlineData(ShareAccessEventType.ShareRevoked)]
    [InlineData(ShareAccessEventType.ShareExpired)]
    [Trait("Category", "Phase111")]
    public void ShareAccessEvent_RetainsTypedMinimizedActivity(ShareAccessEventType eventType)
    {
        var grant = CreateGrant();
        var resourceType = ShareResourceType.Create("export_artifact");

        var accessEvent = ShareAccessEvent.Create(
            grant,
            eventType,
            ShareAccessOutcome.Succeeded,
            Utc(10).AddMinutes(2),
            resourceType);

        Assert.Equal(grant.Id, accessEvent.ShareGrantId);
        Assert.Equal(eventType, accessEvent.EventType);
        Assert.Equal(ShareAccessOutcome.Succeeded, accessEvent.Outcome);
        Assert.Equal(resourceType, accessEvent.ResourceType);
        Assert.Equal(Utc(10).AddMinutes(2), accessEvent.OccurredAt);
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void SharingAuditTypes_AreImmutableAndContainNoSecretOrRawPayloadFields()
    {
        AssertNoPublicSetters(typeof(ShareGrantItem));
        AssertNoPublicSetters(typeof(ShareAccessEvent));
        AssertNoPublicSetters(typeof(ExportArtifact));

        var forbiddenNames = new[]
        {
            "Capability",
            "RawToken",
            "PlainToken",
            "ShareToken",
            "Jwt",
            "IpAddress",
            "UserAgent",
            "Payload",
            "ClinicalContent",
            "ProviderMetadata"
        };
        Assert.DoesNotContain(
            typeof(ShareGrant).GetProperties()
                .Concat(typeof(ShareGrantItem).GetProperties())
                .Concat(typeof(ShareAccessEvent).GetProperties())
                .Concat(typeof(ExportArtifact).GetProperties()),
            property => forbiddenNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ExportArtifactFormat.BeeexyJson, "application/json")]
    [InlineData(ExportArtifactFormat.Pdf, "application/pdf")]
    [InlineData(ExportArtifactFormat.FhirJson, "application/fhir+json")]
    [Trait("Category", "Phase111")]
    public void ExportArtifact_CreatesPendingSnapshotForEveryApprovedFormat(
        ExportArtifactFormat format,
        string mediaType)
    {
        var artifact = CreatePendingArtifact(format, mediaType);

        Assert.NotEqual(Guid.Empty, artifact.Id.Value);
        Assert.Equal(format, artifact.Format);
        Assert.Equal(mediaType, artifact.MediaType);
        Assert.Equal(ExportArtifactStatus.Pending, artifact.Status);
        Assert.Null(artifact.Content);
        Assert.Equal(Utc(10).AddDays(30), artifact.RetentionEligibleAt);
        Assert.Equal(1, artifact.Version);
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void ExportArtifact_AvailableContentIsImmutableAndRetentionAware()
    {
        var artifact = CreatePendingArtifact();
        var content = ExportArtifactContentMetadata.Create(
            "SHA-256",
            new string('b', 64),
            "private/exports/artifact.json");

        artifact.MarkAvailable(content, Utc(11));

        Assert.Equal(ExportArtifactStatus.Available, artifact.Status);
        Assert.Equal(content, artifact.Content);
        Assert.Equal(Utc(11), artifact.CompletedAt);
        Assert.Equal(2, artifact.Version);
        Assert.False(artifact.IsRetentionEligibleAt(Utc(10).AddDays(30).AddTicks(-1)));
        Assert.True(artifact.IsRetentionEligibleAt(Utc(10).AddDays(30)));
        Assert.Throws<InvalidOperationException>(() => artifact.MarkAvailable(
            ExportArtifactContentMetadata.Create(
                "SHA-256",
                new string('c', 64),
                "private/exports/replacement.json"),
            Utc(12)));
        Assert.Equal(content, artifact.Content);
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void ExportArtifact_FailureAndDeletionAreOneWayLifecycleTransitions()
    {
        var failed = CreatePendingArtifact();
        failed.MarkFailed("storage_unavailable", Utc(11));
        Assert.Equal(ExportArtifactStatus.Failed, failed.Status);
        Assert.Equal("storage_unavailable", failed.FailureCategory);
        Assert.Equal(Utc(11), failed.FailedAt);
        Assert.Null(failed.Content);
        Assert.Throws<InvalidOperationException>(() => failed.MarkAvailable(
            ExportArtifactContentMetadata.Create("SHA-256", new string('b', 64), "private/a"),
            Utc(12)));

        var available = CreatePendingArtifact();
        var content = ExportArtifactContentMetadata.Create(
            "SHA-256",
            new string('d', 64),
            "private/exports/deleted.json");
        available.MarkAvailable(content, Utc(11));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            available.MarkDeleted(Utc(10).AddDays(29)));
        available.MarkDeleted(Utc(10).AddDays(30));
        Assert.Equal(ExportArtifactStatus.Deleted, available.Status);
        Assert.Equal(content, available.Content);
        Assert.Equal(Utc(10).AddDays(30), available.DeletedAt);
        Assert.False(available.IsRetentionEligibleAt(Utc(10).AddDays(31)));
        Assert.Throws<InvalidOperationException>(() =>
            available.MarkDeleted(Utc(10).AddDays(31)));
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public void ExportArtifact_RejectsInvalidMetadataAndHasNoClinicalOwnershipCoupling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExportArtifact.CreatePending(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            ExportArtifactFormat.BeeexyJson,
            "application/json",
            EntityId.New(),
            "snapshot-v1",
            Utc(10),
            Utc(10)));
        Assert.Throws<ArgumentException>(() => CreatePendingArtifact(
            ExportArtifactFormat.BeeexyJson,
            "application json"));
        Assert.Throws<ArgumentException>(() => ExportArtifactContentMetadata.Create(
            "SHA 256",
            new string('a', 64),
            "private/artifact"));

        Assert.DoesNotContain(
            typeof(ExportArtifact).GetProperties(),
            property => property.PropertyType.Namespace?.StartsWith(
                "Beeexy.Domain.Triage",
                StringComparison.Ordinal) == true ||
                property.PropertyType.Namespace?.StartsWith(
                    "Beeexy.Domain.History",
                    StringComparison.Ordinal) == true);
    }

    private static ShareGrant CreateGrant(DateTimeOffset? expiresAt = null)
    {
        return ShareGrant.Create(
            EntityId.New(),
            EntityId.New(),
            ShareScope.FullProfile,
            Hash('a'),
            Utc(10),
            expiresAt ?? Utc(12));
    }

    private static ExportArtifact CreatePendingArtifact(
        ExportArtifactFormat format = ExportArtifactFormat.BeeexyJson,
        string mediaType = "application/json")
    {
        return ExportArtifact.CreatePending(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            format,
            mediaType,
            EntityId.New(),
            "shareable-health-profile-v1",
            Utc(10),
            Utc(10).AddDays(30));
    }

    private static TokenHash Hash(char value)
    {
        return TokenHash.FromHash(new string(value, 64));
    }

    private static void AssertNoPublicSetters(Type type)
    {
        Assert.All(
            type.GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 9, 9, hour, 0, 0, TimeSpan.Zero);
    }
}
