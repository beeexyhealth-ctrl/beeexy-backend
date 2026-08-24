using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ClinicalHistoryEventConfiguration
    : IEntityTypeConfiguration<ClinicalHistoryEvent>
{
    public void Configure(EntityTypeBuilder<ClinicalHistoryEvent> builder)
    {
        builder.ToTable(
            "clinical_history_events",
            "history",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_clinical_history_events_supported_type",
                    "event_type = 'completed_pre_triage' AND " +
                    "source_type = 'pre_triage_episode'");
                table.HasCheckConstraint(
                    "ck_clinical_history_events_recorded_at",
                    "recorded_at >= occurred_at");
            });

        builder.HasKey(historyEvent => historyEvent.Id)
            .HasName("pk_clinical_history_events");

        builder.HasAlternateKey(historyEvent => new
        {
            historyEvent.Id,
            historyEvent.SourceType,
            historyEvent.SourceId,
            historyEvent.SourceQuestionnaireVersionId,
            historyEvent.SourceClinicalRuleSetVersionId
        })
            .HasName("ak_clinical_history_events_source_provenance");

        builder.HasAlternateKey(historyEvent => new
        {
            historyEvent.Id,
            historyEvent.PatientProfileId
        })
            .HasName("ak_clinical_history_events_id_patient_profile");

        builder.Property(historyEvent => historyEvent.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(historyEvent => historyEvent.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(historyEvent => historyEvent.EventType)
            .HasColumnName("event_type")
            .HasConversion(
                eventType => ClinicalHistoryPersistence.StoreEventType(eventType),
                value => ClinicalHistoryPersistence.LoadEventType(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(historyEvent => historyEvent.SourceType)
            .HasColumnName("source_type")
            .HasConversion(
                sourceType => ClinicalHistoryPersistence.StoreSourceType(sourceType),
                value => ClinicalHistoryPersistence.LoadSourceType(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(historyEvent => historyEvent.SourceId)
            .HasColumnName("source_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(historyEvent => historyEvent.SourceQuestionnaireVersionId)
            .HasColumnName("source_questionnaire_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(historyEvent => historyEvent.SourceClinicalRuleSetVersionId)
            .HasColumnName("source_clinical_rule_set_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(historyEvent => historyEvent.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(historyEvent => historyEvent.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Ignore(historyEvent => historyEvent.SourceReference);
        builder.Ignore(historyEvent => historyEvent.SourceProvenance);

        builder.HasIndex(historyEvent => new
        {
            historyEvent.SourceType,
            historyEvent.SourceId,
            historyEvent.EventType
        })
            .IsUnique()
            .HasDatabaseName("ux_clinical_history_events_source_projection");

        builder.HasIndex(historyEvent => new
        {
            historyEvent.SourceId,
            historyEvent.SourceQuestionnaireVersionId
        })
            .HasDatabaseName("ix_clinical_history_events_source_questionnaire");

        builder.HasIndex(historyEvent => new
        {
            historyEvent.SourceId,
            historyEvent.SourceClinicalRuleSetVersionId
        })
            .HasDatabaseName("ix_clinical_history_events_source_rule_set");

        builder.HasIndex(historyEvent => new
        {
            historyEvent.PatientProfileId,
            historyEvent.OccurredAt,
            historyEvent.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_clinical_history_events_patient_occurred_id");

        builder.HasIndex(historyEvent => new
        {
            historyEvent.PatientProfileId,
            historyEvent.EventType
        })
            .HasDatabaseName("ix_clinical_history_events_patient_event_type");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(historyEvent => historyEvent.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_history_events_patient_profile");

        builder.HasOne<PreTriageEpisode>()
            .WithMany()
            .HasForeignKey(historyEvent => new
            {
                historyEvent.SourceId,
                historyEvent.SourceQuestionnaireVersionId
            })
            .HasPrincipalKey(episode => new
            {
                episode.Id,
                episode.QuestionnaireVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_history_events_source_questionnaire");

        builder.HasOne<PreTriageEpisode>()
            .WithMany()
            .HasForeignKey(historyEvent => new
            {
                historyEvent.SourceId,
                historyEvent.SourceClinicalRuleSetVersionId
            })
            .HasPrincipalKey(episode => new
            {
                episode.Id,
                episode.ClinicalRuleSetVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_history_events_source_rule_set");
    }
}
