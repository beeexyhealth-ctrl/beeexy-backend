using Beeexy.Infrastructure.DirectoryServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class DirectoryImportRecordConfiguration
    : IEntityTypeConfiguration<DirectoryImportRecord>
{
    public void Configure(EntityTypeBuilder<DirectoryImportRecord> builder)
    {
        builder.ToTable("demo_directory_imports", DirectoryConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_demo_directory_imports_package_code",
                "length(btrim(package_code)) > 0");
            table.HasCheckConstraint(
                "ck_demo_directory_imports_version",
                "length(btrim(version)) > 0");
            table.HasCheckConstraint(
                "ck_demo_directory_imports_content_hash",
                "content_hash ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(value => value.Id).HasName("pk_demo_directory_imports");
        DirectoryConfiguration.ConfigureId(builder, value => value.Id);
        DirectoryConfiguration.ConfigureCode(builder, value => value.PackageCode, "package_code");
        DirectoryConfiguration.ConfigureCode(builder, value => value.Version, "version");
        builder.Property(value => value.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(value => value.ImportedAt)
            .HasColumnName("imported_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.HasIndex(value => new { value.PackageCode, value.Version })
            .IsUnique()
            .HasDatabaseName("ux_demo_directory_imports_package_version");
    }
}
