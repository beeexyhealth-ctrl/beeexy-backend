using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PreTriageHistoryProjectionRecordConfiguration
    : IEntityTypeConfiguration<PreTriageHistoryProjectionRecord>
{
    public void Configure(EntityTypeBuilder<PreTriageHistoryProjectionRecord> builder)
    {
        builder.ToTable(
            "pre_triage_projection_records",
            "history",
            table => table.HasCheckConstraint(
                "ck_pre_triage_projection_records_created_at",
                "created_at >= completed_at"));

        builder.HasKey(record => record.SourceEpisodeId)
            .HasName("pk_pre_triage_projection_records");

        builder.Property(record => record.SourceEpisodeId)
            .HasColumnName("source_episode_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(record => record.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(record => record.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(record => record.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(record => new { record.PatientProfileId, record.CompletedAt })
            .HasDatabaseName("ix_pre_triage_projection_records_patient_completed_at");

        builder.HasOne<PreTriageEpisode>()
            .WithOne()
            .HasForeignKey<PreTriageHistoryProjectionRecord>(
                record => record.SourceEpisodeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pre_triage_projection_records_source_episode_id");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(record => record.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_pre_triage_projection_records_patient_profile_id");
    }
}
