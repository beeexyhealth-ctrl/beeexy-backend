using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class PreTriageIntakeIdempotencyRecordConfiguration
    : IEntityTypeConfiguration<PreTriageIntakeIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<PreTriageIntakeIdempotencyRecord> builder)
    {
        builder.ToTable(
            "pre_triage_intake_idempotency",
            "triage",
            table => table.HasCheckConstraint(
                "ck_pre_triage_intake_idempotency_timestamps",
                "completed_at >= created_at"));

        builder.HasKey(record => record.Id)
            .HasName("pk_pre_triage_intake_idempotency");

        builder.Property(record => record.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(record => record.OperationKeyHash)
            .HasColumnName("operation_key_hash")
            .HasMaxLength(PreTriageIntakeIdempotencyRecord.HashMaximumLength)
            .IsRequired();

        builder.Property(record => record.ReservationAliasHash)
            .HasColumnName("reservation_alias_hash")
            .HasMaxLength(PreTriageIntakeIdempotencyRecord.HashMaximumLength);

        builder.Property(record => record.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasMaxLength(PreTriageIntakeIdempotencyRecord.HashMaximumLength)
            .IsRequired();

        builder.Property(record => record.SessionId)
            .HasColumnName("session_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        var answerCodeComparer = new ValueComparer<string[]>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToArray());
        builder.Property(record => record.InitialAnswerCodes)
            .HasColumnName("initial_answer_codes")
            .HasColumnType("text[]")
            .IsRequired()
            .Metadata.SetValueComparer(answerCodeComparer);

        builder.Property(record => record.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(record => record.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(record => record.OperationKeyHash)
            .IsUnique()
            .HasDatabaseName("ux_pre_triage_intake_idempotency_operation_key_hash");

        builder.HasIndex(record => record.ReservationAliasHash)
            .IsUnique()
            .HasFilter("reservation_alias_hash IS NOT NULL")
            .HasDatabaseName("ux_pre_triage_intake_idempotency_reservation_alias_hash");

        builder.HasIndex(record => record.SessionId)
            .IsUnique()
            .HasDatabaseName("ux_pre_triage_intake_idempotency_session_id");

        builder.HasOne<PreTriageSession>()
            .WithMany()
            .HasForeignKey(record => record.SessionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(
                "fk_pre_triage_intake_idempotency_pre_triage_sessions_session_id");
    }
}
