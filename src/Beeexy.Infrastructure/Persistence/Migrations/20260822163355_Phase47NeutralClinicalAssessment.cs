using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase47NeutralClinicalAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_assessments_urgency_code",
                schema: "triage",
                table: "clinical_assessments");

            migrationBuilder.AlterColumn<string>(
                name: "urgency_code",
                schema: "triage",
                table: "clinical_assessments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_assessments_urgency_code",
                schema: "triage",
                table: "clinical_assessments",
                sql: "urgency_code IS NULL OR length(btrim(urgency_code)) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_assessments_urgency_code",
                schema: "triage",
                table: "clinical_assessments");

            migrationBuilder.AlterColumn<string>(
                name: "urgency_code",
                schema: "triage",
                table: "clinical_assessments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_assessments_urgency_code",
                schema: "triage",
                table: "clinical_assessments",
                sql: "length(btrim(urgency_code)) > 0");
        }
    }
}
