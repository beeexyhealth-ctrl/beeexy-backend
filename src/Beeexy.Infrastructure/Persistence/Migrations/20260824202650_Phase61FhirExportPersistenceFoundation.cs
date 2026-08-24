using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase61FhirExportPersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "interoperability");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_clinical_history_events_id_patient_profile",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "id", "patient_profile_id" });

            migrationBuilder.CreateTable(
                name: "fhir_exports",
                schema: "interoperability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_clinical_history_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fhir_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    mapping_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    profile_canonical = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    profile_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    checksum_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    checksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    private_artifact_storage_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    validation_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    validation_outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fhir_exports", x => x.id);
                    table.UniqueConstraint(
                        "ak_fhir_exports_validated_artifact",
                        x => new
                        {
                            x.id,
                            x.validation_outcome,
                            x.checksum_algorithm,
                            x.checksum,
                            x.validation_completed_at
                        });
                    table.CheckConstraint("ck_fhir_exports_lifecycle_metadata", "(status = 'pending' AND checksum_algorithm IS NULL AND checksum IS NULL AND private_artifact_storage_uri IS NULL AND generated_at IS NULL AND validation_completed_at IS NULL AND validation_outcome IS NULL) OR (status = 'generated' AND length(btrim(checksum_algorithm)) > 0 AND length(btrim(checksum)) > 0 AND length(btrim(private_artifact_storage_uri)) > 0 AND generated_at IS NOT NULL AND validation_completed_at IS NULL AND validation_outcome IS NULL) OR (status = 'validation_failed' AND length(btrim(checksum_algorithm)) > 0 AND length(btrim(checksum)) > 0 AND length(btrim(private_artifact_storage_uri)) > 0 AND generated_at IS NOT NULL AND validation_completed_at IS NOT NULL AND validation_outcome = 'failed') OR (status = 'validated' AND length(btrim(checksum_algorithm)) > 0 AND length(btrim(checksum)) > 0 AND length(btrim(private_artifact_storage_uri)) > 0 AND generated_at IS NOT NULL AND validation_completed_at IS NOT NULL AND validation_outcome = 'passed')");
                    table.CheckConstraint("ck_fhir_exports_status", "status IN ('pending', 'generated', 'validation_failed', 'validated')");
                    table.CheckConstraint("ck_fhir_exports_timestamps", "updated_at >= created_at AND (generated_at IS NULL OR generated_at >= created_at) AND (validation_completed_at IS NULL OR validation_completed_at >= generated_at) AND updated_at >= COALESCE(validation_completed_at, generated_at, created_at)");
                    table.CheckConstraint("ck_fhir_exports_versions", "length(btrim(fhir_version)) > 0 AND length(btrim(mapping_version)) > 0 AND ((profile_canonical IS NULL AND profile_version IS NULL) OR (length(btrim(profile_canonical)) > 0 AND length(btrim(profile_version)) > 0))");
                    table.ForeignKey(
                        name: "fk_fhir_exports_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fhir_exports_source_history_event_patient",
                        columns: x => new { x.source_clinical_history_event_id, x.patient_profile_id },
                        principalSchema: "history",
                        principalTable: "clinical_history_events",
                        principalColumns: new[] { "id", "patient_profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fhir_validation_results",
                schema: "interoperability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fhir_export_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    validator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    validator_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    artifact_checksum_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    artifact_checksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fhir_validation_results", x => x.id);
                    table.CheckConstraint("ck_fhir_validation_results_counts", "error_count >= 0 AND warning_count >= 0");
                    table.CheckConstraint("ck_fhir_validation_results_metadata", "length(btrim(validator_name)) > 0 AND length(btrim(validator_version)) > 0 AND length(btrim(artifact_checksum_algorithm)) > 0 AND length(btrim(artifact_checksum)) > 0");
                    table.CheckConstraint("ck_fhir_validation_results_outcome", "(outcome = 'passed' AND error_count = 0) OR (outcome = 'failed' AND error_count > 0)");
                    table.ForeignKey(
                        name: "fk_fhir_validation_results_export",
                        column: x => x.fhir_export_id,
                        principalSchema: "interoperability",
                        principalTable: "fhir_exports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fhir_validation_results_validated_artifact",
                        columns: x => new
                        {
                            x.fhir_export_id,
                            x.outcome,
                            x.artifact_checksum_algorithm,
                            x.artifact_checksum,
                            x.validated_at
                        },
                        principalSchema: "interoperability",
                        principalTable: "fhir_exports",
                        principalColumns: new[]
                        {
                            "id",
                            "validation_outcome",
                            "checksum_algorithm",
                            "checksum",
                            "validation_completed_at"
                        },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fhir_exports_patient_created_id",
                schema: "interoperability",
                table: "fhir_exports",
                columns: new[] { "patient_profile_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_fhir_exports_source_history_event_patient",
                schema: "interoperability",
                table: "fhir_exports",
                columns: new[] { "source_clinical_history_event_id", "patient_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fhir_exports_status_updated_at",
                schema: "interoperability",
                table: "fhir_exports",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_fhir_exports_patient_idempotency_key",
                schema: "interoperability",
                table: "fhir_exports",
                columns: new[] { "patient_profile_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fhir_validation_results_outcome_validated_at",
                schema: "interoperability",
                table: "fhir_validation_results",
                columns: new[] { "outcome", "validated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_fhir_validation_results_export_id",
                schema: "interoperability",
                table: "fhir_validation_results",
                column: "fhir_export_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fhir_validation_results_validated_artifact",
                schema: "interoperability",
                table: "fhir_validation_results",
                columns: new[]
                {
                    "fhir_export_id",
                    "outcome",
                    "artifact_checksum_algorithm",
                    "artifact_checksum",
                    "validated_at"
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fhir_validation_results",
                schema: "interoperability");

            migrationBuilder.DropTable(
                name: "fhir_exports",
                schema: "interoperability");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_clinical_history_events_id_patient_profile",
                schema: "history",
                table: "clinical_history_events");
        }
    }
}
