using Beeexy.Infrastructure.Scheduling;
using Beeexy.Domain.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AvailabilityImportRecordConfiguration
    : IEntityTypeConfiguration<AvailabilityImportRecord>
{
    public void Configure(EntityTypeBuilder<AvailabilityImportRecord> builder)
    {
        builder.ToTable("demo_availability_imports", SchedulingConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_demo_availability_imports_package_code",
                "length(btrim(package_code)) > 0");
            table.HasCheckConstraint(
                "ck_demo_availability_imports_version",
                "length(btrim(version)) > 0");
            table.HasCheckConstraint(
                "ck_demo_availability_imports_content_hash",
                "content_hash ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(value => value.Id).HasName("pk_demo_availability_imports");
        SchedulingConfiguration.ConfigureId(builder, value => value.Id);
        builder.Property(value => value.PackageCode)
            .HasColumnName("package_code")
            .HasConversion(value => value.Value, value => DirectoryCode.Create(value))
            .HasMaxLength(DirectoryCode.MaximumLength)
            .IsRequired();
        builder.Property(value => value.Version)
            .HasColumnName("version")
            .HasConversion(value => value.Value, value => DirectoryCode.Create(value))
            .HasMaxLength(DirectoryCode.MaximumLength)
            .IsRequired();
        builder.Property(value => value.ReferenceDate)
            .HasColumnName("reference_date")
            .HasColumnType("date")
            .IsRequired();
        builder.Property(value => value.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        SchedulingConfiguration.ConfigureUtc(builder, value => value.ImportedAt, "imported_at");
        builder.HasIndex(value => new
        {
            value.PackageCode,
            value.Version,
            value.ReferenceDate
        })
            .IsUnique()
            .HasDatabaseName("ux_demo_availability_imports_package_version_reference_date");
    }
}
