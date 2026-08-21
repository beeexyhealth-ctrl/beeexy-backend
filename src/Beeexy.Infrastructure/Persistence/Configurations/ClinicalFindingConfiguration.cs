using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ClinicalFindingConfiguration
    : IEntityTypeConfiguration<ClinicalFinding>
{
    public void Configure(EntityTypeBuilder<ClinicalFinding> builder)
    {
        builder.ToTable(
            "clinical_findings",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_clinical_findings_finding_code",
                    "length(btrim(finding_code)) > 0");
                table.HasCheckConstraint(
                    "ck_clinical_findings_source_rule_code",
                    "length(btrim(source_rule_code)) > 0");
            });

        builder.HasKey(finding => finding.Id)
            .HasName("pk_clinical_findings");

        builder.Property(finding => finding.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(finding => finding.AssessmentId)
            .HasColumnName("assessment_id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .IsRequired();

        builder.Property(finding => finding.FindingCode)
            .HasColumnName("finding_code")
            .HasMaxLength(ClinicalFinding.MaximumCodeLength)
            .IsRequired();

        builder.Property(finding => finding.SourceRuleCode)
            .HasColumnName("source_rule_code")
            .HasMaxLength(ClinicalFinding.MaximumCodeLength)
            .IsRequired();

        builder.Property(finding => finding.MessageReference)
            .HasColumnName("message_reference")
            .HasMaxLength(TriagePersistenceLimits.MaximumReferenceLength);

        builder.Property(finding => finding.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(finding => new { finding.AssessmentId, finding.FindingCode })
            .IsUnique()
            .HasDatabaseName("ux_clinical_findings_assessment_finding_code");
    }
}
