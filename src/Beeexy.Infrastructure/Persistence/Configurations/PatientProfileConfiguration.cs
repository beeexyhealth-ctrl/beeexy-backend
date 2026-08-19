using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PatientProfileConfiguration : IEntityTypeConfiguration<PatientProfile>
{
    public void Configure(EntityTypeBuilder<PatientProfile> builder)
    {
        builder.ToTable("patient_profiles", "patients");

        builder.HasKey(profile => profile.Id)
            .HasName("pk_patient_profiles");

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(profile => profile.AccountId)
            .HasColumnName("account_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(profile => profile.BeeexyId)
            .HasColumnName("beeexy_id")
            .HasConversion(id => id.Value, value => BeeexyId.Create(value))
            .HasMaxLength(BeeexyId.MaximumLength)
            .IsRequired();

        builder.Property(profile => profile.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(profile => profile.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(profile => profile.AccountId)
            .IsUnique()
            .HasDatabaseName("ux_patient_profiles_account_id");

        builder.HasIndex(profile => profile.BeeexyId)
            .IsUnique()
            .HasDatabaseName("ux_patient_profiles_beeexy_id");

        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<PatientProfile>(profile => profile.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_patient_profiles_accounts_account_id");
    }
}
