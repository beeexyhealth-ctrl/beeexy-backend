using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Persistence;

public sealed class BeeexyDbContext(DbContextOptions<BeeexyDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<EmailAuthenticationChallenge> EmailAuthenticationChallenges =>
        Set<EmailAuthenticationChallenge>();

    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeeexyDbContext).Assembly);
    }
}
