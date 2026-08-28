using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PrivateAccessCredentialConfiguration
    : IEntityTypeConfiguration<PrivateAccessCredential>
{
    public void Configure(EntityTypeBuilder<PrivateAccessCredential> builder)
    {
        builder.ToTable("private_access_credentials", "identity", table =>
        {
            table.HasCheckConstraint("ck_private_access_credentials_status", "status IN ('active','disabled','revoked')");
            table.HasCheckConstraint("ck_private_access_credentials_timestamps", "(status = 'active' AND disabled_at IS NULL AND revoked_at IS NULL) OR (status = 'disabled' AND disabled_at IS NOT NULL AND revoked_at IS NULL) OR (status = 'revoked' AND disabled_at IS NULL AND revoked_at IS NOT NULL)");
        });
        builder.HasKey(value => value.Id).HasName("pk_private_access_credentials");
        builder.Property(value => value.Id).HasColumnName("id").HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
        builder.Property(value => value.AccountId).HasColumnName("account_id").HasConversion(id => id.Value, value => EntityId.From(value)).IsRequired();
        builder.Property(value => value.TesterKey).HasColumnName("tester_key").HasMaxLength(PrivateAccessCredential.TesterKeyMaximumLength).IsRequired();
        builder.Property(value => value.Username).HasColumnName("username").HasMaxLength(PrivateAccessCredential.UsernameMaximumLength).IsRequired();
        builder.Property(value => value.PasswordHash).HasColumnName("password_hash").HasMaxLength(PrivateAccessCredential.SecretHashMaximumLength).IsRequired();
        builder.Property(value => value.KeywordHash).HasColumnName("keyword_hash").HasMaxLength(PrivateAccessCredential.SecretHashMaximumLength).IsRequired();
        builder.Property(value => value.Status).HasColumnName("status").HasConversion(value => value.ToString().ToLowerInvariant(), value => Enum.Parse<PrivateAccessCredentialStatus>(value, true)).HasMaxLength(16).IsRequired();
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.DisabledAt).HasColumnName("disabled_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
        builder.HasIndex(value => value.AccountId).IsUnique().HasDatabaseName("ux_private_access_credentials_account_id");
        builder.HasIndex(value => value.TesterKey).IsUnique().HasDatabaseName("ux_private_access_credentials_tester_key");
        builder.HasIndex(value => value.Username).IsUnique().HasDatabaseName("ux_private_access_credentials_username");
        builder.HasOne<Account>().WithOne().HasForeignKey<PrivateAccessCredential>(value => value.AccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_private_access_credentials_accounts_account_id");
    }
}
