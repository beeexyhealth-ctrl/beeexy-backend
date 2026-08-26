using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class DemoGuestProvisioningTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task FirstAndRepeatedProvisioning_CreateThenVerifyOneCompleteNormalIdentity()
    {
        await EnsureMigratedAsync();
        var definition = Definition("provision-idempotent");
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        var first = await ProvisionAsync(factory, definition);
        var repeated = await ProvisionAsync(factory, definition);

        Assert.True(first.WasProvisioned);
        Assert.False(repeated.WasProvisioned);
        Assert.Equal(first.AccountId, repeated.AccountId);
        Assert.Equal(first.ProfileId, repeated.ProfileId);
        Assert.Equal(first.BeeexyId, repeated.BeeexyId);
        Assert.StartsWith("BXY-", first.BeeexyId, StringComparison.Ordinal);

        await using var db = CreateDbContext();
        var account = await db.Accounts.AsNoTracking()
            .SingleAsync(candidate => candidate.Email == definition.Email);
        var profile = await db.PatientProfiles.AsNoTracking()
            .SingleAsync(candidate => candidate.AccountId == account.Id);
        var preference = await db.UserPreferences.AsNoTracking()
            .SingleAsync(candidate => candidate.AccountId == account.Id);

        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(definition.FirstName, profile.FirstName);
        Assert.Equal(definition.LastName, profile.LastName);
        Assert.Equal(definition.DateOfBirth, profile.DateOfBirth);
        Assert.Equal(definition.SexAssignedAtBirth, profile.SexAssignedAtBirth);
        Assert.Equal(definition.State, profile.State);
        Assert.Equal(definition.TimeZone, preference.TimeZone);
        Assert.False(await db.ExternalIdentities.AnyAsync(
            identity => identity.AccountId == account.Id));
        Assert.False(await db.EmailAuthenticationChallenges.AnyAsync(
            challenge => challenge.Email == definition.Email));
    }

    [Fact]
    public async Task ConcurrentProvisioning_CreatesOneAccountProfileAndPreference()
    {
        await EnsureMigratedAsync();
        var definition = Definition("provision-concurrent");
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await ProvisionAsync(factory, definition);
        })).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results.Where(result => result.WasProvisioned));
        Assert.Single(results.Select(result => result.AccountId).Distinct());
        Assert.Single(results.Select(result => result.ProfileId).Distinct());

        await using var db = CreateDbContext();
        var account = await db.Accounts.AsNoTracking()
            .SingleAsync(candidate => candidate.Email == definition.Email);
        Assert.Equal(1, await db.PatientProfiles.CountAsync(
            profile => profile.AccountId == account.Id));
        Assert.Equal(1, await db.UserPreferences.CountAsync(
            preference => preference.AccountId == account.Id));
    }

    [Fact]
    public async Task ExistingUnrelatedAccount_FailsWithoutOverwritingItsProfile()
    {
        await EnsureMigratedAsync();
        var definition = Definition("provision-conflict");
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var transaction = scope.ServiceProvider
                .GetRequiredService<IIdentityVerificationTransaction>();
            var standardProvisioning = scope.ServiceProvider
                .GetRequiredService<ProvisionAccountAndPrimaryProfile>();
            await transaction.BeginAsync();
            await standardProvisioning.ExecuteAsync(
                definition.Email,
                DateTimeOffset.UtcNow);
            await transaction.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await Assert.ThrowsAsync<DemoGuestProvisioningConflictException>(() =>
            ProvisionAsync(factory, definition));

        await using var db = CreateDbContext();
        var account = await db.Accounts.AsNoTracking()
            .SingleAsync(candidate => candidate.Email == definition.Email);
        var profile = await db.PatientProfiles.AsNoTracking()
            .SingleAsync(candidate => candidate.AccountId == account.Id);
        var preference = await db.UserPreferences.AsNoTracking()
            .SingleAsync(candidate => candidate.AccountId == account.Id);
        Assert.Null(profile.FirstName);
        Assert.Null(profile.LastName);
        Assert.Null(profile.DateOfBirth);
        Assert.Null(profile.SexAssignedAtBirth);
        Assert.Null(profile.State);
        Assert.Equal("Etc/UTC", preference.TimeZone.Value);
    }

    private static async Task<ProvisionDemoGuestResult> ProvisionAsync(
        BeeexyApiFactory factory,
        DemoGuestDefinition definition)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ProvisionDemoGuest>()
            .ExecuteAsync(definition);
    }

    private static DemoGuestDefinition Definition(string prefix) => new(
        NormalizedEmail.Create($"{prefix}-{Guid.NewGuid():N}@example.com"),
        PatientName.Create("Bee"),
        PatientName.Create("Exy"),
        new DateOnly(1990, 5, 20),
        SexAssignedAtBirth.Female,
        UsState.Create("CA"),
        UserTimeZone.Create("America/Lima"));

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }
}
