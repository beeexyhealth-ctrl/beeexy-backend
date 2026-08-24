using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class FhirValidationResultConfiguration
    : IEntityTypeConfiguration<FhirValidationResult>
{
    public void Configure(EntityTypeBuilder<FhirValidationResult> builder)
    {
        builder.ToTable(
            "fhir_validation_results",
            "interoperability",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fhir_validation_results_outcome",
                    "(outcome = 'passed' AND error_count = 0) OR " +
                    "(outcome = 'failed' AND error_count > 0)");
                table.HasCheckConstraint(
                    "ck_fhir_validation_results_counts",
                    "error_count >= 0 AND warning_count >= 0");
                table.HasCheckConstraint(
                    "ck_fhir_validation_results_metadata",
                    "length(btrim(validator_name)) > 0 AND " +
                    "length(btrim(validator_version)) > 0 AND " +
                    "length(btrim(artifact_checksum_algorithm)) > 0 AND " +
                    "length(btrim(artifact_checksum)) > 0");
            });

        builder.HasKey(result => result.Id)
            .HasName("pk_fhir_validation_results");

        builder.Property(result => result.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(result => result.FhirExportId)
            .HasColumnName("fhir_export_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(result => result.Outcome)
            .HasColumnName("outcome")
            .HasConversion(
                outcome => FhirExportPersistence.StoreValidationOutcome(outcome),
                value => FhirExportPersistence.LoadValidationOutcome(value))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(result => result.ValidatorName)
            .HasColumnName("validator_name")
            .HasMaxLength(FhirValidatorMetadata.MaximumNameLength)
            .IsRequired();

        builder.Property(result => result.ValidatorVersion)
            .HasColumnName("validator_version")
            .HasMaxLength(FhirValidatorMetadata.MaximumVersionLength)
            .IsRequired();

        builder.Property(result => result.ArtifactChecksumAlgorithm)
            .HasColumnName("artifact_checksum_algorithm")
            .HasMaxLength(FhirArtifactMetadata.MaximumChecksumAlgorithmLength)
            .IsRequired();

        builder.Property(result => result.ArtifactChecksum)
            .HasColumnName("artifact_checksum")
            .HasMaxLength(FhirArtifactMetadata.MaximumChecksumLength)
            .IsRequired();

        builder.Property(result => result.ErrorCount)
            .HasColumnName("error_count")
            .IsRequired();

        builder.Property(result => result.WarningCount)
            .HasColumnName("warning_count")
            .IsRequired();

        builder.Property(result => result.ValidatedAt)
            .HasColumnName("validated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Ignore(result => result.IsValid);
        builder.Ignore(result => result.Validator);

        builder.HasIndex(result => result.FhirExportId)
            .IsUnique()
            .HasDatabaseName("ux_fhir_validation_results_export_id");

        builder.HasIndex(result => new { result.Outcome, result.ValidatedAt })
            .HasDatabaseName("ix_fhir_validation_results_outcome_validated_at");

        builder.HasOne<FhirExport>()
            .WithOne()
            .HasForeignKey<FhirValidationResult>(result => result.FhirExportId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_fhir_validation_results_export");
    }
}
