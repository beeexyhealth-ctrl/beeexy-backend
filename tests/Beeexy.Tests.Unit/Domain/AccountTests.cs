using Beeexy.Domain.Identity;

namespace Beeexy.Tests.Unit.Domain;

public sealed class AccountTests
{
    [Fact]
    public void Create_StartsActive_AndStatusCanBeDisabledAndReactivated()
    {
        var createdAt = Utc(12);
        var account = Account.Create(NormalizedEmail.Create("person@example.com"), createdAt);

        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Null(account.UpdatedAt);

        account.Disable(createdAt.AddMinutes(1));
        Assert.Equal(AccountStatus.Disabled, account.Status);
        Assert.Equal(createdAt.AddMinutes(1), account.UpdatedAt);

        account.Activate(createdAt.AddMinutes(2));
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(createdAt.AddMinutes(2), account.UpdatedAt);
    }

    [Fact]
    public void Disable_RejectsTimestampBeforeCreation()
    {
        var createdAt = Utc(12);
        var account = Account.Create(NormalizedEmail.Create("person@example.com"), createdAt);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            account.Disable(createdAt.AddSeconds(-1)));
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 19, hour, 0, 0, TimeSpan.Zero);
    }
}
