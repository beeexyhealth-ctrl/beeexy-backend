using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiExecutionConfiguration : IEntityTypeConfiguration<AiExecution>
{
    public void Configure(EntityTypeBuilder<AiExecution> builder)
    {
        builder.ToTable(
            "ai_executions",
            "ai",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ai_executions_status",
                    "status IN ('pending', 'running', 'succeeded', 'failed', 'rejected')");
                table.HasCheckConstraint(
                    "ck_ai_executions_lifecycle",
                    "(status = 'pending' AND provider_identifier IS NULL AND " +
                    "model_identifier IS NULL AND prompt_version IS NULL AND " +
                    "started_at IS NULL AND completed_at IS NULL AND " +
                    "latency_milliseconds IS NULL AND sanitized_failure_category IS NULL) OR " +
                    "(status = 'running' AND length(btrim(provider_identifier)) > 0 AND " +
                    "length(btrim(model_identifier)) > 0 AND length(btrim(prompt_version)) > 0 " +
                    "AND started_at IS NOT NULL AND completed_at IS NULL AND " +
                    "latency_milliseconds IS NULL AND sanitized_failure_category IS NULL) OR " +
                    "(status IN ('succeeded', 'rejected') AND " +
                    "length(btrim(provider_identifier)) > 0 AND " +
                    "length(btrim(model_identifier)) > 0 AND length(btrim(prompt_version)) > 0 " +
                    "AND started_at IS NOT NULL AND completed_at IS NOT NULL AND " +
                    "latency_milliseconds >= 0 AND sanitized_failure_category IS NULL) OR " +
                    "(status = 'failed' AND length(btrim(provider_identifier)) > 0 AND " +
                    "length(btrim(model_identifier)) > 0 AND length(btrim(prompt_version)) > 0 " +
                    "AND started_at IS NOT NULL AND completed_at IS NOT NULL AND " +
                    "latency_milliseconds >= 0 AND " +
                    "length(btrim(sanitized_failure_category)) > 0)");
                table.HasCheckConstraint(
                    "ck_ai_executions_timestamps",
                    "(started_at IS NULL OR started_at >= created_at) AND " +
                    "(completed_at IS NULL OR completed_at >= started_at)");
            });

        builder.HasKey(execution => execution.Id).HasName("pk_ai_executions");
        builder.HasAlternateKey(execution => new
        {
            execution.Id,
            execution.AnalysisRequestId
        }).HasName("ak_ai_executions_id_analysis_request");
        builder.Property(execution => execution.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(execution => execution.AnalysisRequestId)
            .HasColumnName("analysis_request_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(execution => execution.Status)
            .HasColumnName("status")
            .HasConversion(
                status => AiPersistence.StoreExecutionStatus(status),
                value => AiPersistence.LoadExecutionStatus(value))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(execution => execution.ProviderIdentifier)
            .HasColumnName("provider_identifier")
            .HasMaxLength(AiPersistenceLimits.Identifier);
        builder.Property(execution => execution.ModelIdentifier)
            .HasColumnName("model_identifier")
            .HasMaxLength(AiPersistenceLimits.ModelIdentifier);
        builder.Property(execution => execution.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(AiPersistenceLimits.Identifier);
        builder.Property(execution => execution.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(execution => execution.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(execution => execution.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(execution => execution.LatencyMilliseconds)
            .HasColumnName("latency_milliseconds");
        builder.Property(execution => execution.SanitizedFailureCategory)
            .HasColumnName("sanitized_failure_category")
            .HasMaxLength(AiPersistenceLimits.FailureCategory);

        builder.HasIndex(execution => new
        {
            execution.Status,
            execution.CreatedAt,
            execution.Id
        })
            .HasDatabaseName("ix_ai_executions_status_created_id");
        builder.HasIndex(execution => new
        {
            execution.AnalysisRequestId,
            execution.CreatedAt,
            execution.Id
        })
            .HasDatabaseName("ix_ai_executions_analysis_created_id");
        builder.HasOne<AiAnalysisRequest>()
            .WithMany()
            .HasForeignKey(execution => execution.AnalysisRequestId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_executions_analysis_request");
    }
}
