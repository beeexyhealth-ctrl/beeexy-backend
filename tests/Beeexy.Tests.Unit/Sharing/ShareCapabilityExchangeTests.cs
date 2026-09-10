using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Sharing;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class ShareCapabilityExchangeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task ActiveCapability_IsReusableAndIssuesDistinctFifteenMinuteTokens()
    {
        var fixture = CreateFixture(CreateGrant(Now.AddHours(-1), Now.AddHours(2)));

        var first = await fixture.UseCase.ExecuteAsync(fixture.Capability);
        var second = await fixture.UseCase.ExecuteAsync(fixture.Capability);

        Assert.Equal(Now.AddMinutes(15), first.ExpiresAt);
        Assert.Equal(Now.AddMinutes(15), second.ExpiresAt);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(2, fixture.Issuer.Calls.Count);
        Assert.All(fixture.Issuer.Calls, call =>
        {
            Assert.Equal(fixture.Grant.Id, call.GrantId);
            Assert.Equal(ShareScope.FullProfile, call.Scope);
            Assert.Equal(Now, call.IssuedAt);
            Assert.Equal(Now.AddMinutes(15), call.ExpiresAt);
        });
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task NearExpiryGrant_TruncatesTokenToAuthoritativeGrantExpiry()
    {
        var grant = CreateGrant(Now.AddMinutes(-5), Now.AddMinutes(2));
        var fixture = CreateFixture(grant);

        var result = await fixture.UseCase.ExecuteAsync(fixture.Capability);

        Assert.Equal(grant.ExpiresAt, result.ExpiresAt);
        Assert.Equal(grant.ExpiresAt, Assert.Single(fixture.Issuer.Calls).ExpiresAt);
    }

    [Theory]
    [Trait("Category", "Phase113")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-capability")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("BEE-0000-0000")]
    public async Task MissingMalformedAndIdentifierOnlyCredentials_AreIndistinguishablyDenied(
        string? presented)
    {
        var fixture = CreateFixture(CreateGrant(Now.AddHours(-1), Now.AddHours(2)));

        var exception = await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            fixture.UseCase.ExecuteAsync(presented));

        Assert.Equal("The share capability is invalid or unavailable.", exception.Message);
        Assert.Empty(fixture.Issuer.Calls);
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task WellFormedUnknownCapability_IsDeniedAfterHashLookup()
    {
        var fixture = CreateFixture(CreateGrant(Now.AddHours(-1), Now.AddHours(2)));
        fixture.Repository.State = null;

        await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Capability));

        Assert.NotNull(fixture.Repository.LastHash);
        Assert.StartsWith("sha256:", fixture.Repository.LastHash!.Value, StringComparison.Ordinal);
        Assert.Empty(fixture.Issuer.Calls);
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task RevokedExpiredFutureReservedAndInconsistentGrants_AreDenied()
    {
        var accountId = EntityId.New();
        var revoked = CreateGrant(Now.AddHours(-1), Now.AddHours(1), accountId: accountId);
        revoked.Revoke(accountId, Now.AddMinutes(-1));
        var states = new[]
        {
            new ShareExchangeState(revoked, 0),
            new ShareExchangeState(CreateGrant(Now.AddHours(-2), Now.AddMinutes(-1)), 0),
            new ShareExchangeState(CreateGrant(Now.AddMinutes(1), Now.AddHours(1)), 0),
            new ShareExchangeState(
                CreateGrant(Now.AddHours(-1), Now.AddHours(1), ShareScope.Case),
                0),
            new ShareExchangeState(
                CreateGrant(Now.AddHours(-1), Now.AddHours(1), ShareScope.Visit),
                0),
            new ShareExchangeState(
                CreateGrant(Now.AddHours(-1), Now.AddHours(1), ShareScope.FullProfile),
                1),
            new ShareExchangeState(
                CreateGrant(Now.AddHours(-1), Now.AddHours(1), ShareScope.SpecificRecords),
                0)
        };

        foreach (var state in states)
        {
            var fixture = CreateFixture(state.Grant, state.ItemCount);
            await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Capability));
            Assert.Empty(fixture.Issuer.Calls);
        }
    }

    private static Fixture CreateFixture(ShareGrant grant, int itemCount = 0)
    {
        var capabilityService = new CryptographicShareCapabilityService();
        var generated = capabilityService.Generate();
        var persistedGrant = ShareGrant.Create(
            grant.PatientProfileId,
            grant.CreatorAccountId,
            grant.IdempotencyKey,
            grant.RequestFingerprint,
            grant.Scope,
            generated.Hash,
            grant.CreatedAt,
            grant.ExpiresAt,
            grant.Id);
        if (grant.RevokedAt.HasValue)
        {
            persistedGrant.Revoke(
                grant.RevokedByAccountId!.Value,
                grant.RevokedAt.Value);
        }

        var repository = new StubRepository
        {
            State = new ShareExchangeState(persistedGrant, itemCount)
        };
        var issuer = new RecordingIssuer();
        var policy = new ShareAccessTokenPolicy(
            "unit-test-issuer",
            "unit-test-share-audience",
            "unit-test-share-signing-key-with-at-least-32-bytes",
            TimeSpan.FromMinutes(15));
        var useCase = new ExchangeShareCapability(
            capabilityService,
            repository,
            issuer,
            policy,
            new FixedClock(Now));
        return new Fixture(useCase, repository, issuer, persistedGrant, generated.Value);
    }

    private static ShareGrant CreateGrant(
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        ShareScope scope = ShareScope.FullProfile,
        EntityId? accountId = null) => ShareGrant.Create(
        EntityId.New(),
        accountId ?? EntityId.New(),
        EntityId.New(),
        ShareRequestFingerprint.Create(new string('a', 64)),
        scope,
        TokenHash.FromHash("sha256:" + new string('b', 64)),
        createdAt,
        expiresAt);

    private sealed class StubRepository : IShareExchangeRepository
    {
        public ShareExchangeState? State { get; set; }

        public TokenHash? LastHash { get; private set; }

        public Task<ShareExchangeState?> FindByCapabilityHashAsync(
            TokenHash capabilityHash,
            CancellationToken cancellationToken = default)
        {
            LastHash = capabilityHash;
            return Task.FromResult(State);
        }
    }

    private sealed class RecordingIssuer : IShareAccessTokenIssuer
    {
        public List<IssueCall> Calls { get; } = [];

        public IssuedShareAccessToken Issue(
            EntityId shareGrantId,
            ShareScope scope,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt)
        {
            Calls.Add(new IssueCall(shareGrantId, scope, issuedAt, expiresAt));
            return new IssuedShareAccessToken(Guid.NewGuid().ToString("D"), expiresAt);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed record Fixture(
        ExchangeShareCapability UseCase,
        StubRepository Repository,
        RecordingIssuer Issuer,
        ShareGrant Grant,
        string Capability);

    private sealed record IssueCall(
        EntityId GrantId,
        ShareScope Scope,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt);
}
