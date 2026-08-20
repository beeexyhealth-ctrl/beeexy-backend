using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccountProvisioningConcurrencyTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task ConcurrentFirstSignIns_ResolveOneAccountProfilePreferenceAndBeeexyIdentity()
    {
        await EnsureMigratedAsync();
        var email = NormalizedEmail.Create($"provision-race-{Guid.NewGuid():N}@example.com");
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                await using var scope = factory.Services.CreateAsyncScope();
                var transaction = scope.ServiceProvider
                    .GetRequiredService<IIdentityVerificationTransaction>();
                var provision = scope.ServiceProvider
                    .GetRequiredService<ProvisionAccountAndPrimaryProfile>();

                await transaction.BeginAsync();
                var result = await provision.ExecuteAsync(email, DateTimeOffset.UtcNow);
                await transaction.SaveChangesAsync();
                await transaction.CommitAsync();
                return (
                    AccountId: result.Account.Id,
                    ProfileId: result.PrimaryProfile.Id,
                    BeeexyId: result.PrimaryProfile.BeeexyId.Value);
            }))
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results.Select(result => result.AccountId).Distinct());
        Assert.Single(results.Select(result => result.ProfileId).Distinct());
        Assert.Single(results.Select(result => result.BeeexyId).Distinct());

        await using var dbContext = CreateDbContext();
        var account = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == email);
        var profile = await dbContext.PatientProfiles
            .AsNoTracking()
            .SingleAsync(candidate => candidate.AccountId == account.Id);
        var preference = await dbContext.UserPreferences
            .AsNoTracking()
            .SingleAsync(candidate => candidate.AccountId == account.Id);

        Assert.Equal(results[0].AccountId, account.Id);
        Assert.Equal(results[0].ProfileId, profile.Id);
        Assert.Equal(results[0].BeeexyId, profile.BeeexyId.Value);
        Assert.Equal("Etc/UTC", preference.TimeZone.Value);
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }
}
