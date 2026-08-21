using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class CareRelationshipConfiguration
    : IEntityTypeConfiguration<CareRelationship>
{
    public void Configure(EntityTypeBuilder<CareRelationship> builder)
    {
        builder.ToTable(
            "care_relationships",
            "patients",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_care_relationships_distinct_profiles",
                    "manager_profile_id <> subject_profile_id");
                table.HasCheckConstraint(
                    "ck_care_relationships_type",
                    "relationship_type IN " +
                    "('parent', 'legal_guardian', 'caregiver', 'spouse', 'child', 'sibling', 'other')");
                table.HasCheckConstraint(
                    "ck_care_relationships_status",
                    "status IN ('active', 'revoked')");
                table.HasCheckConstraint(
                    "ck_care_relationships_attestation_version",
                    "length(btrim(attestation_version)) > 0");
                table.HasCheckConstraint(
                    "ck_care_relationships_attestation_timestamp",
                    "attested_at <= created_at");
                table.HasCheckConstraint(
                    "ck_care_relationships_revocation",
                    "(status = 'active' AND revoked_at IS NULL " +
                    "AND revoked_by_account_id IS NULL AND updated_at IS NULL) OR " +
                    "(status = 'revoked' AND revoked_at IS NOT NULL " +
                    "AND revoked_by_account_id IS NOT NULL AND updated_at = revoked_at)");
                table.HasCheckConstraint(
                    "ck_care_relationships_revocation_timestamp",
                    "revoked_at IS NULL OR revoked_at >= created_at");
            });

        builder.HasKey(relationship => relationship.Id)
            .HasName("pk_care_relationships");

        builder.Property(relationship => relationship.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(relationship => relationship.ManagerProfileId)
            .HasColumnName("manager_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(relationship => relationship.SubjectProfileId)
            .HasColumnName("subject_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(relationship => relationship.RelationshipType)
            .HasColumnName("relationship_type")
            .HasConversion(
                type => type == CareRelationshipType.LegalGuardian
                    ? "legal_guardian"
                    : type.ToString().ToLowerInvariant(),
                value => value == "legal_guardian"
                    ? CareRelationshipType.LegalGuardian
                    : Enum.Parse<CareRelationshipType>(value, true))
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(relationship => relationship.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<CareRelationshipStatus>(value, true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(relationship => relationship.CreatedByAccountId)
            .HasColumnName("created_by_account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.OwnsOne(
            relationship => relationship.Attestation,
            attestation =>
            {
                attestation.Property(value => value.Version)
                    .HasColumnName("attestation_version")
                    .HasMaxLength(AuthorizationAttestation.MaximumVersionLength)
                    .IsRequired();

                attestation.Property(value => value.AttestedAt)
                    .HasColumnName("attested_at")
                    .HasColumnType("timestamp with time zone")
                    .IsRequired();
            });
        builder.Navigation(relationship => relationship.Attestation).IsRequired();

        builder.Property(relationship => relationship.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(relationship => relationship.RevokedByAccountId)
            .HasColumnName("revoked_by_account_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(relationship => relationship.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(relationship => relationship.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(relationship => new
            {
                relationship.ManagerProfileId,
                relationship.Status
            })
            .HasDatabaseName("ix_care_relationships_manager_status");

        builder.HasIndex(relationship => new
            {
                relationship.SubjectProfileId,
                relationship.Status
            })
            .HasDatabaseName("ix_care_relationships_subject_status");

        builder.HasIndex(relationship => new
            {
                relationship.ManagerProfileId,
                relationship.SubjectProfileId
            })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_care_relationships_active_manager_subject");

        builder.HasIndex(relationship => relationship.CreatedByAccountId)
            .HasDatabaseName("ix_care_relationships_created_by_account_id");

        builder.HasIndex(relationship => relationship.RevokedByAccountId)
            .HasDatabaseName("ix_care_relationships_revoked_by_account_id");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(relationship => relationship.ManagerProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_care_relationships_patient_profiles_manager_profile_id");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(relationship => relationship.SubjectProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_care_relationships_patient_profiles_subject_profile_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(relationship => relationship.CreatedByAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_care_relationships_accounts_created_by_account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(relationship => relationship.RevokedByAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_care_relationships_accounts_revoked_by_account_id");
    }
}
