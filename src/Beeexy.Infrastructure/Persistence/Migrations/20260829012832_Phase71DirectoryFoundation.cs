using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase71DirectoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "directory");

            migrationBuilder.CreateTable(
                name: "clinics",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinics", x => x.id);
                    table.CheckConstraint("ck_clinics_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_clinics_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "doctor_match_rule_versions",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_match_rule_versions", x => x.id);
                    table.CheckConstraint("ck_doctor_match_rule_versions_version", "length(btrim(version)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "doctors",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctors", x => x.id);
                    table.CheckConstraint("ck_doctors_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_doctors_display_name", "length(btrim(display_name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "insurance_plans",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_insurance_plans", x => x.id);
                    table.CheckConstraint("ck_insurance_plans_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_insurance_plans_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "languages",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_languages", x => x.id);
                    table.CheckConstraint("ck_languages_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_languages_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "specialties",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_specialties", x => x.id);
                    table.CheckConstraint("ck_specialties_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_specialties_name", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "clinic_locations",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    locality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    administrative_area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_locations", x => x.id);
                    table.UniqueConstraint("ak_clinic_locations_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_clinic_locations_area", "length(btrim(administrative_area)) > 0");
                    table.CheckConstraint("ck_clinic_locations_country", "length(btrim(country)) > 0");
                    table.CheckConstraint("ck_clinic_locations_locality", "length(btrim(locality)) > 0");
                    table.CheckConstraint("ck_clinic_locations_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_clinic_locations_timezone", "length(btrim(timezone)) > 0");
                    table.ForeignKey(
                        name: "fk_clinic_locations_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "directory",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_credentials",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_credentials", x => x.id);
                    table.CheckConstraint("ck_doctor_credentials_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_doctor_credentials_status", "status IN ('submitted','pending_verification','verified','rejected')");
                    table.ForeignKey(
                        name: "fk_doctor_credentials_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalSchema: "directory",
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_insurance_participations",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    insurance_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_insurance_participations", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_insurance_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalSchema: "directory",
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_insurance_plans_plan_id",
                        column: x => x.insurance_plan_id,
                        principalSchema: "directory",
                        principalTable: "insurance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_languages",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_languages", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_languages_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalSchema: "directory",
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_languages_languages_id",
                        column: x => x.language_id,
                        principalSchema: "directory",
                        principalTable: "languages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_specialties",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specialty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_specialties", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_specialties_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalSchema: "directory",
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_specialties_specialties_id",
                        column: x => x.specialty_id,
                        principalSchema: "directory",
                        principalTable: "specialties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_affiliations",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_affiliations", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_affiliations_clinic_locations",
                        columns: x => new { x.clinic_id, x.clinic_location_id },
                        principalSchema: "directory",
                        principalTable: "clinic_locations",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_affiliations_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "directory",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_affiliations_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalSchema: "directory",
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clinic_locations_area_published",
                schema: "directory",
                table: "clinic_locations",
                columns: new[] { "country", "administrative_area", "locality", "is_published" });

            migrationBuilder.CreateIndex(
                name: "ix_clinic_locations_clinic_published",
                schema: "directory",
                table: "clinic_locations",
                columns: new[] { "clinic_id", "is_published" });

            migrationBuilder.CreateIndex(
                name: "ix_clinics_published",
                schema: "directory",
                table: "clinics",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ux_clinics_code",
                schema: "directory",
                table: "clinics",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_affiliations_clinic_location",
                schema: "directory",
                table: "doctor_affiliations",
                columns: new[] { "clinic_id", "clinic_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_affiliations_clinic_published",
                schema: "directory",
                table: "doctor_affiliations",
                columns: new[] { "clinic_id", "is_published" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_affiliations_doctor_published",
                schema: "directory",
                table: "doctor_affiliations",
                columns: new[] { "doctor_id", "is_published" });

            migrationBuilder.CreateIndex(
                name: "ux_doctor_affiliations_clinic_only",
                schema: "directory",
                table: "doctor_affiliations",
                columns: new[] { "doctor_id", "clinic_id" },
                unique: true,
                filter: "clinic_location_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_doctor_affiliations_location",
                schema: "directory",
                table: "doctor_affiliations",
                columns: new[] { "doctor_id", "clinic_id", "clinic_location_id" },
                unique: true,
                filter: "clinic_location_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_credentials_doctor_status",
                schema: "directory",
                table: "doctor_credentials",
                columns: new[] { "doctor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_insurance_plan_id",
                schema: "directory",
                table: "doctor_insurance_participations",
                column: "insurance_plan_id");

            migrationBuilder.CreateIndex(
                name: "ux_doctor_insurance_doctor_plan",
                schema: "directory",
                table: "doctor_insurance_participations",
                columns: new[] { "doctor_id", "insurance_plan_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_languages_language_id",
                schema: "directory",
                table: "doctor_languages",
                column: "language_id");

            migrationBuilder.CreateIndex(
                name: "ux_doctor_languages_doctor_language",
                schema: "directory",
                table: "doctor_languages",
                columns: new[] { "doctor_id", "language_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_doctor_match_rule_versions_version",
                schema: "directory",
                table: "doctor_match_rule_versions",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_specialties_specialty_id",
                schema: "directory",
                table: "doctor_specialties",
                column: "specialty_id");

            migrationBuilder.CreateIndex(
                name: "ux_doctor_specialties_doctor_specialty",
                schema: "directory",
                table: "doctor_specialties",
                columns: new[] { "doctor_id", "specialty_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctors_published",
                schema: "directory",
                table: "doctors",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ux_doctors_code",
                schema: "directory",
                table: "doctors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_insurance_plans_code",
                schema: "directory",
                table: "insurance_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_languages_code",
                schema: "directory",
                table: "languages",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_specialties_code",
                schema: "directory",
                table: "specialties",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_affiliations",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "doctor_credentials",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "doctor_insurance_participations",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "doctor_languages",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "doctor_match_rule_versions",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "doctor_specialties",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "clinic_locations",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "insurance_plans",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "languages",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "doctors",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "specialties",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "clinics",
                schema: "directory");
        }
    }
}
