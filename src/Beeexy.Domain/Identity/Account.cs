using Beeexy.Domain.Common;

namespace Beeexy.Domain.Identity;

public sealed class Account
{
    private Account()
    {
        Email = null!;
    }

    private Account(
        EntityId id,
        NormalizedEmail email,
        DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public NormalizedEmail Email { get; private set; }

    public AccountStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Account Create(
        NormalizedEmail email,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(email);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));

        return new Account(id ?? EntityId.New(), email, createdAt);
    }

    public void Disable(DateTimeOffset updatedAt)
    {
        ChangeStatus(AccountStatus.Disabled, updatedAt);
    }

    public void Activate(DateTimeOffset updatedAt)
    {
        ChangeStatus(AccountStatus.Active, updatedAt);
    }

    private void ChangeStatus(AccountStatus status, DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));

        if (Status == status)
        {
            return;
        }

        Status = status;
        UpdatedAt = updatedAt;
    }
}
