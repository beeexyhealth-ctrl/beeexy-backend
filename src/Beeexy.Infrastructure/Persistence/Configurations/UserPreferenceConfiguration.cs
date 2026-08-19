using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("user_preferences", "patients");

        builder.HasKey(preference => preference.Id)
            .HasName("pk_user_preferences");

        builder.Property(preference => preference.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(preference => preference.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(preference => preference.TimeZone)
            .HasColumnName("timezone")
            .HasConversion(timeZone => timeZone.Value, value => UserTimeZone.Create(value))
            .HasMaxLength(UserTimeZone.MaximumLength)
            .IsRequired();

        builder.Property(preference => preference.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(preference => preference.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(preference => preference.AccountId)
            .IsUnique()
            .HasDatabaseName("ux_user_preferences_account_id");

        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<UserPreference>(preference => preference.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_user_preferences_accounts_account_id");
    }
}
