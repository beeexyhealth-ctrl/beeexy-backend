using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiAnalysisRequestConfiguration
    : IEntityTypeConfiguration<AiAnalysisRequest>
{
    public void Configure(EntityTypeBuilder<AiAnalysisRequest> builder)
    {
        builder.ToTable(
            "ai_analysis_requests",
            "ai",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ai_analysis_requests_purpose",
                    "purpose IN ('conversation', 'second_opinion')");
                table.HasCheckConstraint(
                    "ck_ai_analysis_requests_input_schema",
                    "length(btrim(original_input_schema_version)) > 0");
                table.HasCheckConstraint(
                    "ck_ai_analysis_requests_input_snapshot",
                    "jsonb_typeof(original_input_snapshot) = 'object'");
            });

        builder.HasKey(request => request.Id).HasName("pk_ai_analysis_requests");
        builder.Property(request => request.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(request => request.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(request => request.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);
        builder.Property(request => request.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);
        builder.Property(request => request.Purpose)
            .HasColumnName("purpose")
            .HasConversion(
                purpose => AiPersistence.StoreAnalysisPurpose(purpose),
                value => AiPersistence.LoadAnalysisPurpose(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(request => request.OriginalInputSchemaVersion)
            .HasColumnName("original_input_schema_version")
            .HasMaxLength(AiPersistenceLimits.SchemaVersion)
            .IsRequired();
        builder.Property(request => request.OriginalInputSnapshotJson)
            .HasColumnName("original_input_snapshot")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(request => request.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(request => new
        {
            request.AccountId,
            request.CreatedAt,
            request.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_ai_analysis_requests_account_created_id");
        builder.HasIndex(request => new
        {
            request.PatientProfileId,
            request.CreatedAt,
            request.Id
        })
            .HasFilter("patient_profile_id IS NOT NULL")
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_ai_analysis_requests_patient_created_id");
        builder.HasIndex(request => request.ConversationId)
            .HasFilter("conversation_id IS NOT NULL")
            .HasDatabaseName("ix_ai_analysis_requests_conversation");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(request => request.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_analysis_requests_account");
        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(request => request.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_analysis_requests_patient_profile");
        builder.HasOne<AiConversation>()
            .WithMany()
            .HasForeignKey(request => request.ConversationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_analysis_requests_conversation");
    }
}
