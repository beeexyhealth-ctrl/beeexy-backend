using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiResultSnapshotConfiguration
    : IEntityTypeConfiguration<AiResultSnapshot>
{
    public void Configure(EntityTypeBuilder<AiResultSnapshot> builder)
    {
        builder.ToTable(
            "ai_result_snapshots",
            "ai",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ai_result_snapshots_sequence",
                    "sequence > 0");
                table.HasCheckConstraint(
                    "ck_ai_result_snapshots_schema",
                    "length(btrim(result_schema_version)) > 0");
                table.HasCheckConstraint(
                    "ck_ai_result_snapshots_content",
                    "jsonb_typeof(content) = 'object'");
            });

        builder.HasKey(snapshot => snapshot.Id).HasName("pk_ai_result_snapshots");
        builder.HasAlternateKey(snapshot => new { snapshot.Id, snapshot.ExecutionId })
            .HasName("ak_ai_result_snapshots_id_execution");
        builder.Property(snapshot => snapshot.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(snapshot => snapshot.AnalysisRequestId)
            .HasColumnName("analysis_request_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(snapshot => snapshot.ExecutionId)
            .HasColumnName("execution_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(snapshot => snapshot.Sequence)
            .HasColumnName("sequence")
            .IsRequired();
        builder.Property(snapshot => snapshot.ResultSchemaVersion)
            .HasColumnName("result_schema_version")
            .HasMaxLength(AiPersistenceLimits.SchemaVersion)
            .IsRequired();
        builder.Property(snapshot => snapshot.ContentJson)
            .HasColumnName("content")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(snapshot => snapshot.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(snapshot => snapshot.ExecutionId)
            .IsUnique()
            .HasDatabaseName("ux_ai_result_snapshots_execution");
        builder.HasIndex(snapshot => new
        {
            snapshot.ExecutionId,
            snapshot.AnalysisRequestId
        })
            .HasDatabaseName("ix_ai_result_snapshots_execution_analysis");
        builder.HasIndex(snapshot => new
        {
            snapshot.AnalysisRequestId,
            snapshot.Sequence
        })
            .IsUnique()
            .HasDatabaseName("ux_ai_result_snapshots_analysis_sequence");
        builder.HasIndex(snapshot => new
        {
            snapshot.AnalysisRequestId,
            snapshot.CreatedAt,
            snapshot.Id
        })
            .HasDatabaseName("ix_ai_result_snapshots_analysis_created_id");
        builder.HasOne<AiExecution>()
            .WithMany()
            .HasForeignKey(snapshot => new
            {
                snapshot.ExecutionId,
                snapshot.AnalysisRequestId
            })
            .HasPrincipalKey(execution => new
            {
                execution.Id,
                execution.AnalysisRequestId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_result_snapshots_execution_analysis");
        builder.HasOne<AiAnalysisRequest>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.AnalysisRequestId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_result_snapshots_analysis_request");
    }
}
