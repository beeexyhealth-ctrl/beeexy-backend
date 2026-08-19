using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class IdentityPersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Entities_PersistAndReloadWithValueObjectsStatusesAndRelationships()
    {
        await EnsureMigratedAsync();
        var now = UtcNow();
        var suffix = UniqueSuffix();
        var account = Account.Create(NormalizedEmail.Create($"persist-{suffix}@example.com"), now);
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{suffix}"),
            now,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("America/Lima"),
            now);
        var externalIdentity = ExternalIdentity.Create(
            account.Id,
            "Google",
            $"subject-{suffix}",
            now);
        var session = RefreshSession.Create(
            account.Id,
            TokenHash.FromHash($"refresh-hash-{suffix}"),
            now.AddDays(30),
            now);
        var challenge = EmailAuthenticationChallenge.Create(
            account.Email,
            TokenHash.FromHash($"otp-hash-{suffix}"),
            now.AddMinutes(10),
            now);

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(account, profile, preference, externalIdentity, session, challenge);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedAccount = await dbContext.Accounts.AsNoTracking().SingleAsync(x => x.Id == account.Id);
            var savedProfile = await dbContext.PatientProfiles.AsNoTracking().SingleAsync(x => x.Id == profile.Id);
            var savedPreference = await dbContext.UserPreferences.AsNoTracking().SingleAsync(x => x.Id == preference.Id);
            var savedIdentity = await dbContext.ExternalIdentities.AsNoTracking().SingleAsync(x => x.Id == externalIdentity.Id);
            var savedSession = await dbContext.RefreshSessions.AsNoTracking().SingleAsync(x => x.Id == session.Id);
            var savedChallenge = await dbContext.EmailAuthenticationChallenges.AsNoTracking().SingleAsync(x => x.Id == challenge.Id);

            Assert.Equal(account.Email, savedAccount.Email);
            Assert.Equal(AccountStatus.Active, savedAccount.Status);
            Assert.Equal(account.Id, savedProfile.AccountId);
            Assert.Equal(profile.BeeexyId, savedProfile.BeeexyId);
            Assert.Equal("America/Lima", savedPreference.TimeZone.Value);
            Assert.Equal("google", savedIdentity.Provider);
            Assert.Equal(session.RefreshTokenHash, savedSession.RefreshTokenHash);
            Assert.Equal(RefreshSessionStatus.Active, savedSession.Status);
            Assert.Equal(challenge.OtpHash, savedChallenge.OtpHash);
            Assert.Equal(ChallengeStatus.Pending, savedChallenge.Status);
            Assert.Equal(0, savedChallenge.AttemptCount);
        }
    }

    [Fact]
    public async Task Accounts_RejectDuplicateNormalizedEmail()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var now = UtcNow();
        var first = Account.Create(NormalizedEmail.Create($"Case-{suffix}@Example.com"), now);
        var duplicate = Account.Create(NormalizedEmail.Create($"case-{suffix}@example.COM"), now);

        await SaveAsync(first);

        await AssertUniqueViolationAsync(() => SaveAsync(duplicate), "ux_accounts_normalized_email");
    }

    [Fact]
    public async Task ExternalIdentities_RejectDuplicateProviderAndSubject()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var now = UtcNow();
        var firstAccount = Account.Create(NormalizedEmail.Create($"external-a-{suffix}@example.com"), now);
        var secondAccount = Account.Create(NormalizedEmail.Create($"external-b-{suffix}@example.com"), now);

        await SaveAsync(firstAccount, secondAccount);
        await SaveAsync(ExternalIdentity.Create(firstAccount.Id, "Google", $"subject-{suffix}", now));

        await AssertUniqueViolationAsync(
            () => SaveAsync(ExternalIdentity.Create(secondAccount.Id, "GOOGLE", $"subject-{suffix}", now)),
            "ux_external_identities_provider_subject");
    }

    [Fact]
    public async Task PatientProfiles_RejectMoreThanOneOwnedProfilePerAccount()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var now = UtcNow();
        var account = Account.Create(NormalizedEmail.Create($"profile-owner-{suffix}@example.com"), now);

        await SaveAsync(account);
        await SaveAsync(PatientProfile.Create(BeeexyId.Create($"BXY-A-{suffix}"), now, account.Id));

        await AssertUniqueViolationAsync(
            () => SaveAsync(PatientProfile.Create(BeeexyId.Create($"BXY-B-{suffix}"), now, account.Id)),
            "ux_patient_profiles_account_id");
    }

    [Fact]
    public async Task PatientProfiles_AllowMultipleRowsWithoutAccountOwner()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var now = UtcNow();
        var first = PatientProfile.Create(BeeexyId.Create($"BXY-UNOWNED-A-{suffix}"), now);
        var second = PatientProfile.Create(BeeexyId.Create($"BXY-UNOWNED-B-{suffix}"), now);

        await SaveAsync(first, second);

        await using var dbContext = CreateDbContext();
        var persisted = await dbContext.PatientProfiles
            .AsNoTracking()
            .CountAsync(profile => profile.Id == first.Id || profile.Id == second.Id);
        Assert.Equal(2, persisted);
    }

    [Fact]
    public async Task PatientProfiles_RejectDuplicateBeeexyId()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var now = UtcNow();
        var beeexyId = BeeexyId.Create($"BXY-DUPLICATE-{suffix}");

        await SaveAsync(PatientProfile.Create(beeexyId, now));

        await AssertUniqueViolationAsync(
            () => SaveAsync(PatientProfile.Create(beeexyId, now)),
            "ux_patient_profiles_beeexy_id");
    }

    [Fact]
    public async Task AccountDelete_IsRestrictedWhenPatientOrIdentityDataExists()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var now = UtcNow();
        var account = Account.Create(NormalizedEmail.Create($"delete-safe-{suffix}@example.com"), now);
        var profile = PatientProfile.Create(BeeexyId.Create($"BXY-SAFE-{suffix}"), now, account.Id);

        await SaveAsync(account, profile);

        await using var dbContext = CreateDbContext();
        dbContext.Accounts.Remove(account);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
        Assert.Equal("fk_patient_profiles_accounts_account_id", postgresException.ConstraintName);
    }

    private BeeexyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
        return new BeeexyDbContext(options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
    }

    private static async Task AssertUniqueViolationAsync(
        Func<Task> action,
        string expectedConstraint)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(expectedConstraint, postgresException.ConstraintName);
    }

    private static DateTimeOffset UtcNow()
    {
        return new DateTimeOffset(2026, 8, 19, 22, 0, 0, TimeSpan.Zero);
    }

    private static string UniqueSuffix()
    {
        return Guid.NewGuid().ToString("N");
    }
}
