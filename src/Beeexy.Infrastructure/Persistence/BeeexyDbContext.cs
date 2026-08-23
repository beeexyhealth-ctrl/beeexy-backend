using Beeexy.Domain.Identity;
using Beeexy.Domain.History;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
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

    public DbSet<CareRelationship> CareRelationships => Set<CareRelationship>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<PreTriageSession> PreTriageSessions => Set<PreTriageSession>();

    public DbSet<PreTriageEpisode> PreTriageEpisodes => Set<PreTriageEpisode>();

    public DbSet<QuestionnaireDefinitionVersion> QuestionnaireVersions =>
        Set<QuestionnaireDefinitionVersion>();

    public DbSet<TriageQuestion> TriageQuestions => Set<TriageQuestion>();

    public DbSet<TriageAnswer> TriageAnswers => Set<TriageAnswer>();

    public DbSet<ReportedSymptom> ReportedSymptoms => Set<ReportedSymptom>();

    public DbSet<ClinicalRuleSetVersion> ClinicalRuleSetVersions =>
        Set<ClinicalRuleSetVersion>();

    public DbSet<ClinicalAssessment> ClinicalAssessments => Set<ClinicalAssessment>();

    public DbSet<ClinicalFinding> ClinicalFindings => Set<ClinicalFinding>();

    public DbSet<PreTriageHistoryProjectionRecord> PreTriageHistoryProjectionRecords =>
        Set<PreTriageHistoryProjectionRecord>();

    public DbSet<ClinicalHistoryEvent> ClinicalHistoryEvents =>
        Set<ClinicalHistoryEvent>();

    public DbSet<ClinicalAmendment> ClinicalAmendments => Set<ClinicalAmendment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeeexyDbContext).Assembly);
    }
}
