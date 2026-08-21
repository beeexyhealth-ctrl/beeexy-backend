using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class QuestionnaireDefinitionVersionConfiguration
    : IEntityTypeConfiguration<QuestionnaireDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<QuestionnaireDefinitionVersion> builder)
    {
        builder.ToTable(
            "questionnaire_versions",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_questionnaire_versions_code",
                    "length(btrim(questionnaire_code)) > 0");
                table.HasCheckConstraint(
                    "ck_questionnaire_versions_version",
                    "length(btrim(version)) > 0");
                table.HasCheckConstraint(
                    "ck_questionnaire_versions_content_hash",
                    $"length(content_hash) >= {DefinitionHash.MinimumLength}");
                table.HasCheckConstraint(
                    "ck_questionnaire_versions_activation",
                    "activated_at IS NULL OR " +
                    "(activated_at >= imported_at AND activated_at >= approved_at)");
            });

        builder.HasKey(version => version.Id)
            .HasName("pk_questionnaire_versions");

        builder.Property(version => version.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(version => version.QuestionnaireCode)
            .HasColumnName("questionnaire_code")
            .HasConversion(code => code.Value, value => QuestionnaireCode.Create(value))
            .HasMaxLength(QuestionnaireCode.MaximumLength)
            .IsRequired();

        builder.Property(version => version.Version)
            .HasColumnName("version")
            .HasConversion(value => value.Value, value => DefinitionVersion.Create(value))
            .HasMaxLength(DefinitionVersion.MaximumLength)
            .IsRequired();

        builder.Property(version => version.ContentHash)
            .HasColumnName("content_hash")
            .HasConversion(hash => hash.Value, value => DefinitionHash.FromHash(value))
            .HasMaxLength(DefinitionHash.MaximumLength)
            .IsRequired();

        builder.Property(version => version.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(TriagePersistenceLimits.MaximumReferenceLength);

        builder.Property(version => version.ImportedAt)
            .HasColumnName("imported_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(version => version.ApprovedAt)
            .HasColumnName("approved_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(version => version.ActivatedAt)
            .HasColumnName("activated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(version => new { version.QuestionnaireCode, version.Version })
            .IsUnique()
            .HasDatabaseName("ux_questionnaire_versions_code_version");

        builder.HasIndex(version => new { version.QuestionnaireCode, version.ActivatedAt })
            .HasDatabaseName("ix_questionnaire_versions_code_activation");

        builder.HasMany(version => version.Questions)
            .WithOne()
            .HasForeignKey(question => question.QuestionnaireVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_questions_questionnaire_versions_questionnaire_version_id");
        builder.Navigation(version => version.Questions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
