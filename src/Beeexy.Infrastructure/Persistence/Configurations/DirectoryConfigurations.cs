using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal static class DirectoryConfiguration
{
    public const string Schema = "directory";

    public static PropertyBuilder<EntityId> ConfigureId<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, EntityId>> property)
        where TEntity : class
    {
        return builder.Property(property)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
    }

    public static PropertyBuilder<DirectoryCode> ConfigureCode<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, DirectoryCode>> property,
        string columnName)
        where TEntity : class
    {
        return builder.Property(property)
            .HasColumnName(columnName)
            .HasConversion(value => value.Value, value => DirectoryCode.Create(value))
            .HasMaxLength(DirectoryCode.MaximumLength)
            .IsRequired();
    }

    public static PropertyBuilder<DirectoryName> ConfigureName<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, DirectoryName>> property,
        string columnName)
        where TEntity : class
    {
        return builder.Property(property)
            .HasColumnName(columnName)
            .HasConversion(value => value.Value, value => DirectoryName.Create(value))
            .HasMaxLength(DirectoryName.MaximumLength)
            .IsRequired();
    }

    public static PropertyBuilder<EntityId> ConfigureForeignKey<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, EntityId>> property,
        string columnName)
        where TEntity : class
    {
        return builder.Property(property)
            .HasColumnName(columnName)
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
    }

    public static void ConfigureCreatedAt<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset>> property)
        where TEntity : class
    {
        builder.Property(property)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}

internal sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.ToTable("clinics", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_clinics_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_clinics_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_clinics");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureCode(builder, entity => entity.Code, "code");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.Name, "name");
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").IsRequired();
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("ux_clinics_code");
        builder.HasIndex(entity => entity.IsPublished).HasDatabaseName("ix_clinics_published");
    }
}

internal sealed class ClinicLocationConfiguration : IEntityTypeConfiguration<ClinicLocation>
{
    public void Configure(EntityTypeBuilder<ClinicLocation> builder)
    {
        builder.ToTable("clinic_locations", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_clinic_locations_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_clinic_locations_locality", "length(btrim(locality)) > 0");
            table.HasCheckConstraint("ck_clinic_locations_area", "length(btrim(administrative_area)) > 0");
            table.HasCheckConstraint("ck_clinic_locations_country", "length(btrim(country)) > 0");
            table.HasCheckConstraint("ck_clinic_locations_timezone", "length(btrim(timezone)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_clinic_locations");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.ClinicId, "clinic_id");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.Name, "name");
        builder.Property(entity => entity.Locality)
            .HasColumnName("locality")
            .HasMaxLength(ClinicLocation.MaximumLocationPartLength)
            .IsRequired();
        builder.Property(entity => entity.AdministrativeArea)
            .HasColumnName("administrative_area")
            .HasMaxLength(ClinicLocation.MaximumLocationPartLength)
            .IsRequired();
        builder.Property(entity => entity.Country)
            .HasColumnName("country")
            .HasMaxLength(ClinicLocation.MaximumLocationPartLength)
            .IsRequired();
        builder.Property(entity => entity.TimeZone)
            .HasColumnName("timezone")
            .HasConversion(value => value.Value, value => IanaTimeZone.Create(value))
            .HasMaxLength(IanaTimeZone.MaximumLength)
            .IsRequired();
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").IsRequired();
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.HasAlternateKey(entity => new { entity.ClinicId, entity.Id })
            .HasName("ak_clinic_locations_clinic_id_id");
        builder.HasIndex(entity => new { entity.ClinicId, entity.IsPublished })
            .HasDatabaseName("ix_clinic_locations_clinic_published");
        builder.HasIndex(entity => new
        {
            entity.Country,
            entity.AdministrativeArea,
            entity.Locality,
            entity.IsPublished
        }).HasDatabaseName("ix_clinic_locations_area_published");
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(entity => entity.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinic_locations_clinics_clinic_id");
    }
}

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_doctors_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_doctors_display_name", "length(btrim(display_name)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_doctors");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureCode(builder, entity => entity.Code, "code");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.DisplayName, "display_name");
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").IsRequired();
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("ux_doctors_code");
        builder.HasIndex(entity => entity.IsPublished).HasDatabaseName("ix_doctors_published");
    }
}

internal sealed class DoctorAffiliationConfiguration : IEntityTypeConfiguration<DoctorAffiliation>
{
    public void Configure(EntityTypeBuilder<DoctorAffiliation> builder)
    {
        builder.ToTable("doctor_affiliations", DirectoryConfiguration.Schema);
        builder.HasKey(entity => entity.Id).HasName("pk_doctor_affiliations");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.DoctorId, "doctor_id");
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.ClinicId, "clinic_id");
        builder.Property(entity => entity.ClinicLocationId)
            .HasColumnName("clinic_location_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").IsRequired();
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => new { entity.DoctorId, entity.IsPublished })
            .HasDatabaseName("ix_doctor_affiliations_doctor_published");
        builder.HasIndex(entity => new { entity.ClinicId, entity.IsPublished })
            .HasDatabaseName("ix_doctor_affiliations_clinic_published");
        builder.HasIndex(entity => new { entity.ClinicId, entity.ClinicLocationId })
            .HasDatabaseName("ix_doctor_affiliations_clinic_location");
        builder.HasIndex(entity => new { entity.DoctorId, entity.ClinicId })
            .IsUnique()
            .HasFilter("clinic_location_id IS NULL")
            .HasDatabaseName("ux_doctor_affiliations_clinic_only");
        builder.HasIndex(entity => new
        {
            entity.DoctorId,
            entity.ClinicId,
            entity.ClinicLocationId
        })
            .IsUnique()
            .HasFilter("clinic_location_id IS NOT NULL")
            .HasDatabaseName("ux_doctor_affiliations_location");
        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(entity => entity.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_affiliations_doctors_doctor_id");
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(entity => entity.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_affiliations_clinics_clinic_id");
        builder.HasOne<ClinicLocation>()
            .WithMany()
            .HasForeignKey(entity => new { entity.ClinicId, entity.ClinicLocationId })
            .HasPrincipalKey(entity => new { entity.ClinicId, entity.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_affiliations_clinic_locations");
    }
}

internal sealed class DoctorCredentialConfiguration : IEntityTypeConfiguration<DoctorCredential>
{
    public void Configure(EntityTypeBuilder<DoctorCredential> builder)
    {
        builder.ToTable("doctor_credentials", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_doctor_credentials_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint(
                "ck_doctor_credentials_status",
                "status IN ('submitted','pending_verification','verified','rejected')");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_doctor_credentials");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.DoctorId, "doctor_id");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.Name, "name");
        builder.Property(entity => entity.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status == DoctorCredentialStatus.PendingVerification
                    ? "pending_verification"
                    : status.ToString().ToLowerInvariant(),
                value => value == "pending_verification"
                    ? DoctorCredentialStatus.PendingVerification
                    : Enum.Parse<DoctorCredentialStatus>(value, true))
            .HasMaxLength(24)
            .IsRequired();
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => new { entity.DoctorId, entity.Status })
            .HasDatabaseName("ix_doctor_credentials_doctor_status");
        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(entity => entity.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_credentials_doctors_doctor_id");
    }
}

internal sealed class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        ConfigureCatalog(builder, "specialties", "pk_specialties", "ux_specialties_code");
    }

    private static void ConfigureCatalog(
        EntityTypeBuilder<Specialty> builder,
        string tableName,
        string keyName,
        string indexName)
    {
        builder.ToTable(tableName, DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_specialties_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_specialties_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName(keyName);
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureCode(builder, entity => entity.Code, "code");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.Name, "name");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName(indexName);
    }
}

internal sealed class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable("languages", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_languages_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_languages_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_languages");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureCode(builder, entity => entity.Code, "code");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.Name, "name");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("ux_languages_code");
    }
}

internal sealed class InsurancePlanConfiguration : IEntityTypeConfiguration<InsurancePlan>
{
    public void Configure(EntityTypeBuilder<InsurancePlan> builder)
    {
        builder.ToTable("insurance_plans", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_insurance_plans_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_insurance_plans_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_insurance_plans");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureCode(builder, entity => entity.Code, "code");
        DirectoryConfiguration.ConfigureName(builder, entity => entity.Name, "name");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("ux_insurance_plans_code");
    }
}

internal sealed class DoctorSpecialtyConfiguration : IEntityTypeConfiguration<DoctorSpecialty>
{
    public void Configure(EntityTypeBuilder<DoctorSpecialty> builder)
    {
        builder.ToTable("doctor_specialties", DirectoryConfiguration.Schema);
        builder.HasKey(entity => entity.Id).HasName("pk_doctor_specialties");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.DoctorId, "doctor_id");
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.SpecialtyId, "specialty_id");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => new { entity.DoctorId, entity.SpecialtyId })
            .IsUnique()
            .HasDatabaseName("ux_doctor_specialties_doctor_specialty");
        builder.HasIndex(entity => entity.SpecialtyId)
            .HasDatabaseName("ix_doctor_specialties_specialty_id");
        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(entity => entity.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_specialties_doctors_doctor_id");
        builder.HasOne<Specialty>()
            .WithMany()
            .HasForeignKey(entity => entity.SpecialtyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_specialties_specialties_id");
    }
}

internal sealed class DoctorLanguageConfiguration : IEntityTypeConfiguration<DoctorLanguage>
{
    public void Configure(EntityTypeBuilder<DoctorLanguage> builder)
    {
        builder.ToTable("doctor_languages", DirectoryConfiguration.Schema);
        builder.HasKey(entity => entity.Id).HasName("pk_doctor_languages");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.DoctorId, "doctor_id");
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.LanguageId, "language_id");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => new { entity.DoctorId, entity.LanguageId })
            .IsUnique()
            .HasDatabaseName("ux_doctor_languages_doctor_language");
        builder.HasIndex(entity => entity.LanguageId)
            .HasDatabaseName("ix_doctor_languages_language_id");
        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(entity => entity.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_languages_doctors_doctor_id");
        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(entity => entity.LanguageId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_languages_languages_id");
    }
}

internal sealed class DoctorInsuranceParticipationConfiguration
    : IEntityTypeConfiguration<DoctorInsuranceParticipation>
{
    public void Configure(EntityTypeBuilder<DoctorInsuranceParticipation> builder)
    {
        builder.ToTable("doctor_insurance_participations", DirectoryConfiguration.Schema);
        builder.HasKey(entity => entity.Id).HasName("pk_doctor_insurance_participations");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureForeignKey(builder, entity => entity.DoctorId, "doctor_id");
        DirectoryConfiguration.ConfigureForeignKey(
            builder,
            entity => entity.InsurancePlanId,
            "insurance_plan_id");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => new { entity.DoctorId, entity.InsurancePlanId })
            .IsUnique()
            .HasDatabaseName("ux_doctor_insurance_doctor_plan");
        builder.HasIndex(entity => entity.InsurancePlanId)
            .HasDatabaseName("ix_doctor_insurance_plan_id");
        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(entity => entity.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_insurance_doctors_doctor_id");
        builder.HasOne<InsurancePlan>()
            .WithMany()
            .HasForeignKey(entity => entity.InsurancePlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_doctor_insurance_plans_plan_id");
    }
}

internal sealed class DoctorMatchRuleVersionConfiguration
    : IEntityTypeConfiguration<DoctorMatchRuleVersion>
{
    public void Configure(EntityTypeBuilder<DoctorMatchRuleVersion> builder)
    {
        builder.ToTable("doctor_match_rule_versions", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_doctor_match_rule_versions_version",
                "length(btrim(version)) > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_doctor_match_rule_versions");
        DirectoryConfiguration.ConfigureId(builder, entity => entity.Id);
        DirectoryConfiguration.ConfigureCode(builder, entity => entity.Version, "version");
        DirectoryConfiguration.ConfigureCreatedAt(builder, entity => entity.CreatedAt);
        builder.HasIndex(entity => entity.Version)
            .IsUnique()
            .HasDatabaseName("ux_doctor_match_rule_versions_version");
    }
}
