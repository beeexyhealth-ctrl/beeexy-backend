using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class ClinicalRuleSetVersionConfiguration
    : IEntityTypeConfiguration<ClinicalRuleSetVersion>
{
    public void Configure(EntityTypeBuilder<ClinicalRuleSetVersion> builder)
    {
        builder.ToTable(
            "clinical_rule_set_versions",
            "triage",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_clinical_rule_set_versions_code",
                    "length(btrim(rule_set_code)) > 0");
                table.HasCheckConstraint(
                    "ck_clinical_rule_set_versions_version",
                    "length(btrim(version)) > 0");
                table.HasCheckConstraint(
                    "ck_clinical_rule_set_versions_content_hash",
                    $"length(content_hash) >= {DefinitionHash.MinimumLength}");
                table.HasCheckConstraint(
                    "ck_clinical_rule_set_versions_activation",
                    "activated_at IS NULL OR " +
                    "(activated_at >= imported_at AND activated_at >= approved_at)");
            });

        builder.HasKey(version => version.Id)
            .HasName("pk_clinical_rule_set_versions");

        builder.Property(version => version.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(version => version.RuleSetCode)
            .HasColumnName("rule_set_code")
            .HasConversion(code => code.Value, value => RuleSetCode.Create(value))
            .HasMaxLength(RuleSetCode.MaximumLength)
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

        builder.HasIndex(version => new { version.RuleSetCode, version.Version })
            .IsUnique()
            .HasDatabaseName("ux_clinical_rule_set_versions_code_version");

        builder.HasIndex(version => new { version.RuleSetCode, version.ActivatedAt })
            .HasDatabaseName("ix_clinical_rule_set_versions_code_activation");
    }
}
