using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PrivateAccessSessionConfiguration
    : IEntityTypeConfiguration<PrivateAccessSession>
{
    public void Configure(EntityTypeBuilder<PrivateAccessSession> builder)
    {
        builder.ToTable("private_access_sessions", "identity", table =>
        {
            table.HasCheckConstraint("ck_private_access_sessions_status", "status IN ('active','revoked','expired')");
            table.HasCheckConstraint("ck_private_access_sessions_expiry", "expires_at > created_at");
            table.HasCheckConstraint("ck_private_access_sessions_revoked", "(status = 'revoked' AND revoked_at IS NOT NULL) OR (status <> 'revoked' AND revoked_at IS NULL)");
        });
        builder.HasKey(value => value.Id).HasName("pk_private_access_sessions");
        builder.Property(value => value.Id).HasColumnName("id").HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
        builder.Property(value => value.CredentialId).HasColumnName("credential_id").HasConversion(id => id.Value, value => EntityId.From(value)).IsRequired();
        builder.Property(value => value.RootRefreshSessionId).HasColumnName("root_refresh_session_id").HasConversion(id => id.Value, value => EntityId.From(value)).IsRequired();
        builder.Property(value => value.TokenHash).HasColumnName("token_hash").HasConversion(value => value.Value, value => TokenHash.FromHash(value)).HasMaxLength(TokenHash.MaximumLength).IsRequired();
        builder.Property(value => value.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.Status).HasColumnName("status").HasConversion(value => value.ToString().ToLowerInvariant(), value => Enum.Parse<PrivateAccessSessionStatus>(value, true)).HasMaxLength(16).IsRequired();
        builder.Property(value => value.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.TokenHash).IsUnique().HasDatabaseName("ux_private_access_sessions_token_hash");
        builder.HasIndex(value => new { value.CredentialId, value.ExpiresAt }).HasFilter("status = 'active'").HasDatabaseName("ix_private_access_sessions_active_credential_expiry");
        builder.HasIndex(value => value.RootRefreshSessionId).IsUnique().HasDatabaseName("ux_private_access_sessions_root_refresh_session_id");
        builder.HasOne<PrivateAccessCredential>().WithMany().HasForeignKey(value => value.CredentialId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_private_access_sessions_credentials_credential_id");
        builder.HasOne<RefreshSession>().WithMany().HasForeignKey(value => value.RootRefreshSessionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_private_access_sessions_refresh_sessions_root_id");
    }
}
