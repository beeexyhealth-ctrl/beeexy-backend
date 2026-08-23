using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase51ClinicalHistoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_pre_triage_episodes_id_patient_profile",
                schema: "triage",
                table: "pre_triage_episodes",
                columns: new[] { "id", "patient_profile_id" });

            migrationBuilder.CreateTable(
                name: "clinical_history_events",
                schema: "history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_questionnaire_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_clinical_rule_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_history_events", x => x.id);
                    table.UniqueConstraint("ak_clinical_history_events_source_provenance", x => new { x.id, x.source_type, x.source_id, x.source_questionnaire_version_id, x.source_clinical_rule_set_version_id });
                    table.CheckConstraint("ck_clinical_history_events_recorded_at", "recorded_at >= occurred_at");
                    table.CheckConstraint("ck_clinical_history_events_supported_type", "event_type = 'completed_pre_triage' AND source_type = 'pre_triage_episode'");
                    table.ForeignKey(
                        name: "fk_clinical_history_events_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_history_events_source_questionnaire",
                        columns: x => new { x.source_id, x.source_questionnaire_version_id },
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumns: new[] { "id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_history_events_source_rule_set",
                        columns: x => new { x.source_id, x.source_clinical_rule_set_version_id },
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumns: new[] { "id", "clinical_rule_set_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_amendments",
                schema: "history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinical_history_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_questionnaire_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_clinical_rule_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_amendments", x => x.id);
                    table.CheckConstraint("ck_clinical_amendments_reason", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_clinical_amendments_supported_source", "source_type = 'pre_triage_episode'");
                    table.ForeignKey(
                        name: "fk_clinical_amendments_author_account",
                        column: x => x.author_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_amendments_event_source_provenance",
                        columns: x => new { x.clinical_history_event_id, x.source_type, x.source_id, x.source_questionnaire_version_id, x.source_clinical_rule_set_version_id },
                        principalSchema: "history",
                        principalTable: "clinical_history_events",
                        principalColumns: new[] { "id", "source_type", "source_id", "source_questionnaire_version_id", "source_clinical_rule_set_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_amendments_source_episode",
                        column: x => x.source_id,
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_amendments_author_account",
                schema: "history",
                table: "clinical_amendments",
                column: "author_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_amendments_event_created_id",
                schema: "history",
                table: "clinical_amendments",
                columns: new[] { "clinical_history_event_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_amendments_event_source_provenance",
                schema: "history",
                table: "clinical_amendments",
                columns: new[] { "clinical_history_event_id", "source_type", "source_id", "source_questionnaire_version_id", "source_clinical_rule_set_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_amendments_source_episode",
                schema: "history",
                table: "clinical_amendments",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_history_events_patient_event_type",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "patient_profile_id", "event_type" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_history_events_patient_occurred_id",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "patient_profile_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_history_events_source_questionnaire",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "source_id", "source_questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_history_events_source_rule_set",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "source_id", "source_clinical_rule_set_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_clinical_history_events_source_projection",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "source_type", "source_id", "event_type" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_clinical_history_events_source_patient",
                schema: "history",
                table: "clinical_history_events",
                columns: new[] { "source_id", "patient_profile_id" },
                principalSchema: "triage",
                principalTable: "pre_triage_episodes",
                principalColumns: new[] { "id", "patient_profile_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_amendments",
                schema: "history");

            migrationBuilder.DropTable(
                name: "clinical_history_events",
                schema: "history");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pre_triage_episodes_id_patient_profile",
                schema: "triage",
                table: "pre_triage_episodes");
        }
    }
}
