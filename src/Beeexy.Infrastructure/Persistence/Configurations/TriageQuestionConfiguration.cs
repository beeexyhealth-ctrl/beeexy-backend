using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class TriageQuestionConfiguration : IEntityTypeConfiguration<TriageQuestion>
{
    public void Configure(EntityTypeBuilder<TriageQuestion> builder)
    {
        builder.ToTable(
            "questions",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_questions_code",
                    "length(btrim(code)) > 0");
                table.HasCheckConstraint(
                    "ck_questions_prompt_text",
                    "length(btrim(prompt_text)) > 0");
                table.HasCheckConstraint(
                    "ck_questions_display_order",
                    "display_order > 0");
            });

        builder.HasKey(question => question.Id)
            .HasName("pk_questions");

        builder.HasAlternateKey(question => new
        {
            question.Id,
            question.QuestionnaireVersionId
        })
            .HasName("ak_questions_id_questionnaire_version_id");

        builder.Property(question => question.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(question => question.QuestionnaireVersionId)
            .HasColumnName("questionnaire_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(question => question.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => QuestionCode.Create(value))
            .HasMaxLength(QuestionCode.MaximumLength)
            .IsRequired();

        builder.Property(question => question.PromptText)
            .HasColumnName("prompt_text")
            .HasMaxLength(TriageQuestion.MaximumPromptLength)
            .IsRequired();

        builder.Property(question => question.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(question => question.AnswerSchemaJson)
            .HasColumnName("answer_schema")
            .HasColumnType("jsonb");

        builder.Property(question => question.BranchingMetadataJson)
            .HasColumnName("branching_metadata")
            .HasColumnType("jsonb");

        builder.Property(question => question.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(question => new
        {
            question.QuestionnaireVersionId,
            question.Code
        })
            .IsUnique()
            .HasDatabaseName("ux_questions_questionnaire_version_code");

        builder.HasIndex(question => new
        {
            question.QuestionnaireVersionId,
            question.DisplayOrder
        })
            .IsUnique()
            .HasDatabaseName("ux_questions_questionnaire_version_order");
    }
}
