using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ShareGrantConfiguration : IEntityTypeConfiguration<ShareGrant>
{
    public void Configure(EntityTypeBuilder<ShareGrant> builder)
    {
        builder.ToTable(
            "share_grants",
            "sharing",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_share_grants_scope",
                    "scope IN ('full_profile', 'case', 'pre_triage', 'visit', 'specific_records')");
                table.HasCheckConstraint(
                    "ck_share_grants_expiry",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_share_grants_revocation",
                    "(revoked_at IS NULL AND revoked_by_account_id IS NULL) OR " +
                    "(revoked_at >= created_at AND revoked_by_account_id IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_share_grants_capability_hash",
                    $"length(btrim(capability_hash)) >= " +
                    $"{SharingPersistenceLimits.CapabilityHashMinimum} AND " +
                    "capability_hash !~ '[[:space:]]'");
                table.HasCheckConstraint(
                    "ck_share_grants_request_fingerprint",
                    $"request_fingerprint ~ '^[0-9a-f]{{" +
                    $"{SharingPersistenceLimits.RequestFingerprint}}}$'");
                table.HasCheckConstraint(
                    "ck_share_grants_version",
                    "version > 0");
            });

        builder.HasKey(grant => grant.Id)
            .HasName("pk_share_grants");

        builder.Property(grant => grant.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(grant => grant.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(grant => grant.CreatorAccountId)
            .HasColumnName("creator_account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(grant => grant.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .HasDefaultValueSql("gen_random_uuid()")
            .IsRequired();

        builder.Property(grant => grant.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasConversion(
                fingerprint => fingerprint.Value,
                value => ShareRequestFingerprint.Create(value))
            .HasMaxLength(SharingPersistenceLimits.RequestFingerprint)
            .HasDefaultValueSql("repeat('0', 64)")
            .IsRequired();

        builder.Property(grant => grant.Scope)
            .HasColumnName("scope")
            .HasConversion(
                scope => SharingPersistence.StoreScope(scope),
                value => SharingPersistence.LoadScope(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(grant => grant.CapabilityHash)
            .HasColumnName("capability_hash")
            .HasConversion(hash => hash.Value, value => TokenHash.FromHash(value))
            .HasMaxLength(TokenHash.MaximumLength)
            .IsRequired();

        builder.Property(grant => grant.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(grant => grant.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(grant => grant.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(grant => grant.RevokedByAccountId)
            .HasColumnName("revoked_by_account_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);

        builder.Property(grant => grant.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(grant => grant.CapabilityHash)
            .IsUnique()
            .HasDatabaseName("ux_share_grants_capability_hash");

        builder.HasIndex(grant => new
        {
            grant.PatientProfileId,
            grant.IdempotencyKey
        })
            .IsUnique()
            .HasDatabaseName("ux_share_grants_patient_idempotency_key");

        builder.HasIndex(grant => new
        {
            grant.PatientProfileId,
            grant.CreatedAt,
            grant.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_share_grants_patient_created_id");

        builder.HasIndex(grant => new
        {
            grant.ExpiresAt,
            grant.Id
        })
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_share_grants_active_expiry_id");

        builder.HasIndex(grant => grant.CreatorAccountId)
            .HasDatabaseName("ix_share_grants_creator_account");

        builder.HasIndex(grant => grant.RevokedByAccountId)
            .HasFilter("revoked_by_account_id IS NOT NULL")
            .HasDatabaseName("ix_share_grants_revoker_account");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(grant => grant.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_share_grants_patient_profile");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(grant => grant.CreatorAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_share_grants_creator_account");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(grant => grant.RevokedByAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_share_grants_revoker_account");
    }
}
