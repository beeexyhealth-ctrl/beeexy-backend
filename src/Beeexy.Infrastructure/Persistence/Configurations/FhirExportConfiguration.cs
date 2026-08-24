using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class FhirExportConfiguration : IEntityTypeConfiguration<FhirExport>
{
    public void Configure(EntityTypeBuilder<FhirExport> builder)
    {
        builder.ToTable(
            "fhir_exports",
            "interoperability",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fhir_exports_versions",
                    "length(btrim(fhir_version)) > 0 AND " +
                    "length(btrim(mapping_version)) > 0 AND " +
                    "((profile_canonical IS NULL AND profile_version IS NULL) OR " +
                    "(length(btrim(profile_canonical)) > 0 AND " +
                    "length(btrim(profile_version)) > 0))");
                table.HasCheckConstraint(
                    "ck_fhir_exports_status",
                    "status IN ('pending', 'generated', 'validation_failed', 'validated')");
                table.HasCheckConstraint(
                    "ck_fhir_exports_lifecycle_metadata",
                    "(status = 'pending' AND checksum_algorithm IS NULL AND " +
                    "checksum IS NULL AND private_artifact_storage_uri IS NULL AND " +
                    "generated_at IS NULL AND validation_completed_at IS NULL AND " +
                    "validation_outcome IS NULL) OR " +
                    "(status = 'generated' AND length(btrim(checksum_algorithm)) > 0 AND " +
                    "length(btrim(checksum)) > 0 AND " +
                    "length(btrim(private_artifact_storage_uri)) > 0 AND " +
                    "generated_at IS NOT NULL AND validation_completed_at IS NULL AND " +
                    "validation_outcome IS NULL) OR " +
                    "(status = 'validation_failed' AND " +
                    "length(btrim(checksum_algorithm)) > 0 AND length(btrim(checksum)) > 0 AND " +
                    "length(btrim(private_artifact_storage_uri)) > 0 AND " +
                    "generated_at IS NOT NULL AND validation_completed_at IS NOT NULL AND " +
                    "validation_outcome = 'failed') OR " +
                    "(status = 'validated' AND length(btrim(checksum_algorithm)) > 0 AND " +
                    "length(btrim(checksum)) > 0 AND " +
                    "length(btrim(private_artifact_storage_uri)) > 0 AND " +
                    "generated_at IS NOT NULL AND validation_completed_at IS NOT NULL AND " +
                    "validation_outcome = 'passed')");
                table.HasCheckConstraint(
                    "ck_fhir_exports_timestamps",
                    "updated_at >= created_at AND " +
                    "(generated_at IS NULL OR generated_at >= created_at) AND " +
                    "(validation_completed_at IS NULL OR " +
                    "validation_completed_at >= generated_at) AND " +
                    "updated_at >= COALESCE(validation_completed_at, generated_at, created_at)");
            });

        builder.HasKey(export => export.Id)
            .HasName("pk_fhir_exports");

        builder.Property(export => export.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(export => export.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(export => export.SourceClinicalHistoryEventId)
            .HasColumnName("source_clinical_history_event_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(export => export.FhirVersion)
            .HasColumnName("fhir_version")
            .HasMaxLength(FhirExportVersionMetadata.MaximumVersionLength)
            .IsRequired();

        builder.Property(export => export.MappingVersion)
            .HasColumnName("mapping_version")
            .HasMaxLength(FhirExportVersionMetadata.MaximumVersionLength)
            .IsRequired();

        builder.Property(export => export.ProfileCanonical)
            .HasColumnName("profile_canonical")
            .HasMaxLength(FhirExportVersionMetadata.MaximumProfileCanonicalLength);

        builder.Property(export => export.ProfileVersion)
            .HasColumnName("profile_version")
            .HasMaxLength(FhirExportVersionMetadata.MaximumVersionLength);

        builder.Property(export => export.Status)
            .HasColumnName("status")
            .HasConversion(
                status => FhirExportPersistence.StoreStatus(status),
                value => FhirExportPersistence.LoadStatus(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(export => export.ChecksumAlgorithm)
            .HasColumnName("checksum_algorithm")
            .HasMaxLength(FhirArtifactMetadata.MaximumChecksumAlgorithmLength);

        builder.Property(export => export.Checksum)
            .HasColumnName("checksum")
            .HasMaxLength(FhirArtifactMetadata.MaximumChecksumLength);

        builder.Property(export => export.PrivateArtifactStorageUri)
            .HasColumnName("private_artifact_storage_uri")
            .HasMaxLength(FhirArtifactMetadata.MaximumStorageUriLength);

        builder.Property(export => export.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(export => export.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(export => export.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(export => export.GeneratedAt)
            .HasColumnName("generated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(export => export.ValidationCompletedAt)
            .HasColumnName("validation_completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(export => export.ValidationOutcome)
            .HasColumnName("validation_outcome")
            .HasConversion(
                outcome => outcome.HasValue
                    ? FhirExportPersistence.StoreValidationOutcome(outcome.Value)
                    : null,
                value => value == null
                    ? null
                    : FhirExportPersistence.LoadValidationOutcome(value))
            .HasMaxLength(16);

        builder.Ignore(export => export.ValidatedAt);
        builder.Ignore(export => export.Versions);
        builder.Ignore(export => export.Artifact);

        builder.HasIndex(export => new
        {
            export.PatientProfileId,
            export.IdempotencyKey
        })
            .IsUnique()
            .HasDatabaseName("ux_fhir_exports_patient_idempotency_key");

        builder.HasIndex(export => new
        {
            export.PatientProfileId,
            export.CreatedAt,
            export.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_fhir_exports_patient_created_id");

        builder.HasIndex(export => new
        {
            export.Status,
            export.UpdatedAt
        })
            .HasDatabaseName("ix_fhir_exports_status_updated_at");

        builder.HasIndex(export => new
        {
            export.SourceClinicalHistoryEventId,
            export.PatientProfileId
        })
            .HasDatabaseName("ix_fhir_exports_source_history_event_patient");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(export => export.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_fhir_exports_patient_profile");

        builder.HasOne<ClinicalHistoryEvent>()
            .WithMany()
            .HasForeignKey(export => new
            {
                export.SourceClinicalHistoryEventId,
                export.PatientProfileId
            })
            .HasPrincipalKey(historyEvent => new
            {
                historyEvent.Id,
                historyEvent.PatientProfileId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_fhir_exports_source_history_event_patient");
    }
}
