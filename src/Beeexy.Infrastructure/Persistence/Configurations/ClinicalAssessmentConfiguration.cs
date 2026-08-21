using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ClinicalAssessmentConfiguration
    : IEntityTypeConfiguration<ClinicalAssessment>
{
    public void Configure(EntityTypeBuilder<ClinicalAssessment> builder)
    {
        builder.ToTable(
            "clinical_assessments",
            "triage",
            table => table.HasCheckConstraint(
                "ck_clinical_assessments_urgency_code",
                "length(btrim(urgency_code)) > 0"));

        builder.HasKey(assessment => assessment.Id)
            .HasName("pk_clinical_assessments");

        builder.Property(assessment => assessment.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(assessment => assessment.EpisodeId)
            .HasColumnName("episode_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(assessment => assessment.ClinicalRuleSetVersionId)
            .HasColumnName("clinical_rule_set_version_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(assessment => assessment.UrgencyCode)
            .HasColumnName("urgency_code")
            .HasConversion(code => code.Value, value => UrgencyCode.Create(value))
            .HasMaxLength(UrgencyCode.MaximumLength)
            .IsRequired();

        builder.Property(assessment => assessment.ResultMessageReference)
            .HasColumnName("result_message_reference")
            .HasMaxLength(TriagePersistenceLimits.MaximumReferenceLength);

        builder.Property(assessment => assessment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(assessment => assessment.EpisodeId)
            .IsUnique()
            .HasDatabaseName("ux_clinical_assessments_episode_id");

        builder.HasIndex(assessment => assessment.ClinicalRuleSetVersionId)
            .HasDatabaseName("ix_clinical_assessments_clinical_rule_set_version_id");

        builder.HasIndex(assessment => new
        {
            assessment.EpisodeId,
            assessment.ClinicalRuleSetVersionId
        })
            .HasDatabaseName("ix_clinical_assessments_episode_rule_set_version");

        builder.HasOne<PreTriageEpisode>()
            .WithMany()
            .HasForeignKey(assessment => new
            {
                assessment.EpisodeId,
                assessment.ClinicalRuleSetVersionId
            })
            .HasPrincipalKey(episode => new
            {
                episode.Id,
                episode.ClinicalRuleSetVersionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_assessments_episodes_episode_rule_set_version");

        builder.HasOne<ClinicalRuleSetVersion>()
            .WithMany()
            .HasForeignKey(assessment => assessment.ClinicalRuleSetVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_clinical_assessments_rule_set_versions_version_id");

        builder.HasMany(assessment => assessment.Findings)
            .WithOne()
            .HasForeignKey(finding => finding.AssessmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clinical_findings_clinical_assessments_assessment_id");
        builder.Navigation(assessment => assessment.Findings)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
