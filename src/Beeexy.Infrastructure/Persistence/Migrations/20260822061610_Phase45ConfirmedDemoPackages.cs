using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase45ConfirmedDemoPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_approval_status",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_review_status",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_approval_status",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_review_status",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_approval_status",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW', 'NOT_CLINICALLY_APPROVED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_review_status",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_review_status IN ('REVIEWED', 'PROVISIONAL', 'NOT_APPLICABLE')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_approval_status",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW', 'NOT_CLINICALLY_APPROVED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_review_status",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_review_status IN ('REVIEWED', 'PROVISIONAL', 'NOT_APPLICABLE')");

            migrationBuilder.Sql(
                "UPDATE triage.questionnaire_versions SET " +
                "clinical_content_source = 'PRODUCT_DEMO_DEFINED', " +
                "clinical_review_status = 'NOT_APPLICABLE', " +
                "clinical_approval_status = 'NOT_CLINICALLY_APPROVED' " +
                "WHERE source_reference = " +
                "'Beeexy_Phase_4.5_Confirmed_Demo_Pathways_Simplified_Packages_Prompt.md';");
            migrationBuilder.Sql(
                "UPDATE triage.clinical_rule_set_versions SET " +
                "clinical_content_source = 'PRODUCT_DEMO_DEFINED', " +
                "clinical_review_status = 'NOT_APPLICABLE', " +
                "clinical_approval_status = 'NOT_CLINICALLY_APPROVED' " +
                "WHERE source_reference = " +
                "'Beeexy_Phase_4.5_Confirmed_Demo_Pathways_Simplified_Packages_Prompt.md';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE triage.questionnaire_versions SET " +
                "clinical_content_source = 'REFERENCE_PLATFORM_DERIVED', " +
                "clinical_review_status = 'PROVISIONAL', " +
                "clinical_approval_status = 'PENDING_FORMAL_REVIEW' " +
                "WHERE clinical_content_source = 'PRODUCT_DEMO_DEFINED';");
            migrationBuilder.Sql(
                "UPDATE triage.clinical_rule_set_versions SET " +
                "clinical_content_source = 'REFERENCE_PLATFORM_DERIVED', " +
                "clinical_review_status = 'PROVISIONAL', " +
                "clinical_approval_status = 'PENDING_FORMAL_REVIEW' " +
                "WHERE clinical_content_source = 'PRODUCT_DEMO_DEFINED';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_approval_status",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_review_status",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_approval_status",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_review_status",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_approval_status",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_review_status",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_review_status IN ('REVIEWED', 'PROVISIONAL')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_approval_status",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_review_status",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_review_status IN ('REVIEWED', 'PROVISIONAL')");
        }
    }
}
