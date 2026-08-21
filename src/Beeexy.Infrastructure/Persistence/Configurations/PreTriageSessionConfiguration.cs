using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PreTriageSessionConfiguration
    : IEntityTypeConfiguration<PreTriageSession>
{
    public void Configure(EntityTypeBuilder<PreTriageSession> builder)
    {
        builder.ToTable(
            "pre_triage_sessions",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_pre_triage_sessions_status",
                    "status IN ('active', 'completed')");
                table.HasCheckConstraint(
                    "ck_pre_triage_sessions_expiration",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_pre_triage_sessions_ownership",
                    "(patient_profile_id IS NULL AND anonymous_capability_hash IS NOT NULL) OR " +
                    "(patient_profile_id IS NOT NULL AND anonymous_capability_hash IS NULL)");
                table.HasCheckConstraint(
                    "ck_pre_triage_sessions_completion",
                    "(status = 'active' AND completed_at IS NULL) OR " +
                    "(status = 'completed' AND completed_at IS NOT NULL " +
                    "AND completed_at >= created_at AND completed_at < expires_at)");
            });

        builder.HasKey(session => session.Id)
            .HasName("pk_pre_triage_sessions");

        builder.HasAlternateKey(session => new
        {
            session.Id,
            session.QuestionnaireVersionId
        })
            .HasName("ak_pre_triage_sessions_id_questionnaire_version_id");

        builder.Property(session => session.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(session => session.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(session => session.QuestionnaireVersionId)
            .HasColumnName("questionnaire_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(session => session.AnonymousCapabilityHash)
            .HasColumnName("anonymous_capability_hash")
            .HasConversion(
                hash => hash == null ? null : hash.Value,
                value => value == null ? null : AnonymousCapabilityHash.FromHash(value))
            .HasMaxLength(AnonymousCapabilityHash.MaximumLength);

        builder.Property(session => session.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<PreTriageSessionStatus>(value, true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(session => session.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(session => session.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.Ignore(session => session.IsAnonymous);

        builder.HasIndex(session => session.AnonymousCapabilityHash)
            .IsUnique()
            .HasFilter("anonymous_capability_hash IS NOT NULL")
            .HasDatabaseName("ux_pre_triage_sessions_anonymous_capability_hash");

        builder.HasIndex(session => new { session.Status, session.ExpiresAt })
            .HasDatabaseName("ix_pre_triage_sessions_status_expiry");

        builder.HasIndex(session => session.PatientProfileId)
            .HasDatabaseName("ix_pre_triage_sessions_patient_profile_id");

        builder.HasIndex(session => session.QuestionnaireVersionId)
            .HasDatabaseName("ix_pre_triage_sessions_questionnaire_version_id");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(session => session.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pre_triage_sessions_patient_profiles_patient_profile_id");

        builder.HasOne<QuestionnaireDefinitionVersion>()
            .WithMany()
            .HasForeignKey(session => session.QuestionnaireVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_pre_triage_sessions_questionnaire_versions_version_id");

        builder.HasMany(session => session.Answers)
            .WithOne()
            .HasForeignKey(answer => new
            {
                answer.SessionId,
                answer.QuestionnaireVersionId
            })
            .HasPrincipalKey(session => new
            {
                session.Id,
                session.QuestionnaireVersionId
            })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_answers_pre_triage_sessions_session_version");
        builder.Navigation(session => session.Answers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(session => session.ReportedSymptoms)
            .WithOne()
            .HasForeignKey(symptom => symptom.SessionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_reported_symptoms_pre_triage_sessions_session_id");
        builder.Navigation(session => session.ReportedSymptoms)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
