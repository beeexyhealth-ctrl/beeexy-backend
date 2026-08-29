using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase75VersionedDoctorMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "doctor_match_rule_configurations",
                schema: "directory",
                columns: table => new
                {
                    rule_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    specialty_weight_points = table.Column<int>(type: "integer", nullable: false),
                    language_weight_points = table.Column<int>(type: "integer", nullable: false),
                    location_weight_points = table.Column<int>(type: "integer", nullable: false),
                    stored_insurance_weight_points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_match_rule_configurations", x => x.rule_version_id);
                    table.CheckConstraint("ck_doctor_match_rule_configurations_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_doctor_match_rule_configurations_package_code", "length(btrim(package_code)) > 0");
                    table.CheckConstraint("ck_doctor_match_rule_configurations_weights", "specialty_weight_points BETWEEN 1 AND 100 AND language_weight_points BETWEEN 1 AND 100 AND location_weight_points BETWEEN 1 AND 100 AND stored_insurance_weight_points BETWEEN 1 AND 100 AND specialty_weight_points + language_weight_points + location_weight_points + stored_insurance_weight_points = 100");
                    table.ForeignKey(
                        name: "fk_doctor_match_rule_configurations_rule_versions",
                        column: x => x.rule_version_id,
                        principalSchema: "directory",
                        principalTable: "doctor_match_rule_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_match_rule_configurations_package_code",
                schema: "directory",
                table: "doctor_match_rule_configurations",
                column: "package_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_match_rule_configurations",
                schema: "directory");
        }
    }
}
