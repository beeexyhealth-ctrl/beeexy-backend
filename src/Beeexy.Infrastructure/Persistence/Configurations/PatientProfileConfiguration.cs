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
        builder.ToTable(
            "patient_profiles",
            "patients",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_patient_profiles_first_name",
                    "first_name IS NULL OR length(btrim(first_name)) > 0");
                table.HasCheckConstraint(
                    "ck_patient_profiles_last_name",
                    "last_name IS NULL OR length(btrim(last_name)) > 0");
                table.HasCheckConstraint(
                    "ck_patient_profiles_sex_assigned_at_birth",
                    "sex_assigned_at_birth IS NULL OR sex_assigned_at_birth IN ('male', 'female')");
                table.HasCheckConstraint(
                    "ck_patient_profiles_state",
                    "state IS NULL OR state IN (" +
                    "'AL','AK','AZ','AR','CA','CO','CT','DE','FL','GA'," +
                    "'HI','ID','IL','IN','IA','KS','KY','LA','ME','MD'," +
                    "'MA','MI','MN','MS','MO','MT','NE','NV','NH','NJ'," +
                    "'NM','NY','NC','ND','OH','OK','OR','PA','RI','SC'," +
                    "'SD','TN','TX','UT','VT','VA','WA','WV','WI','WY')");
                table.HasCheckConstraint(
                    "ck_patient_profiles_version",
                    "version > 0");
            });

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

        builder.Property(profile => profile.FirstName)
            .HasColumnName("first_name")
            .HasConversion(
                name => name!.Value,
                value => PatientName.Create(value))
            .HasMaxLength(PatientName.MaximumLength);

        builder.Property(profile => profile.LastName)
            .HasColumnName("last_name")
            .HasConversion(
                name => name!.Value,
                value => PatientName.Create(value))
            .HasMaxLength(PatientName.MaximumLength);

        builder.Property(profile => profile.DateOfBirth)
            .HasColumnName("date_of_birth")
            .HasColumnType("date");

        builder.Property(profile => profile.SexAssignedAtBirth)
            .HasColumnName("sex_assigned_at_birth")
            .HasConversion(
                value => value.HasValue
                    ? value.Value.ToString().ToLowerInvariant()
                    : null,
                value => value == null
                    ? null
                    : Enum.Parse<SexAssignedAtBirth>(value, true))
            .HasMaxLength(6);

        builder.Property(profile => profile.State)
            .HasColumnName("state")
            .HasConversion(
                state => state!.Code,
                value => UsState.Create(value))
            .HasMaxLength(UsState.CodeLength);

        builder.Property(profile => profile.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
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
