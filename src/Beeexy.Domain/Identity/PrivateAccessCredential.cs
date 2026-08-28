using Beeexy.Domain.Common;

namespace Beeexy.Domain.Identity;

public sealed class PrivateAccessCredential
{
    private PrivateAccessCredential()
    {
        TesterKey = null!;
        Username = null!;
        PasswordHash = null!;
        KeywordHash = null!;
    }

    private PrivateAccessCredential(
        EntityId id,
        EntityId accountId,
        string testerKey,
        string username,
        string passwordHash,
        string keywordHash,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        TesterKey = testerKey;
        Username = username;
        PasswordHash = passwordHash;
        KeywordHash = keywordHash;
        Status = PrivateAccessCredentialStatus.Active;
        CreatedAt = createdAt;
    }

    public const int TesterKeyMaximumLength = 100;
    public const int UsernameMaximumLength = 128;
    public const int SecretHashMaximumLength = 512;

    public EntityId Id { get; private set; }
    public EntityId AccountId { get; private set; }
    public string TesterKey { get; private set; }
    public string Username { get; private set; }
    public string PasswordHash { get; private set; }
    public string KeywordHash { get; private set; }
    public PrivateAccessCredentialStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static PrivateAccessCredential Create(
        EntityId accountId,
        string testerKey,
        string username,
        string passwordHash,
        string keywordHash,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        EnsureText(testerKey, TesterKeyMaximumLength, nameof(testerKey));
        EnsureText(username, UsernameMaximumLength, nameof(username));
        EnsureText(passwordHash, SecretHashMaximumLength, nameof(passwordHash));
        EnsureText(keywordHash, SecretHashMaximumLength, nameof(keywordHash));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new PrivateAccessCredential(
            id ?? EntityId.New(),
            accountId,
            testerKey,
            username,
            passwordHash,
            keywordHash,
            createdAt);
    }

    public void Disable(DateTimeOffset updatedAt)
    {
        EnsureMutable(updatedAt);
        if (Status == PrivateAccessCredentialStatus.Disabled)
        {
            return;
        }

        Status = PrivateAccessCredentialStatus.Disabled;
        DisabledAt = updatedAt;
        UpdatedAt = updatedAt;
    }

    public void Activate(DateTimeOffset updatedAt)
    {
        EnsureMutable(updatedAt);
        if (Status == PrivateAccessCredentialStatus.Active)
        {
            return;
        }

        Status = PrivateAccessCredentialStatus.Active;
        DisabledAt = null;
        UpdatedAt = updatedAt;
    }

    public void Revoke(DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        if (Status == PrivateAccessCredentialStatus.Revoked)
        {
            return;
        }

        Status = PrivateAccessCredentialStatus.Revoked;
        DisabledAt = null;
        RevokedAt = updatedAt;
        UpdatedAt = updatedAt;
    }

    public void RotateSecrets(
        string passwordHash,
        string keywordHash,
        DateTimeOffset updatedAt)
    {
        EnsureMutable(updatedAt);
        EnsureText(passwordHash, SecretHashMaximumLength, nameof(passwordHash));
        EnsureText(keywordHash, SecretHashMaximumLength, nameof(keywordHash));
        PasswordHash = passwordHash;
        KeywordHash = keywordHash;
        UpdatedAt = updatedAt;
    }

    private void EnsureMutable(DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        if (Status == PrivateAccessCredentialStatus.Revoked)
        {
            throw new InvalidOperationException("A revoked private-access credential is immutable.");
        }
    }

    private static void EnsureText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
