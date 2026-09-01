using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable(
            "ai_conversations",
            "ai",
            table => table.HasCheckConstraint(
                "ck_ai_conversations_deleted_at",
                "deleted_at IS NULL OR deleted_at >= created_at"));

        builder.HasKey(conversation => conversation.Id)
            .HasName("pk_ai_conversations");

        builder.Property(conversation => conversation.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(conversation => conversation.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(conversation => conversation.PatientProfileId)
            .HasColumnName("patient_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : null);
        builder.Property(conversation => conversation.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(conversation => conversation.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");
        builder.Ignore(conversation => conversation.IsDeleted);

        builder.HasIndex(conversation => new
        {
            conversation.AccountId,
            conversation.CreatedAt,
            conversation.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_ai_conversations_account_created_id");
        builder.HasIndex(conversation => new
        {
            conversation.PatientProfileId,
            conversation.CreatedAt,
            conversation.Id
        })
            .HasFilter("patient_profile_id IS NOT NULL")
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_ai_conversations_patient_created_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(conversation => conversation.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_conversations_account");
        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(conversation => conversation.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_conversations_patient_profile");
    }
}
