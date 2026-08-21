using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class TriageAnswerConfiguration : IEntityTypeConfiguration<TriageAnswer>
{
    public void Configure(EntityTypeBuilder<TriageAnswer> builder)
    {
        builder.ToTable(
            "answers",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_answers_owner",
                    "(session_id IS NOT NULL AND episode_id IS NULL) OR " +
                    "(session_id IS NULL AND episode_id IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_answers_sequence",
                    "sequence > 0");
            });

        builder.HasKey(answer => answer.Id)
            .HasName("pk_answers");

        builder.Property(answer => answer.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(answer => answer.SessionId)
            .HasColumnName("session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(answer => answer.EpisodeId)
            .HasColumnName("episode_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? EntityId.From(value.Value) : (EntityId?)null);

        builder.Property(answer => answer.QuestionnaireVersionId)
            .HasColumnName("questionnaire_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(answer => answer.QuestionId)
            .HasColumnName("question_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(answer => answer.AnswerJson)
            .HasColumnName("answer")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(answer => answer.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        builder.Property(answer => answer.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(answer => new { answer.SessionId, answer.Sequence })
            .IsUnique()
            .HasFilter("session_id IS NOT NULL")
            .HasDatabaseName("ux_answers_session_sequence");

        builder.HasIndex(answer => new { answer.EpisodeId, answer.Sequence })
            .IsUnique()
            .HasFilter("episode_id IS NOT NULL")
            .HasDatabaseName("ux_answers_episode_sequence");

        builder.HasIndex(answer => new
        {
            answer.SessionId,
            answer.QuestionnaireVersionId
        })
            .HasDatabaseName("ix_answers_session_questionnaire_version");

        builder.HasIndex(answer => new
        {
            answer.EpisodeId,
            answer.QuestionnaireVersionId
        })
            .HasDatabaseName("ix_answers_episode_questionnaire_version");

        builder.HasIndex(answer => new
        {
            answer.QuestionId,
            answer.QuestionnaireVersionId
        })
            .HasDatabaseName("ix_answers_question_questionnaire_version");

        builder.HasOne<TriageQuestion>()
            .WithMany()
            .HasForeignKey(answer => new
            {
                answer.QuestionId,
                answer.QuestionnaireVersionId
            })
            .HasPrincipalKey(question => new
            {
                question.Id,
                question.QuestionnaireVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_answers_questions_question_version");

    }
}
