using Beeexy.Domain.Common;

namespace Beeexy.Domain.Patients;

public sealed class UserPreference
{
    private UserPreference()
    {
        TimeZone = null!;
    }

    private UserPreference(
        EntityId id,
        EntityId accountId,
        UserTimeZone timeZone,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        TimeZone = timeZone;
        Version = 1;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AccountId { get; private set; }

    public UserTimeZone TimeZone { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static UserPreference Create(
        EntityId accountId,
        UserTimeZone timeZone,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));

        return new UserPreference(id ?? EntityId.New(), accountId, timeZone, createdAt);
    }

    public void ChangeTimeZone(UserTimeZone timeZone, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));

        if (TimeZone == timeZone)
        {
            return;
        }

        TimeZone = timeZone;
        Version = checked(Version + 1);
        UpdatedAt = updatedAt;
    }
}
