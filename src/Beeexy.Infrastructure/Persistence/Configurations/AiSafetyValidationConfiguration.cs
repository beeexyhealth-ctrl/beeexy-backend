using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiSafetyValidationConfiguration
    : IEntityTypeConfiguration<AiSafetyValidation>
{
    public void Configure(EntityTypeBuilder<AiSafetyValidation> builder)
    {
        builder.ToTable(
            "ai_safety_validations",
            "ai",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ai_safety_validations_category",
                    "category IN ('approved', 'unsafe_medical_advice', 'diagnosis', " +
                    "'prescription', 'unsupported', 'malformed')");
                table.HasCheckConstraint(
                    "ck_ai_safety_validations_policy",
                    "length(btrim(policy_version)) > 0 AND " +
                    "(product_content_version IS NULL OR " +
                    "length(btrim(product_content_version)) > 0)");
                table.HasCheckConstraint(
                    "ck_ai_safety_validations_display",
                    "(category = 'approved' AND display_eligible AND " +
                    "result_snapshot_id IS NOT NULL AND restricted_audit_output IS NULL) OR " +
                    "(category <> 'approved' AND NOT display_eligible AND " +
                    "result_snapshot_id IS NULL AND " +
                    "length(btrim(restricted_audit_output)) > 0)");
            });

        builder.HasKey(validation => validation.Id).HasName("pk_ai_safety_validations");
        builder.Property(validation => validation.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(validation => validation.ExecutionId)
            .HasColumnName("execution_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(validation => validation.ResultSnapshotId)
            .HasColumnName("result_snapshot_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);
        builder.Property(validation => validation.Category)
            .HasColumnName("category")
            .HasConversion(
                category => AiPersistence.StoreSafetyCategory(category),
                value => AiPersistence.LoadSafetyCategory(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(validation => validation.PolicyVersion)
            .HasColumnName("policy_version")
            .HasMaxLength(AiPersistenceLimits.PolicyVersion)
            .IsRequired();
        builder.Property(validation => validation.ProductContentVersion)
            .HasColumnName("product_content_version")
            .HasMaxLength(AiPersistenceLimits.ProductContentVersion);
        builder.Property(validation => validation.DisplayEligible)
            .HasColumnName("display_eligible")
            .IsRequired();
        builder.Property(validation => validation.RestrictedAuditOutput)
            .HasColumnName("restricted_audit_output")
            .HasColumnType("text");
        builder.Property(validation => validation.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(validation => validation.ExecutionId)
            .IsUnique()
            .HasDatabaseName("ux_ai_safety_validations_execution");
        builder.HasIndex(validation => validation.ResultSnapshotId)
            .IsUnique()
            .HasFilter("result_snapshot_id IS NOT NULL")
            .HasDatabaseName("ux_ai_safety_validations_result_snapshot");
        builder.HasIndex(validation => new
        {
            validation.ResultSnapshotId,
            validation.ExecutionId
        })
            .HasDatabaseName("ix_ai_safety_validations_result_execution");
        builder.HasOne<AiExecution>()
            .WithMany()
            .HasForeignKey(validation => validation.ExecutionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_safety_validations_execution");
        builder.HasOne<AiResultSnapshot>()
            .WithMany()
            .HasForeignKey(validation => new
            {
                validation.ResultSnapshotId,
                validation.ExecutionId
            })
            .HasPrincipalKey(snapshot => new
            {
                snapshot.Id,
                snapshot.ExecutionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_safety_validations_result_execution");
    }
}
