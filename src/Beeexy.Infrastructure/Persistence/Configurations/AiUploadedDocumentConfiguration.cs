using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiUploadedDocumentConfiguration
    : IEntityTypeConfiguration<AiUploadedDocument>
{
    public void Configure(EntityTypeBuilder<AiUploadedDocument> builder)
    {
        builder.ToTable(
            "ai_uploaded_documents",
            "ai",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ai_uploaded_documents_status",
                    "status IN ('active', 'deleted', 'expired')");
                table.HasCheckConstraint(
                    "ck_ai_uploaded_documents_size",
                    "size_bytes > 0");
                table.HasCheckConstraint(
                    "ck_ai_uploaded_documents_expiry",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_ai_uploaded_documents_lifecycle",
                    "(status = 'active' AND deleted_at IS NULL) OR " +
                    "(status = 'deleted' AND deleted_at >= created_at) OR " +
                    "(status = 'expired' AND deleted_at >= expires_at)");
            });

        builder.HasKey(document => document.Id).HasName("pk_ai_uploaded_documents");
        builder.Property(document => document.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(document => document.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(document => document.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);
        builder.Property(document => document.AnalysisRequestId)
            .HasColumnName("analysis_request_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);
        builder.Property(document => document.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(AiPersistenceLimits.StorageKey)
            .IsRequired();
        builder.Property(document => document.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(AiPersistenceLimits.ContentType)
            .IsRequired();
        builder.Property(document => document.SizeBytes)
            .HasColumnName("size_bytes")
            .IsRequired();
        builder.Property(document => document.Status)
            .HasColumnName("status")
            .HasConversion(
                status => AiPersistence.StoreDocumentStatus(status),
                value => AiPersistence.LoadDocumentStatus(value))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(document => document.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(document => document.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(document => document.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(document => document.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_ai_uploaded_documents_storage_key");
        builder.HasIndex(document => new
        {
            document.AccountId,
            document.CreatedAt,
            document.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_ai_uploaded_documents_account_created_id");
        builder.HasIndex(document => new
        {
            document.Status,
            document.ExpiresAt,
            document.Id
        })
            .HasDatabaseName("ix_ai_uploaded_documents_status_expiry_id");
        builder.HasIndex(document => new
        {
            document.PatientProfileId,
            document.CreatedAt,
            document.Id
        })
            .HasFilter("patient_profile_id IS NOT NULL")
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_ai_uploaded_documents_patient_created_id");
        builder.HasIndex(document => document.AnalysisRequestId)
            .HasFilter("analysis_request_id IS NOT NULL")
            .HasDatabaseName("ix_ai_uploaded_documents_analysis_request");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(document => document.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_uploaded_documents_account");
        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(document => document.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_uploaded_documents_patient_profile");
        builder.HasOne<AiAnalysisRequest>()
            .WithMany()
            .HasForeignKey(document => document.AnalysisRequestId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_uploaded_documents_analysis_request");
    }
}
