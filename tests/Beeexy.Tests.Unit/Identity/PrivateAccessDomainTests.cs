using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class PrivateAccessDomainTests
{
    [Fact]
    public void Credential_DisableActivateAndPermanentRevoke_EnforceLifecycle()
    {
        var createdAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var credential = PrivateAccessCredential.Create(
            EntityId.New(),
            "external-testers-tester-001",
            "external-testers-tester-001-abcd1234",
            "password-hash",
            "keyword-hash",
            createdAt);

        credential.Disable(createdAt.AddMinutes(1));
        Assert.Equal(PrivateAccessCredentialStatus.Disabled, credential.Status);
        Assert.NotNull(credential.DisabledAt);

        credential.Activate(createdAt.AddMinutes(2));
        Assert.Equal(PrivateAccessCredentialStatus.Active, credential.Status);
        Assert.Null(credential.DisabledAt);

        credential.Revoke(createdAt.AddMinutes(3));
        Assert.Equal(PrivateAccessCredentialStatus.Revoked, credential.Status);
        Assert.Throws<InvalidOperationException>(() => credential.Activate(createdAt.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() => credential.RotateSecrets(
            "replacement-password-hash",
            "replacement-keyword-hash",
            createdAt.AddMinutes(4)));
    }

    [Fact]
    public void Session_ExpiresOnlyAtOrAfterExpiry()
    {
        var createdAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = createdAt.AddHours(1);
        var session = PrivateAccessSession.Create(
            EntityId.New(),
            EntityId.New(),
            TokenHash.FromHash(new string('A', 64)),
            expiresAt,
            createdAt);

        Assert.Throws<InvalidOperationException>(() => session.MarkExpired(createdAt.AddMinutes(59)));
        session.MarkExpired(expiresAt);
        Assert.Equal(PrivateAccessSessionStatus.Expired, session.Status);
    }
}
