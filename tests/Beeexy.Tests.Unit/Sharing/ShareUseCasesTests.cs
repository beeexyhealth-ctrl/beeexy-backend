using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Sharing;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class ShareUseCasesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_DefaultsToTwentyFourHoursAndDisclosesOneFragmentCapability()
    {
        var fixture = new Fixture();

        var result = await fixture.Create.ExecuteAsync(Command(ShareScope.FullProfile));

        Assert.True(result.NewlyCreated);
        Assert.Equal(Now, result.Share.CreatedAt);
        Assert.Equal(Now.AddHours(24), result.Share.ExpiresAt);
        Assert.Equal("phase112-capability", result.Capability);
        Assert.Equal("https://share.test/open#phase112-capability", result.ShareUrl);
        var capability = Assert.IsType<string>(result.Capability);
        var shareUrl = Assert.IsType<string>(result.ShareUrl);
        Assert.DoesNotContain('?', shareUrl);
        Assert.Equal(fixture.Capabilities.GeneratedHash, fixture.Transaction.AddedGrant!.CapabilityHash);
        Assert.DoesNotContain(
            capability,
            fixture.Transaction.AddedGrant.CapabilityHash.Value,
            StringComparison.Ordinal);
        var createdEvent = Assert.IsType<ShareAccessEvent>(fixture.Transaction.AddedEvent);
        Assert.Equal(ShareAccessEventType.ShareCreated, createdEvent.EventType);
        Assert.Equal(ShareAccessOutcome.Succeeded, createdEvent.Outcome);
        Assert.True(fixture.Transaction.Saved);
        Assert.True(fixture.Transaction.Committed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(10080)]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_AcceptsPositiveLifetimeThroughExactSevenDayMaximum(int minutes)
    {
        var fixture = new Fixture();

        var result = await fixture.Create.ExecuteAsync(
            Command(ShareScope.PreTriage, lifetimeMinutes: minutes));

        Assert.Equal(Now.AddMinutes(minutes), result.Share.ExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10081)]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_RejectsInvalidOrOverMaximumLifetime(int minutes)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.Create.ExecuteAsync(
                Command(ShareScope.FullProfile, lifetimeMinutes: minutes)));

        Assert.Equal("sharing.lifetime_invalid", exception.Code);
        Assert.Null(fixture.Transaction.AddedGrant);
    }

    [Theory]
    [InlineData(ShareScope.Case)]
    [InlineData(ShareScope.Visit)]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_ReservedScopesFailClosed(ShareScope scope)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.Create.ExecuteAsync(Command(scope)));

        Assert.Equal("sharing.scope_unavailable", exception.Code);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_UnknownScopeFailsClosed()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.Create.ExecuteAsync(Command((ShareScope)999)));

        Assert.Equal("sharing.scope_invalid", exception.Code);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_SpecificRecordsRequiresSupportedUniqueOwnedItems()
    {
        var fixture = new Fixture();
        var item = new ShareItemReference(
            ShareResourceType.Create(SupportedShareResourceTypes.PreTriageEpisode),
            EntityId.New());

        var success = await fixture.Create.ExecuteAsync(
            Command(ShareScope.SpecificRecords, items: [item]));

        Assert.Equal(1, success.Share.ItemCount);
        Assert.Equal(item.ResourceId, Assert.Single(fixture.Transaction.AddedItems).ResourceId);

        var missingFixture = new Fixture { AllItemsOwned = false };
        await Assert.ThrowsAsync<ShareItemNotFoundException>(() =>
            missingFixture.Create.ExecuteAsync(
                Command(ShareScope.SpecificRecords, items: [item])));
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_RejectsMissingDuplicateUnsupportedOrMisplacedItems()
    {
        var item = new ShareItemReference(
            ShareResourceType.Create(SupportedShareResourceTypes.ClinicalHistoryEvent),
            EntityId.New());
        var unsupported = item with { ResourceType = ShareResourceType.Create("unknown_record") };

        await AssertValidationAsync(Command(ShareScope.SpecificRecords), "sharing.items_required");
        await AssertValidationAsync(
            Command(ShareScope.SpecificRecords, items: [item, item]),
            "sharing.items_duplicate");
        await AssertValidationAsync(
            Command(ShareScope.SpecificRecords, items: [unsupported]),
            "sharing.item_type_invalid");
        await AssertValidationAsync(
            Command(ShareScope.FullProfile, items: [item]),
            "sharing.items_not_allowed");
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_ExactReplayReturnsMetadataWithoutSecondDisclosureOrEvent()
    {
        var fixture = new Fixture();
        var command = Command(ShareScope.FullProfile);
        fixture.Transaction.Existing = CreateExisting(fixture, command);

        var result = await fixture.Create.ExecuteAsync(command);

        Assert.False(result.NewlyCreated);
        Assert.Null(result.Capability);
        Assert.Null(result.ShareUrl);
        Assert.Equal(fixture.Transaction.Existing.Grant.Id, result.Share.ShareGrantId);
        Assert.Equal(0, fixture.Capabilities.GenerateCalls);
        Assert.Null(fixture.Transaction.AddedEvent);
        Assert.False(fixture.Transaction.Saved);
        Assert.True(fixture.Transaction.Committed);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task CreateShare_IncompatibleIdempotencyReuseReturnsConflict()
    {
        var fixture = new Fixture();
        var original = Command(ShareScope.FullProfile);
        fixture.Transaction.Existing = CreateExisting(fixture, original);

        var preTriageItem = new ShareItemReference(
            ShareResourceType.Create(SupportedShareResourceTypes.PreTriageEpisode),
            EntityId.New());

        await Assert.ThrowsAsync<ShareIdempotencyConflictException>(() =>
            fixture.Create.ExecuteAsync(original with
            {
                Scope = ShareScope.PreTriage,
                Items = [preTriageItem]
            }));

        Assert.Equal(0, fixture.Capabilities.GenerateCalls);
        Assert.Null(fixture.Transaction.AddedGrant);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task ListShares_UsesOnlyResolvedPrimaryAndMapsActiveRevokedExpired()
    {
        var fixture = new Fixture();
        var active = CreateGrant(fixture, Now.AddHours(1));
        var revoked = CreateGrant(fixture, Now.AddHours(2));
        revoked.Revoke(fixture.Account.Id, Now.AddMinutes(-1));
        var expired = CreateGrant(fixture, Now);
        fixture.Read.States =
        [
            new ShareCreationState(active, 0),
            new ShareCreationState(revoked, 1),
            new ShareCreationState(expired, 2)
        ];

        var result = await fixture.List.ExecuteAsync();

        Assert.Equal(fixture.Patient.Id, fixture.Read.RequestedPatientId);
        Assert.Equal(
            [ShareGrantStatus.Active, ShareGrantStatus.Revoked, ShareGrantStatus.Expired],
            result.Select(value => value.Status));
        Assert.Equal([0, 1, 2], result.Select(value => value.ItemCount));
        Assert.DoesNotContain(
            typeof(ShareSummary).GetProperties(),
            property => property.Name.Contains("Capability", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public void CapabilityService_UsesTwoHundredFiftySixRandomBitsAndUrlSafeEncoding()
    {
        var service = new CryptographicShareCapabilityService();

        var generated = Enumerable.Range(0, 64).Select(_ => service.Generate()).ToArray();

        Assert.Equal(64, generated.Select(value => value.Value).Distinct().Count());
        Assert.All(generated, value =>
        {
            Assert.StartsWith(CryptographicShareCapabilityService.CapabilityPrefix, value.Value);
            Assert.Equal(
                CryptographicShareCapabilityService.CapabilityPrefix.Length +
                    CryptographicShareCapabilityService.EncodedRandomLength,
                value.Value.Length);
            Assert.DoesNotContain(value.Value, character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'));
            Assert.Equal(value.Hash, service.Hash(value.Value));
            Assert.Equal("[REDACTED]", value.ToString());
        });
    }

    [Theory]
    [InlineData("https://beeexy.ai/share")]
    [InlineData("http://localhost:3000/share")]
    [Trait("Category", "Phase112")]
    public void ShareUrlOptions_AcceptsConfiguredHttpOriginsWithPath(string value)
    {
        var options = ShareUrlOptions.Create(value);
        Assert.Equal($"{value}#secret", options.Build("secret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/share")]
    [InlineData("https://user@example.com/share")]
    [InlineData("https://example.com/share?token=x")]
    [InlineData("https://example.com/share#old")]
    [InlineData("https://example.com/share/")]
    [Trait("Category", "Phase112")]
    public void ShareUrlOptions_RejectsUnsafeOrAmbiguousBases(string value)
    {
        Assert.Throws<ArgumentException>(() => ShareUrlOptions.Create(value));
    }

    private static async Task AssertValidationAsync(
        CreateShareCommand command,
        string expectedCode)
    {
        var fixture = new Fixture();
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.Create.ExecuteAsync(command));
        Assert.Equal(expectedCode, exception.Code);
    }

    private static CreateShareCommand Command(
        ShareScope scope,
        int? lifetimeMinutes = null,
        IReadOnlyList<ShareItemReference>? items = null)
    {
        var resolvedItems = items ?? (scope == ShareScope.PreTriage
            ? [new ShareItemReference(
                ShareResourceType.Create(SupportedShareResourceTypes.PreTriageEpisode),
                EntityId.New())]
            : []);
        return new CreateShareCommand(
            scope,
            lifetimeMinutes,
            EntityId.New(),
            resolvedItems);
    }

    private static ShareCreationState CreateExisting(
        Fixture fixture,
        CreateShareCommand command)
    {
        var lifetime = fixture.Lifetime.Resolve(command.LifetimeMinutes);
        var grant = ShareGrant.Create(
            fixture.Patient.Id,
            fixture.Account.Id,
            command.IdempotencyKey,
            ShareRequestFingerprintCalculator.Calculate(
                fixture.Patient.Id,
                command.Scope,
                lifetime,
                command.Items),
            command.Scope,
            fixture.Capabilities.GeneratedHash,
            Now,
            Now.Add(lifetime));
        return new ShareCreationState(grant, command.Items.Count);
    }

    private static ShareGrant CreateGrant(Fixture fixture, DateTimeOffset expiresAt) =>
        ShareGrant.Create(
            fixture.Patient.Id,
            fixture.Account.Id,
            EntityId.New(),
            ShareRequestFingerprint.Create(new string('0', 64)),
            ShareScope.FullProfile,
            fixture.Capabilities.GeneratedHash,
            Now.AddHours(-1),
            expiresAt);

    private sealed class Fixture
    {
        public Fixture()
        {
            Account = Account.Create(
                NormalizedEmail.Create($"share-unit-{Guid.NewGuid():N}@example.com"),
                Now.AddDays(-1));
            Patient = PatientProfile.Create(
                BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
                Now.AddDays(-1),
                Account.Id);
            var preference = UserPreference.Create(
                Account.Id,
                UserTimeZone.Create("UTC"),
                Now.AddDays(-1));
            var resolver = new CurrentAccountProfileResolver(
                new SessionIdentity(Account.Id),
                new AccountProfileRepository(Account, Patient, preference),
                new NullAuditLogger());
            Clock = new FixedClock(Now);
            Lifetime = new ShareLifetimePolicy();
            Capabilities = new CapabilityService();
            Transaction = new ShareTransaction();
            Read = new ShareReadRepository();
            Create = new CreateShare(
                Clock,
                resolver,
                Lifetime,
                ShareUrlOptions.Create("https://share.test/open"),
                Capabilities,
                Transaction);
            List = new ListShares(Clock, resolver, Read);
        }

        public Account Account { get; }
        public PatientProfile Patient { get; }
        public FixedClock Clock { get; }
        public ShareLifetimePolicy Lifetime { get; }
        public CapabilityService Capabilities { get; }
        public ShareTransaction Transaction { get; }
        public ShareReadRepository Read { get; }
        public CreateShare Create { get; }
        public ListShares List { get; }

        public bool AllItemsOwned
        {
            set => Transaction.AllItemsOwned = value;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
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

    private sealed class NullAuditLogger : IAccountProfileAuditLogger
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

    private sealed class CapabilityService : IShareCapabilityService
    {
        public TokenHash GeneratedHash { get; } =
            TokenHash.FromHash("sha256:" + new string('a', 64));

        public int GenerateCalls { get; private set; }

        public GeneratedShareCapability Generate()
        {
            GenerateCalls++;
            return new GeneratedShareCapability("phase112-capability", GeneratedHash);
        }

        public TokenHash Hash(string capability) => GeneratedHash;
    }

    private sealed class ShareTransaction : IShareCreationTransaction
    {
        public ShareCreationState? Existing { get; set; }
        public bool AllItemsOwned { get; set; } = true;
        public ShareGrant? AddedGrant { get; private set; }
        public IReadOnlyList<ShareGrantItem> AddedItems { get; private set; } = [];
        public ShareAccessEvent? AddedEvent { get; private set; }
        public bool Saved { get; private set; }
        public bool Committed { get; private set; }

        public Task BeginAsync(
            EntityId patientProfileId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ShareCreationState?> FindExistingAsync(
            EntityId patientProfileId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default) => Task.FromResult(Existing);

        public Task<bool> AllItemsBelongToPatientAsync(
            EntityId patientProfileId,
            IReadOnlyList<ShareItemReference> items,
            CancellationToken cancellationToken = default) => Task.FromResult(AllItemsOwned);

        public void Add(
            ShareGrant grant,
            IReadOnlyList<ShareGrantItem> items,
            ShareAccessEvent createdEvent)
        {
            AddedGrant = grant;
            AddedItems = items;
            AddedEvent = createdEvent;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Saved = true;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ShareReadRepository : IShareReadRepository
    {
        public IReadOnlyList<ShareCreationState> States { get; set; } = [];
        public EntityId RequestedPatientId { get; private set; }

        public Task<IReadOnlyList<ShareCreationState>> ListAsync(
            EntityId patientProfileId,
            CancellationToken cancellationToken = default)
        {
            RequestedPatientId = patientProfileId;
            return Task.FromResult(States);
        }
    }
}
