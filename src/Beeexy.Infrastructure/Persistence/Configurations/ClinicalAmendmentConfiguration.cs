using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ClinicalAmendmentConfiguration
    : IEntityTypeConfiguration<ClinicalAmendment>
{
    public void Configure(EntityTypeBuilder<ClinicalAmendment> builder)
    {
        builder.ToTable(
            "clinical_amendments",
            "history",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_clinical_amendments_supported_source",
                    "source_type = 'pre_triage_episode'");
                table.HasCheckConstraint(
                    "ck_clinical_amendments_reason",
                    "length(btrim(reason)) > 0");
            });

        builder.HasKey(amendment => amendment.Id)
            .HasName("pk_clinical_amendments");

        builder.Property(amendment => amendment.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(amendment => amendment.ClinicalHistoryEventId)
            .HasColumnName("clinical_history_event_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(amendment => amendment.SourceType)
            .HasColumnName("source_type")
            .HasConversion(
                sourceType => ClinicalHistoryPersistence.StoreSourceType(sourceType),
                value => ClinicalHistoryPersistence.LoadSourceType(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(amendment => amendment.SourceId)
            .HasColumnName("source_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(amendment => amendment.SourceQuestionnaireVersionId)
            .HasColumnName("source_questionnaire_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(amendment => amendment.SourceClinicalRuleSetVersionId)
            .HasColumnName("source_clinical_rule_set_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(amendment => amendment.AuthorAccountId)
            .HasColumnName("author_account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(amendment => amendment.Reason)
            .HasColumnName("reason")
            .HasColumnType("text")
            .HasConversion(
                reason => reason.Value,
                value => AmendmentReason.Create(value))
            .IsRequired();

        builder.Property(amendment => amendment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Ignore(amendment => amendment.SourceReference);
        builder.Ignore(amendment => amendment.SourceProvenance);

        builder.HasIndex(amendment => new
        {
            amendment.ClinicalHistoryEventId,
            amendment.CreatedAt,
            amendment.Id
        })
            .HasDatabaseName("ix_clinical_amendments_event_created_id");

        builder.HasIndex(amendment => amendment.AuthorAccountId)
            .HasDatabaseName("ix_clinical_amendments_author_account");

        builder.HasIndex(amendment => amendment.SourceId)
            .HasDatabaseName("ix_clinical_amendments_source_episode");

        builder.HasIndex(amendment => new
        {
            amendment.ClinicalHistoryEventId,
            amendment.SourceType,
            amendment.SourceId,
            amendment.SourceQuestionnaireVersionId,
            amendment.SourceClinicalRuleSetVersionId
        })
            .HasDatabaseName("ix_clinical_amendments_event_source_provenance");

        builder.HasOne<ClinicalHistoryEvent>()
            .WithMany()
            .HasForeignKey(amendment => new
            {
                amendment.ClinicalHistoryEventId,
                amendment.SourceType,
                amendment.SourceId,
                amendment.SourceQuestionnaireVersionId,
                amendment.SourceClinicalRuleSetVersionId
            })
            .HasPrincipalKey(historyEvent => new
            {
                historyEvent.Id,
                historyEvent.SourceType,
                historyEvent.SourceId,
                historyEvent.SourceQuestionnaireVersionId,
                historyEvent.SourceClinicalRuleSetVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_amendments_event_source_provenance");

        builder.HasOne<PreTriageEpisode>()
            .WithMany()
            .HasForeignKey(amendment => amendment.SourceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_amendments_source_episode");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(amendment => amendment.AuthorAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_amendments_author_account");
    }
}
