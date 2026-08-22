using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase42ClinicalDefinitionPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_activation",
                schema: "triage",
                table: "questionnaire_versions");
            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_activation",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "triage",
                table: "questionnaire_versions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "triage",
                table: "clinical_rule_set_versions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            AddRequiredTextColumn(
                migrationBuilder, "questionnaire_versions", "clinical_approval_status", 64,
                "APPROVED");
            AddRequiredTextColumn(
                migrationBuilder, "questionnaire_versions", "clinical_content_source", 64,
                "LEGACY_UNSPECIFIED");
            AddRequiredTextColumn(
                migrationBuilder, "questionnaire_versions", "clinical_review_status", 64,
                "REVIEWED");
            AddRequiredTextColumn(
                migrationBuilder, "questionnaire_versions", "pathway_code", 100,
                "UNSPECIFIED");
            AddRequiredTextColumn(
                migrationBuilder, "clinical_rule_set_versions", "clinical_approval_status", 64,
                "APPROVED");
            AddRequiredTextColumn(
                migrationBuilder, "clinical_rule_set_versions", "clinical_content_source", 64,
                "LEGACY_UNSPECIFIED");
            AddRequiredTextColumn(
                migrationBuilder, "clinical_rule_set_versions", "clinical_review_status", 64,
                "REVIEWED");
            migrationBuilder.AddColumn<string>(
                name: "definition_metadata",
                schema: "triage",
                table: "clinical_rule_set_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
            AddRequiredTextColumn(
                migrationBuilder, "clinical_rule_set_versions", "pathway_code", 100,
                "UNSPECIFIED");

            migrationBuilder.Sql(
                "ALTER TABLE triage.questionnaire_versions " +
                "ALTER COLUMN clinical_approval_status DROP DEFAULT, " +
                "ALTER COLUMN clinical_content_source DROP DEFAULT, " +
                "ALTER COLUMN clinical_review_status DROP DEFAULT, " +
                "ALTER COLUMN pathway_code DROP DEFAULT;");
            migrationBuilder.Sql(
                "ALTER TABLE triage.clinical_rule_set_versions " +
                "ALTER COLUMN clinical_approval_status DROP DEFAULT, " +
                "ALTER COLUMN clinical_content_source DROP DEFAULT, " +
                "ALTER COLUMN clinical_review_status DROP DEFAULT, " +
                "ALTER COLUMN definition_metadata DROP DEFAULT, " +
                "ALTER COLUMN pathway_code DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_versions_pathway_activation",
                schema: "triage",
                table: "questionnaire_versions",
                columns: new[] { "pathway_code", "activated_at" });
            migrationBuilder.CreateIndex(
                name: "ix_clinical_rule_set_versions_pathway_activation",
                schema: "triage",
                table: "clinical_rule_set_versions",
                columns: new[] { "pathway_code", "activated_at" });

            AddQuestionnaireConstraints(migrationBuilder);
            AddRuleSetConstraints(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_questionnaire_versions_pathway_activation",
                schema: "triage",
                table: "questionnaire_versions");
            migrationBuilder.DropIndex(
                name: "ix_clinical_rule_set_versions_pathway_activation",
                schema: "triage",
                table: "clinical_rule_set_versions");

            DropQuestionnaireConstraints(migrationBuilder);
            DropRuleSetConstraints(migrationBuilder);

            foreach (var column in new[]
            {
                "clinical_approval_status",
                "clinical_content_source",
                "clinical_review_status",
                "pathway_code"
            })
            {
                migrationBuilder.DropColumn(
                    name: column,
                    schema: "triage",
                    table: "questionnaire_versions");
            }

            foreach (var column in new[]
            {
                "clinical_approval_status",
                "clinical_content_source",
                "clinical_review_status",
                "definition_metadata",
                "pathway_code"
            })
            {
                migrationBuilder.DropColumn(
                    name: column,
                    schema: "triage",
                    table: "clinical_rule_set_versions");
            }

            migrationBuilder.Sql(
                "UPDATE triage.questionnaire_versions " +
                "SET approved_at = imported_at WHERE approved_at IS NULL;");
            migrationBuilder.Sql(
                "UPDATE triage.clinical_rule_set_versions " +
                "SET approved_at = imported_at WHERE approved_at IS NULL;");
            MakeApprovalRequired(migrationBuilder, "questionnaire_versions");
            MakeApprovalRequired(migrationBuilder, "clinical_rule_set_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_activation",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "activated_at IS NULL OR " +
                    "(activated_at >= imported_at AND activated_at >= approved_at)");
            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_activation",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "activated_at IS NULL OR " +
                    "(activated_at >= imported_at AND activated_at >= approved_at)");
        }

        private static void AddRequiredTextColumn(
            MigrationBuilder migrationBuilder,
            string table,
            string column,
            int maximumLength,
            string legacyValue)
        {
            migrationBuilder.AddColumn<string>(
                name: column,
                schema: "triage",
                table: table,
                type: $"character varying({maximumLength})",
                maxLength: maximumLength,
                nullable: false,
                defaultValue: legacyValue);
        }

        private static void MakeApprovalRequired(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "triage",
                table: table,
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        private static void AddQuestionnaireConstraints(MigrationBuilder migrationBuilder)
        {
            AddDefinitionConstraints(
                migrationBuilder,
                "questionnaire_versions",
                "questionnaire_versions");
        }

        private static void AddRuleSetConstraints(MigrationBuilder migrationBuilder)
        {
            AddDefinitionConstraints(
                migrationBuilder,
                "clinical_rule_set_versions",
                "clinical_rule_set_versions");
            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_definition_metadata",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "jsonb_typeof(definition_metadata) = 'object'");
        }

        private static void AddDefinitionConstraints(
            MigrationBuilder migrationBuilder,
            string table,
            string constraintPrefix)
        {
            migrationBuilder.AddCheckConstraint(
                name: $"ck_{constraintPrefix}_activation",
                schema: "triage",
                table: table,
                sql: "activated_at IS NULL OR (activated_at >= imported_at AND " +
                    "(approved_at IS NULL OR activated_at >= approved_at))");
            migrationBuilder.AddCheckConstraint(
                name: $"ck_{constraintPrefix}_approval",
                schema: "triage",
                table: table,
                sql: "(clinical_approval_status = 'APPROVED' AND approved_at IS NOT NULL) OR " +
                    "(clinical_approval_status <> 'APPROVED' AND approved_at IS NULL)");
            migrationBuilder.AddCheckConstraint(
                name: $"ck_{constraintPrefix}_approval_status",
                schema: "triage",
                table: table,
                sql: "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW')");
            migrationBuilder.AddCheckConstraint(
                name: $"ck_{constraintPrefix}_content_source",
                schema: "triage",
                table: table,
                sql: "clinical_content_source IN " +
                    "('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED')");
            migrationBuilder.AddCheckConstraint(
                name: $"ck_{constraintPrefix}_pathway",
                schema: "triage",
                table: table,
                sql: "length(btrim(pathway_code)) > 0");
            migrationBuilder.AddCheckConstraint(
                name: $"ck_{constraintPrefix}_review_status",
                schema: "triage",
                table: table,
                sql: "clinical_review_status IN ('REVIEWED', 'PROVISIONAL')");
        }

        private static void DropQuestionnaireConstraints(MigrationBuilder migrationBuilder)
        {
            DropDefinitionConstraints(
                migrationBuilder,
                "questionnaire_versions",
                "questionnaire_versions");
        }

        private static void DropRuleSetConstraints(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_definition_metadata",
                schema: "triage",
                table: "clinical_rule_set_versions");
            DropDefinitionConstraints(
                migrationBuilder,
                "clinical_rule_set_versions",
                "clinical_rule_set_versions");
        }

        private static void DropDefinitionConstraints(
            MigrationBuilder migrationBuilder,
            string table,
            string constraintPrefix)
        {
            foreach (var suffix in new[]
            {
                "activation",
                "approval",
                "approval_status",
                "content_source",
                "pathway",
                "review_status"
            })
            {
                migrationBuilder.DropCheckConstraint(
                    name: $"ck_{constraintPrefix}_{suffix}",
                    schema: "triage",
                    table: table);
            }
        }
    }
}
