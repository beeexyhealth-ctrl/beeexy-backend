using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase36ApprovedPatientDemographics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                schema: "patients",
                table: "patient_profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                schema: "patients",
                table: "patient_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                schema: "patients",
                table: "patient_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sex_assigned_at_birth",
                schema: "patients",
                table: "patient_profiles",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "patients",
                table: "patient_profiles",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "patients",
                table: "patient_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_patient_profiles_first_name",
                schema: "patients",
                table: "patient_profiles",
                sql: "first_name IS NULL OR length(btrim(first_name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_patient_profiles_last_name",
                schema: "patients",
                table: "patient_profiles",
                sql: "last_name IS NULL OR length(btrim(last_name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_patient_profiles_sex_assigned_at_birth",
                schema: "patients",
                table: "patient_profiles",
                sql: "sex_assigned_at_birth IS NULL OR sex_assigned_at_birth IN ('male', 'female')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_patient_profiles_state",
                schema: "patients",
                table: "patient_profiles",
                sql: "state IS NULL OR state IN ('AL','AK','AZ','AR','CA','CO','CT','DE','FL','GA','HI','ID','IL','IN','IA','KS','KY','LA','ME','MD','MA','MI','MN','MS','MO','MT','NE','NV','NH','NJ','NM','NY','NC','ND','OH','OK','OR','PA','RI','SC','SD','TN','TX','UT','VT','VA','WA','WV','WI','WY')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_patient_profiles_version",
                schema: "patients",
                table: "patient_profiles",
                sql: "version > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_profiles_first_name",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_profiles_last_name",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_profiles_sex_assigned_at_birth",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_profiles_state",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_profiles_version",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropColumn(
                name: "first_name",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropColumn(
                name: "last_name",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropColumn(
                name: "sex_assigned_at_birth",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "patients",
                table: "patient_profiles");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "patients",
                table: "patient_profiles");
        }
    }
}
