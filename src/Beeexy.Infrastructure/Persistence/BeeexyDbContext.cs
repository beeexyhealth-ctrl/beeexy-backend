using Beeexy.Domain.Ai;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Identity;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Scheduling;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Persistence;

public sealed class BeeexyDbContext(DbContextOptions<BeeexyDbContext> options)
    : DbContext(options)
{
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();

    public DbSet<AiMessage> AiMessages => Set<AiMessage>();

    public DbSet<AiAnalysisRequest> AiAnalysisRequests => Set<AiAnalysisRequest>();

    public DbSet<AiResultSnapshot> AiResultSnapshots => Set<AiResultSnapshot>();

    public DbSet<AiExecution> AiExecutions => Set<AiExecution>();

    public DbSet<AiUploadedDocument> AiUploadedDocuments => Set<AiUploadedDocument>();

    public DbSet<AiSafetyValidation> AiSafetyValidations => Set<AiSafetyValidation>();

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<ClinicLocation> ClinicLocations => Set<ClinicLocation>();

    public DbSet<Doctor> Doctors => Set<Doctor>();

    public DbSet<DoctorAffiliation> DoctorAffiliations => Set<DoctorAffiliation>();

    public DbSet<DoctorCredential> DoctorCredentials => Set<DoctorCredential>();

    public DbSet<Specialty> Specialties => Set<Specialty>();

    public DbSet<DoctorSpecialty> DoctorSpecialties => Set<DoctorSpecialty>();

    public DbSet<Language> Languages => Set<Language>();

    public DbSet<DoctorLanguage> DoctorLanguages => Set<DoctorLanguage>();

    public DbSet<InsurancePlan> InsurancePlans => Set<InsurancePlan>();

    public DbSet<DoctorInsuranceParticipation> DoctorInsuranceParticipations =>
        Set<DoctorInsuranceParticipation>();

    public DbSet<DoctorMatchRuleVersion> DoctorMatchRuleVersions =>
        Set<DoctorMatchRuleVersion>();

    public DbSet<DoctorMatchRuleConfiguration> DoctorMatchRuleConfigurations =>
        Set<DoctorMatchRuleConfiguration>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<EmailAuthenticationChallenge> EmailAuthenticationChallenges =>
        Set<EmailAuthenticationChallenge>();

    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<PrivateAccessCredential> PrivateAccessCredentials =>
        Set<PrivateAccessCredential>();

    public DbSet<PrivateAccessSession> PrivateAccessSessions => Set<PrivateAccessSession>();

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();

    public DbSet<CareRelationship> CareRelationships => Set<CareRelationship>();

    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<AppointmentStatusHistory> AppointmentStatusHistory =>
        Set<AppointmentStatusHistory>();

    public DbSet<AppointmentRescheduleHistory> AppointmentRescheduleHistory =>
        Set<AppointmentRescheduleHistory>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<PreTriageSession> PreTriageSessions => Set<PreTriageSession>();

    public DbSet<PreTriageIntakeIdempotencyRecord> PreTriageIntakeIdempotencyRecords =>
        Set<PreTriageIntakeIdempotencyRecord>();

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

    public DbSet<FhirExport> FhirExports => Set<FhirExport>();

    public DbSet<FhirValidationResult> FhirValidationResults =>
        Set<FhirValidationResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeeexyDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureSchedulingAuditIsAppendOnly();
        EnsureAiHistoryIsProtected();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureSchedulingAuditIsAppendOnly();
        EnsureAiHistoryIsProtected();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureSchedulingAuditIsAppendOnly()
    {
        var auditMutation = ChangeTracker.Entries()
            .FirstOrDefault(entry =>
                (entry.Entity is AppointmentStatusHistory ||
                 entry.Entity is AppointmentRescheduleHistory) &&
                entry.State is EntityState.Modified or EntityState.Deleted);
        if (auditMutation is not null)
        {
            throw new InvalidOperationException(
                "Scheduling history records are append-only and cannot be changed or deleted.");
        }

        if (ChangeTracker.Entries<Appointment>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Appointments are historical records and cannot be deleted.");
        }
    }

    private void EnsureAiHistoryIsProtected()
    {
        var immutableMutation = ChangeTracker.Entries()
            .FirstOrDefault(entry =>
                entry.Entity is AiMessage or
                    AiAnalysisRequest or
                    AiResultSnapshot or
                    AiSafetyValidation &&
                entry.State is EntityState.Modified or EntityState.Deleted);
        if (immutableMutation is not null)
        {
            throw new InvalidOperationException(
                "AI messages, analysis inputs, result snapshots, and safety validations " +
                "are append-only and cannot be changed or deleted.");
        }

        var destructiveDeletion = ChangeTracker.Entries()
            .FirstOrDefault(entry =>
                entry.Entity is AiConversation or
                    AiExecution or
                    AiUploadedDocument &&
                entry.State == EntityState.Deleted);
        if (destructiveDeletion is not null)
        {
            throw new InvalidOperationException(
                "AI history and lifecycle metadata cannot be physically deleted.");
        }
    }
}
