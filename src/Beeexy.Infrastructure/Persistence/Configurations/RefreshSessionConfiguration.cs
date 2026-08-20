using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable(
            "refresh_sessions",
            "identity",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_refresh_sessions_status",
                    "\"status\" IN ('active', 'revoked', 'expired')");
                table.HasCheckConstraint(
                    "ck_refresh_sessions_expiration",
                    "\"expires_at\" > \"created_at\"");
                table.HasCheckConstraint(
                    "ck_refresh_sessions_revoked",
                    "(\"status\" = 'revoked' AND \"revoked_at\" IS NOT NULL) OR " +
                    "(\"status\" <> 'revoked' AND \"revoked_at\" IS NULL)");
                table.HasCheckConstraint(
                    "ck_refresh_sessions_rotation",
                    "(\"rotated_at\" IS NULL AND \"replaced_by_session_id\" IS NULL) OR " +
                    "(\"rotated_at\" IS NOT NULL AND \"replaced_by_session_id\" IS NOT NULL)");
            });

        builder.HasKey(session => session.Id)
            .HasName("pk_refresh_sessions");

        builder.Property(session => session.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(session => session.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(session => session.FamilyId)
            .HasColumnName("family_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(session => session.ParentSessionId)
            .HasColumnName("parent_session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(session => session.ReplacedBySessionId)
            .HasColumnName("replaced_by_session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(session => session.RefreshTokenHash)
            .HasColumnName("refresh_token_hash")
            .HasConversion(hash => hash.Value, value => TokenHash.FromHash(value))
            .HasMaxLength(TokenHash.MaximumLength)
            .IsRequired();

        builder.Property(session => session.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<RefreshSessionStatus>(value, true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(session => session.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(session => session.RotatedAt)
            .HasColumnName("rotated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(session => session.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(session => session.RefreshTokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_sessions_refresh_token_hash");

        builder.HasIndex(session => session.ParentSessionId)
            .IsUnique()
            .HasFilter("\"parent_session_id\" IS NOT NULL")
            .HasDatabaseName("ux_refresh_sessions_parent_session_id");

        builder.HasIndex(session => session.FamilyId)
            .HasDatabaseName("ix_refresh_sessions_family_id");

        builder.HasIndex(session => new { session.AccountId, session.ExpiresAt })
            .HasFilter("\"status\" = 'active'")
            .HasDatabaseName("ix_refresh_sessions_active_account_expiry");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(session => session.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_refresh_sessions_accounts_account_id");
    }
}
