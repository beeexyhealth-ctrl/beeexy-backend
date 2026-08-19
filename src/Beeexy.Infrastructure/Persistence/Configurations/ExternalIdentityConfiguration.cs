using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ExternalIdentityConfiguration
    : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities", "identity");

        builder.HasKey(identity => identity.Id)
            .HasName("pk_external_identities");

        builder.Property(identity => identity.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(identity => identity.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(identity => identity.Provider)
            .HasColumnName("provider")
            .HasMaxLength(ExternalIdentity.ProviderMaximumLength)
            .IsRequired();

        builder.Property(identity => identity.Subject)
            .HasColumnName("subject")
            .HasMaxLength(ExternalIdentity.SubjectMaximumLength)
            .IsRequired();

        builder.Property(identity => identity.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(identity => identity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(identity => new { identity.Provider, identity.Subject })
            .IsUnique()
            .HasDatabaseName("ux_external_identities_provider_subject");

        builder.HasIndex(identity => identity.AccountId)
            .HasDatabaseName("ix_external_identities_account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(identity => identity.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_external_identities_accounts_account_id");
    }
}
