using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PreTriageEpisodeConfiguration
    : IEntityTypeConfiguration<PreTriageEpisode>
{
    public void Configure(EntityTypeBuilder<PreTriageEpisode> builder)
    {
        builder.ToTable(
            "pre_triage_episodes",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_pre_triage_episodes_anonymous_claim",
                    "(patient_profile_id IS NULL AND anonymous_expires_at IS NOT NULL " +
                    "AND claimed_at IS NULL) OR " +
                    "(patient_profile_id IS NOT NULL AND " +
                    "((anonymous_expires_at IS NULL AND claimed_at IS NULL) OR " +
                    "(anonymous_expires_at IS NOT NULL AND claimed_at IS NOT NULL)))");
                table.HasCheckConstraint(
                    "ck_pre_triage_episodes_anonymous_expiration",
                    "anonymous_expires_at IS NULL OR anonymous_expires_at > completed_at");
                table.HasCheckConstraint(
                    "ck_pre_triage_episodes_claim_timestamp",
                    "claimed_at IS NULL OR " +
                    "(claimed_at >= completed_at AND claimed_at < anonymous_expires_at)");
            });

        builder.HasKey(episode => episode.Id)
            .HasName("pk_pre_triage_episodes");

        builder.HasAlternateKey(episode => new
        {
            episode.Id,
            episode.QuestionnaireVersionId
        })
            .HasName("ak_pre_triage_episodes_id_questionnaire_version_id");

        builder.HasAlternateKey(episode => new
        {
            episode.Id,
            episode.ClinicalRuleSetVersionId
        })
            .HasName("ak_pre_triage_episodes_id_clinical_rule_set_version_id");

        builder.Property(episode => episode.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(episode => episode.SourceSessionId)
            .HasColumnName("source_session_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(episode => episode.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(episode => episode.QuestionnaireVersionId)
            .HasColumnName("questionnaire_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(episode => episode.ClinicalRuleSetVersionId)
            .HasColumnName("clinical_rule_set_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(episode => episode.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(episode => episode.AnonymousExpiresAt)
            .HasColumnName("anonymous_expires_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(episode => episode.ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");

        builder.Ignore(episode => episode.IsClaimed);

        builder.HasIndex(episode => episode.SourceSessionId)
            .IsUnique()
            .HasDatabaseName("ux_pre_triage_episodes_source_session_id");

        builder.HasIndex(episode => new { episode.PatientProfileId, episode.CompletedAt })
            .HasDatabaseName("ix_pre_triage_episodes_patient_completed_at");

        builder.HasIndex(episode => episode.AnonymousExpiresAt)
            .HasFilter("patient_profile_id IS NULL")
            .HasDatabaseName("ix_pre_triage_episodes_unclaimed_expiry");

        builder.HasIndex(episode => episode.QuestionnaireVersionId)
            .HasDatabaseName("ix_pre_triage_episodes_questionnaire_version_id");

        builder.HasIndex(episode => episode.ClinicalRuleSetVersionId)
            .HasDatabaseName("ix_pre_triage_episodes_clinical_rule_set_version_id");

        builder.HasIndex(episode => new
        {
            episode.SourceSessionId,
            episode.QuestionnaireVersionId
        })
            .HasDatabaseName("ix_pre_triage_episodes_source_session_version");

        builder.HasOne<PreTriageSession>()
            .WithMany()
            .HasForeignKey(episode => new
            {
                episode.SourceSessionId,
                episode.QuestionnaireVersionId
            })
            .HasPrincipalKey(session => new
            {
                session.Id,
                session.QuestionnaireVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pre_triage_episodes_sessions_source_session_version");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(episode => episode.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pre_triage_episodes_patient_profiles_patient_profile_id");

        builder.HasOne<QuestionnaireDefinitionVersion>()
            .WithMany()
            .HasForeignKey(episode => episode.QuestionnaireVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_pre_triage_episodes_questionnaire_versions_version_id");

        builder.HasOne<ClinicalRuleSetVersion>()
            .WithMany()
            .HasForeignKey(episode => episode.ClinicalRuleSetVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_pre_triage_episodes_rule_set_versions_version_id");

        builder.HasMany(episode => episode.Answers)
            .WithOne()
            .HasForeignKey(answer => new
            {
                answer.EpisodeId,
                answer.QuestionnaireVersionId
            })
            .HasPrincipalKey(episode => new
            {
                episode.Id,
                episode.QuestionnaireVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_answers_pre_triage_episodes_episode_version");
        builder.Navigation(episode => episode.Answers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(episode => episode.ReportedSymptoms)
            .WithOne()
            .HasForeignKey(symptom => symptom.EpisodeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_reported_symptoms_pre_triage_episodes_episode_id");
        builder.Navigation(episode => episode.ReportedSymptoms)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
