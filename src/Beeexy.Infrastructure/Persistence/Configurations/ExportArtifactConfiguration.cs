using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ExportArtifactConfiguration : IEntityTypeConfiguration<ExportArtifact>
{
    public void Configure(EntityTypeBuilder<ExportArtifact> builder)
    {
        builder.ToTable(
            "export_artifacts",
            "sharing",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_export_artifacts_format",
                    "format IN ('beeexy_json', 'pdf', 'fhir_json')");
                table.HasCheckConstraint(
                    "ck_export_artifacts_status",
                    "status IN ('pending', 'available', 'failed', 'deleted')");
                table.HasCheckConstraint(
                    "ck_export_artifacts_media_type",
                    "media_type ~ '^[^[:space:]/]+/[^[:space:]/]+$'");
                table.HasCheckConstraint(
                    "ck_export_artifacts_snapshot_version",
                    "length(btrim(snapshot_version)) > 0");
                table.HasCheckConstraint(
                    "ck_export_artifacts_retention",
                    "retention_eligible_at > created_at");
                table.HasCheckConstraint(
                    "ck_export_artifacts_version",
                    "version > 0");
                table.HasCheckConstraint(
                    "ck_export_artifacts_checksum",
                    "(checksum_algorithm IS NULL AND checksum IS NULL) OR " +
                    "(checksum_algorithm !~ '[[:space:]]' AND checksum !~ '[[:space:]]')");
                table.HasCheckConstraint(
                    "ck_export_artifacts_lifecycle",
                    "(status = 'pending' AND checksum_algorithm IS NULL AND checksum IS NULL " +
                    "AND private_storage_identity IS NULL AND failure_category IS NULL " +
                    "AND completed_at IS NULL AND failed_at IS NULL AND deleted_at IS NULL) OR " +
                    "(status = 'available' AND length(btrim(checksum_algorithm)) > 0 " +
                    "AND length(btrim(checksum)) > 0 " +
                    "AND length(btrim(private_storage_identity)) > 0 " +
                    "AND failure_category IS NULL AND completed_at >= created_at " +
                    "AND failed_at IS NULL AND deleted_at IS NULL) OR " +
                    "(status = 'failed' AND checksum_algorithm IS NULL AND checksum IS NULL " +
                    "AND private_storage_identity IS NULL AND length(btrim(failure_category)) > 0 " +
                    "AND completed_at IS NULL AND failed_at >= created_at AND deleted_at IS NULL) OR " +
                    "(status = 'deleted' AND length(btrim(checksum_algorithm)) > 0 " +
                    "AND length(btrim(checksum)) > 0 " +
                    "AND length(btrim(private_storage_identity)) > 0 " +
                    "AND failure_category IS NULL AND completed_at >= created_at " +
                    "AND failed_at IS NULL AND deleted_at >= retention_eligible_at)");
            });

        builder.HasKey(artifact => artifact.Id)
            .HasName("pk_export_artifacts");

        builder.Property(artifact => artifact.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(artifact => artifact.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(artifact => artifact.RequestedByAccountId)
            .HasColumnName("requested_by_account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(artifact => artifact.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(artifact => artifact.Format)
            .HasColumnName("format")
            .HasConversion(
                value => SharingPersistence.StoreExportFormat(value),
                value => SharingPersistence.LoadExportFormat(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(artifact => artifact.MediaType)
            .HasColumnName("media_type")
            .HasMaxLength(SharingPersistenceLimits.MediaType)
            .IsRequired();

        builder.Property(artifact => artifact.SnapshotId)
            .HasColumnName("snapshot_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(artifact => artifact.SnapshotVersion)
            .HasColumnName("snapshot_version")
            .HasMaxLength(SharingPersistenceLimits.SnapshotVersion)
            .IsRequired();

        builder.Property(artifact => artifact.Status)
            .HasColumnName("status")
            .HasConversion(
                value => SharingPersistence.StoreExportStatus(value),
                value => SharingPersistence.LoadExportStatus(value))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(artifact => artifact.ChecksumAlgorithm)
            .HasColumnName("checksum_algorithm")
            .HasMaxLength(SharingPersistenceLimits.ChecksumAlgorithm);

        builder.Property(artifact => artifact.Checksum)
            .HasColumnName("checksum")
            .HasMaxLength(SharingPersistenceLimits.Checksum);

        builder.Property(artifact => artifact.PrivateStorageIdentity)
            .HasColumnName("private_storage_identity")
            .HasMaxLength(SharingPersistenceLimits.PrivateStorageIdentity);

        builder.Property(artifact => artifact.FailureCategory)
            .HasColumnName("failure_category")
            .HasMaxLength(SharingPersistenceLimits.FailureCategory);

        builder.Property(artifact => artifact.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(artifact => artifact.RetentionEligibleAt)
            .HasColumnName("retention_eligible_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(artifact => artifact.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(artifact => artifact.FailedAt)
            .HasColumnName("failed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(artifact => artifact.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(artifact => artifact.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(artifact => artifact.Content);

        builder.HasIndex(artifact => new
        {
            artifact.PatientProfileId,
            artifact.IdempotencyKey
        })
            .IsUnique()
            .HasDatabaseName("ux_export_artifacts_patient_idempotency_key");

        builder.HasIndex(artifact => artifact.PrivateStorageIdentity)
            .IsUnique()
            .HasFilter("private_storage_identity IS NOT NULL")
            .HasDatabaseName("ux_export_artifacts_private_storage_identity");

        builder.HasIndex(artifact => new
        {
            artifact.PatientProfileId,
            artifact.CreatedAt,
            artifact.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_export_artifacts_patient_created_id");

        builder.HasIndex(artifact => new
        {
            artifact.Status,
            artifact.RetentionEligibleAt,
            artifact.Id
        })
            .HasDatabaseName("ix_export_artifacts_status_retention_id");

        builder.HasIndex(artifact => artifact.RequestedByAccountId)
            .HasDatabaseName("ix_export_artifacts_requester_account");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(artifact => artifact.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_export_artifacts_patient_profile");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(artifact => artifact.RequestedByAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_export_artifacts_requester_account");
    }
}
