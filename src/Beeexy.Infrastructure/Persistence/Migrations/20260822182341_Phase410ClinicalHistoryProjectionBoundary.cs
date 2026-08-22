using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase410ClinicalHistoryProjectionBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "history");

            migrationBuilder.CreateTable(
                name: "pre_triage_projection_records",
                schema: "history",
                columns: table => new
                {
                    source_episode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pre_triage_projection_records", x => x.source_episode_id);
                    table.CheckConstraint("ck_pre_triage_projection_records_created_at", "created_at >= completed_at");
                    table.ForeignKey(
                        name: "fk_pre_triage_projection_records_patient_profile_id",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pre_triage_projection_records_source_episode_id",
                        column: x => x.source_episode_id,
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_projection_records_patient_completed_at",
                schema: "history",
                table: "pre_triage_projection_records",
                columns: new[] { "patient_profile_id", "completed_at" });

            migrationBuilder.Sql(
                """
                INSERT INTO history.pre_triage_projection_records
                    (source_episode_id, patient_profile_id, completed_at, created_at)
                SELECT
                    episode.id,
                    episode.patient_profile_id,
                    episode.completed_at,
                    COALESCE(episode.claimed_at, episode.completed_at)
                FROM triage.pre_triage_episodes AS episode
                INNER JOIN triage.pre_triage_sessions AS session
                    ON session.id = episode.source_session_id
                    AND session.questionnaire_version_id = episode.questionnaire_version_id
                INNER JOIN triage.clinical_assessments AS assessment
                    ON assessment.episode_id = episode.id
                    AND assessment.clinical_rule_set_version_id =
                        episode.clinical_rule_set_version_id
                WHERE episode.patient_profile_id IS NOT NULL
                    AND session.status = 'completed'
                    AND session.completed_at = episode.completed_at
                    AND assessment.urgency_code IS NULL
                    AND assessment.result_message_reference IS NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM triage.clinical_findings AS finding
                        WHERE finding.assessment_id = assessment.id)
                    AND (
                        (session.patient_profile_id IS NOT NULL
                            AND episode.patient_profile_id = session.patient_profile_id
                            AND episode.anonymous_expires_at IS NULL
                            AND episode.claimed_at IS NULL)
                        OR
                        (session.patient_profile_id IS NULL
                            AND episode.anonymous_expires_at = session.expires_at
                            AND episode.claimed_at IS NOT NULL)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_triage_projection_records",
                schema: "history");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS history;");
        }
    }
}
