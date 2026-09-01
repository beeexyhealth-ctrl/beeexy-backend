using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ToTable(
            "ai_messages",
            "ai",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ai_messages_role",
                    "role IN ('user', 'assistant')");
                table.HasCheckConstraint(
                    "ck_ai_messages_sequence",
                    "sequence > 0");
                table.HasCheckConstraint(
                    "ck_ai_messages_content",
                    "length(btrim(content)) > 0");
            });

        builder.HasKey(message => message.Id).HasName("pk_ai_messages");
        builder.Property(message => message.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
        builder.Property(message => message.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();
        builder.Property(message => message.Role)
            .HasColumnName("role")
            .HasConversion(
                role => AiPersistence.StoreMessageRole(role),
                value => AiPersistence.LoadMessageRole(value))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(message => message.Content)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(message => message.Sequence)
            .HasColumnName("sequence")
            .IsRequired();
        builder.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(message => new { message.ConversationId, message.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_ai_messages_conversation_sequence");
        builder.HasOne<AiConversation>()
            .WithMany()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ai_messages_conversation");
    }
}
