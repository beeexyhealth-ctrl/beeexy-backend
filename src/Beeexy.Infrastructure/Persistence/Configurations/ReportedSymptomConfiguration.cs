using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ReportedSymptomConfiguration
    : IEntityTypeConfiguration<ReportedSymptom>
{
    public void Configure(EntityTypeBuilder<ReportedSymptom> builder)
    {
        builder.ToTable(
            "reported_symptoms",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_reported_symptoms_owner",
                    "(session_id IS NOT NULL AND episode_id IS NULL) OR " +
                    "(session_id IS NULL AND episode_id IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_reported_symptoms_original_text",
                    "length(btrim(original_text)) > 0");
                table.HasCheckConstraint(
                    "ck_reported_symptoms_sequence",
                    "sequence > 0");
                table.HasCheckConstraint(
                    "ck_reported_symptoms_normalization",
                    "(terminology_system IS NULL AND terminology_code IS NULL " +
                    "AND terminology_display IS NULL AND normalization_source IS NULL " +
                    "AND normalized_at IS NULL) OR " +
                    "(terminology_system IS NOT NULL AND terminology_code IS NOT NULL " +
                    "AND normalization_source IS NOT NULL AND normalized_at IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_reported_symptoms_normalized_at",
                    "normalized_at IS NULL OR normalized_at >= reported_at");
            });

        builder.HasKey(symptom => symptom.Id)
            .HasName("pk_reported_symptoms");

        builder.Property(symptom => symptom.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(symptom => symptom.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(symptom => symptom.EpisodeId)
            .HasColumnName("episode_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(symptom => symptom.OriginalText)
            .HasColumnName("original_text")
            .HasConversion(text => text.Value, value => SymptomText.Create(value))
            .HasMaxLength(SymptomText.MaximumLength)
            .IsRequired();

        builder.Property(symptom => symptom.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        builder.Property(symptom => symptom.TerminologySystem)
            .HasColumnName("terminology_system")
            .HasMaxLength(ReportedSymptom.MaximumTerminologySystemLength);

        builder.Property(symptom => symptom.TerminologyCode)
            .HasColumnName("terminology_code")
            .HasMaxLength(ReportedSymptom.MaximumTerminologyCodeLength);

        builder.Property(symptom => symptom.TerminologyDisplay)
            .HasColumnName("terminology_display")
            .HasMaxLength(ReportedSymptom.MaximumTerminologyDisplayLength);

        builder.Property(symptom => symptom.NormalizationSource)
            .HasColumnName("normalization_source")
            .HasMaxLength(ReportedSymptom.MaximumNormalizationSourceLength);

        builder.Property(symptom => symptom.NormalizedAt)
            .HasColumnName("normalized_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(symptom => symptom.ReportedAt)
            .HasColumnName("reported_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(symptom => new { symptom.SessionId, symptom.Sequence })
            .IsUnique()
            .HasFilter("session_id IS NOT NULL")
            .HasDatabaseName("ux_reported_symptoms_session_sequence");

        builder.HasIndex(symptom => new { symptom.EpisodeId, symptom.Sequence })
            .IsUnique()
            .HasFilter("episode_id IS NOT NULL")
            .HasDatabaseName("ux_reported_symptoms_episode_sequence");

    }
}
