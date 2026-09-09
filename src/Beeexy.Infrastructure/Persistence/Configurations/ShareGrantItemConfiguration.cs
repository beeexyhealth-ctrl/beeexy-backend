using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ShareGrantItemConfiguration : IEntityTypeConfiguration<ShareGrantItem>
{
    public void Configure(EntityTypeBuilder<ShareGrantItem> builder)
    {
        builder.ToTable(
            "share_grant_items",
            "sharing",
            table => table.HasCheckConstraint(
                "ck_share_grant_items_resource_type",
                "resource_type ~ '^[a-z][a-z0-9_]*$'"));

        builder.HasKey(item => item.Id)
            .HasName("pk_share_grant_items");

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(item => item.ShareGrantId)
            .HasColumnName("share_grant_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(item => item.ResourceType)
            .HasColumnName("resource_type")
            .HasConversion(type => type.Value, value => ShareResourceType.Create(value))
            .HasMaxLength(SharingPersistenceLimits.ResourceType)
            .IsRequired();

        builder.Property(item => item.ResourceId)
            .HasColumnName("resource_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(item => new
        {
            item.ShareGrantId,
            item.ResourceType,
            item.ResourceId
        })
            .IsUnique()
            .HasDatabaseName("ux_share_grant_items_grant_resource");

        builder.HasOne<ShareGrant>()
            .WithMany()
            .HasForeignKey(item => item.ShareGrantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_share_grant_items_share_grant");
    }
}
