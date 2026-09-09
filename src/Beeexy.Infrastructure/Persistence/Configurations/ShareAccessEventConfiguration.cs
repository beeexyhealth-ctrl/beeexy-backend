using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ShareAccessEventConfiguration : IEntityTypeConfiguration<ShareAccessEvent>
{
    public void Configure(EntityTypeBuilder<ShareAccessEvent> builder)
    {
        builder.ToTable(
            "share_access_events",
            "sharing",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_share_access_events_type",
                    "event_type IN ('share_created', 'share_accessed', 'share_downloaded', " +
                    "'share_revoked', 'share_expired')");
                table.HasCheckConstraint(
                    "ck_share_access_events_outcome",
                    "outcome IN ('succeeded', 'denied')");
                table.HasCheckConstraint(
                    "ck_share_access_events_resource_type",
                    "resource_type IS NULL OR resource_type ~ '^[a-z][a-z0-9_]*$'");
            });

        builder.HasKey(accessEvent => accessEvent.Id)
            .HasName("pk_share_access_events");

        builder.Property(accessEvent => accessEvent.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(accessEvent => accessEvent.ShareGrantId)
            .HasColumnName("share_grant_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(accessEvent => accessEvent.EventType)
            .HasColumnName("event_type")
            .HasConversion(
                value => SharingPersistence.StoreEventType(value),
                value => SharingPersistence.LoadEventType(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(accessEvent => accessEvent.Outcome)
            .HasColumnName("outcome")
            .HasConversion(
                value => SharingPersistence.StoreEventOutcome(value),
                value => SharingPersistence.LoadEventOutcome(value))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(accessEvent => accessEvent.ResourceType)
            .HasColumnName("resource_type")
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : ShareResourceType.Create(value))
            .HasMaxLength(SharingPersistenceLimits.ResourceType);

        builder.Property(accessEvent => accessEvent.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(accessEvent => new
        {
            accessEvent.ShareGrantId,
            accessEvent.OccurredAt,
            accessEvent.Id
        })
            .HasDatabaseName("ix_share_access_events_grant_occurred_id");

        builder.HasOne<ShareGrant>()
            .WithMany()
            .HasForeignKey(accessEvent => accessEvent.ShareGrantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_share_access_events_share_grant");
    }
}
