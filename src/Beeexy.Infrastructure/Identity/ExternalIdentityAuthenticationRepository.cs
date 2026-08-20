using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class ExternalIdentityAuthenticationRepository(BeeexyDbContext dbContext)
    : IExternalIdentityAuthenticationRepository
{
    private const long AdvisoryLockNamespace = 2505;

    public async Task AcquireIdentityLockAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var identityKey = $"{provider.Trim().ToLowerInvariant()}\u001f{subject.Trim()}";

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identityKey}, {AdvisoryLockNamespace}))",
            cancellationToken);
    }

    public Task<ExternalIdentity?> FindIdentityAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedSubject = subject.Trim();
        return dbContext.ExternalIdentities.SingleOrDefaultAsync(
            identity => identity.Provider == normalizedProvider &&
                identity.Subject == normalizedSubject,
            cancellationToken);
    }

    public Task<Account?> FindAccountAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Accounts.SingleOrDefaultAsync(
            account => account.Id == accountId,
            cancellationToken);
    }

    public Task<Account?> FindAccountAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        return dbContext.Accounts.SingleOrDefaultAsync(
            account => account.Email == email,
            cancellationToken);
    }

    public Task<PatientProfile?> FindPrimaryProfileAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.PatientProfiles.SingleOrDefaultAsync(
            profile => profile.AccountId == accountId,
            cancellationToken);
    }

    public void Add(ExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        dbContext.ExternalIdentities.Add(identity);
    }
}
