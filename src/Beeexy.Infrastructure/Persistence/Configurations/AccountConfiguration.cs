using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable(
            "accounts",
            "identity",
            table => table.HasCheckConstraint(
                "ck_accounts_status",
                "\"status\" IN ('active', 'disabled')"));

        builder.HasKey(account => account.Id)
            .HasName("pk_accounts");

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(account => account.Email)
            .HasColumnName("normalized_email")
            .HasConversion(email => email.Value, value => NormalizedEmail.Create(value))
            .HasMaxLength(NormalizedEmail.MaximumLength)
            .IsRequired();

        builder.Property(account => account.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<AccountStatus>(value, true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(account => account.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(account => account.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(account => account.Email)
            .IsUnique()
            .HasDatabaseName("ux_accounts_normalized_email");
    }
}
