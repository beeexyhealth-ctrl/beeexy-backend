using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase41PreTriagePersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "triage");

            migrationBuilder.CreateTable(
                name: "clinical_rule_set_versions",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_set_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_rule_set_versions", x => x.id);
                    table.CheckConstraint("ck_clinical_rule_set_versions_activation", "activated_at IS NULL OR (activated_at >= imported_at AND activated_at >= approved_at)");
                    table.CheckConstraint("ck_clinical_rule_set_versions_code", "length(btrim(rule_set_code)) > 0");
                    table.CheckConstraint("ck_clinical_rule_set_versions_content_hash", "length(content_hash) >= 32");
                    table.CheckConstraint("ck_clinical_rule_set_versions_version", "length(btrim(version)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_versions",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_versions", x => x.id);
                    table.CheckConstraint("ck_questionnaire_versions_activation", "activated_at IS NULL OR (activated_at >= imported_at AND activated_at >= approved_at)");
                    table.CheckConstraint("ck_questionnaire_versions_code", "length(btrim(questionnaire_code)) > 0");
                    table.CheckConstraint("ck_questionnaire_versions_content_hash", "length(content_hash) >= 32");
                    table.CheckConstraint("ck_questionnaire_versions_version", "length(btrim(version)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "pre_triage_sessions",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    questionnaire_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anonymous_capability_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pre_triage_sessions", x => x.id);
                    table.UniqueConstraint("ak_pre_triage_sessions_id_questionnaire_version_id", x => new { x.id, x.questionnaire_version_id });
                    table.CheckConstraint("ck_pre_triage_sessions_completion", "(status = 'active' AND completed_at IS NULL) OR (status = 'completed' AND completed_at IS NOT NULL AND completed_at >= created_at AND completed_at < expires_at)");
                    table.CheckConstraint("ck_pre_triage_sessions_expiration", "expires_at > created_at");
                    table.CheckConstraint("ck_pre_triage_sessions_ownership", "(patient_profile_id IS NULL AND anonymous_capability_hash IS NOT NULL) OR (patient_profile_id IS NOT NULL AND anonymous_capability_hash IS NULL)");
                    table.CheckConstraint("ck_pre_triage_sessions_status", "status IN ('active', 'completed')");
                    table.ForeignKey(
                        name: "fk_pre_triage_sessions_patient_profiles_patient_profile_id",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pre_triage_sessions_questionnaire_versions_version_id",
                        column: x => x.questionnaire_version_id,
                        principalSchema: "triage",
                        principalTable: "questionnaire_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    answer_schema = table.Column<string>(type: "jsonb", nullable: true),
                    branching_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                    table.UniqueConstraint("ak_questions_id_questionnaire_version_id", x => new { x.id, x.questionnaire_version_id });
                    table.CheckConstraint("ck_questions_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_questions_display_order", "display_order > 0");
                    table.CheckConstraint("ck_questions_prompt_text", "length(btrim(prompt_text)) > 0");
                    table.ForeignKey(
                        name: "fk_questions_questionnaire_versions_questionnaire_version_id",
                        column: x => x.questionnaire_version_id,
                        principalSchema: "triage",
                        principalTable: "questionnaire_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pre_triage_episodes",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    questionnaire_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinical_rule_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    anonymous_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pre_triage_episodes", x => x.id);
                    table.UniqueConstraint("ak_pre_triage_episodes_id_clinical_rule_set_version_id", x => new { x.id, x.clinical_rule_set_version_id });
                    table.UniqueConstraint("ak_pre_triage_episodes_id_questionnaire_version_id", x => new { x.id, x.questionnaire_version_id });
                    table.CheckConstraint("ck_pre_triage_episodes_anonymous_claim", "(patient_profile_id IS NULL AND anonymous_expires_at IS NOT NULL AND claimed_at IS NULL) OR (patient_profile_id IS NOT NULL AND ((anonymous_expires_at IS NULL AND claimed_at IS NULL) OR (anonymous_expires_at IS NOT NULL AND claimed_at IS NOT NULL)))");
                    table.CheckConstraint("ck_pre_triage_episodes_anonymous_expiration", "anonymous_expires_at IS NULL OR anonymous_expires_at > completed_at");
                    table.CheckConstraint("ck_pre_triage_episodes_claim_timestamp", "claimed_at IS NULL OR (claimed_at >= completed_at AND claimed_at < anonymous_expires_at)");
                    table.ForeignKey(
                        name: "fk_pre_triage_episodes_patient_profiles_patient_profile_id",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pre_triage_episodes_questionnaire_versions_version_id",
                        column: x => x.questionnaire_version_id,
                        principalSchema: "triage",
                        principalTable: "questionnaire_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pre_triage_episodes_rule_set_versions_version_id",
                        column: x => x.clinical_rule_set_version_id,
                        principalSchema: "triage",
                        principalTable: "clinical_rule_set_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pre_triage_episodes_sessions_source_session_version",
                        columns: x => new { x.source_session_id, x.questionnaire_version_id },
                        principalSchema: "triage",
                        principalTable: "pre_triage_sessions",
                        principalColumns: new[] { "id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "answers",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: true),
                    questionnaire_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    answer = table.Column<string>(type: "jsonb", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_answers", x => x.id);
                    table.CheckConstraint("ck_answers_owner", "(session_id IS NOT NULL AND episode_id IS NULL) OR (session_id IS NULL AND episode_id IS NOT NULL)");
                    table.CheckConstraint("ck_answers_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_answers_pre_triage_episodes_episode_version",
                        columns: x => new { x.episode_id, x.questionnaire_version_id },
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumns: new[] { "id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_answers_pre_triage_sessions_session_version",
                        columns: x => new { x.session_id, x.questionnaire_version_id },
                        principalSchema: "triage",
                        principalTable: "pre_triage_sessions",
                        principalColumns: new[] { "id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_answers_questions_question_version",
                        columns: x => new { x.question_id, x.questionnaire_version_id },
                        principalSchema: "triage",
                        principalTable: "questions",
                        principalColumns: new[] { "id", "questionnaire_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_assessments",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinical_rule_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    urgency_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    result_message_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_assessments", x => x.id);
                    table.CheckConstraint("ck_clinical_assessments_urgency_code", "length(btrim(urgency_code)) > 0");
                    table.ForeignKey(
                        name: "fk_clinical_assessments_episodes_episode_rule_set_version",
                        columns: x => new { x.episode_id, x.clinical_rule_set_version_id },
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumns: new[] { "id", "clinical_rule_set_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_assessments_rule_set_versions_version_id",
                        column: x => x.clinical_rule_set_version_id,
                        principalSchema: "triage",
                        principalTable: "clinical_rule_set_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reported_symptoms",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    terminology_system = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    terminology_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    terminology_display = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    normalization_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    normalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reported_symptoms", x => x.id);
                    table.CheckConstraint("ck_reported_symptoms_normalization", "(terminology_system IS NULL AND terminology_code IS NULL AND terminology_display IS NULL AND normalization_source IS NULL AND normalized_at IS NULL) OR (terminology_system IS NOT NULL AND terminology_code IS NOT NULL AND normalization_source IS NOT NULL AND normalized_at IS NOT NULL)");
                    table.CheckConstraint("ck_reported_symptoms_normalized_at", "normalized_at IS NULL OR normalized_at >= reported_at");
                    table.CheckConstraint("ck_reported_symptoms_original_text", "length(btrim(original_text)) > 0");
                    table.CheckConstraint("ck_reported_symptoms_owner", "(session_id IS NOT NULL AND episode_id IS NULL) OR (session_id IS NULL AND episode_id IS NOT NULL)");
                    table.CheckConstraint("ck_reported_symptoms_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_reported_symptoms_pre_triage_episodes_episode_id",
                        column: x => x.episode_id,
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reported_symptoms_pre_triage_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "triage",
                        principalTable: "pre_triage_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clinical_findings",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_findings", x => x.id);
                    table.CheckConstraint("ck_clinical_findings_finding_code", "length(btrim(finding_code)) > 0");
                    table.CheckConstraint("ck_clinical_findings_source_rule_code", "length(btrim(source_rule_code)) > 0");
                    table.ForeignKey(
                        name: "fk_clinical_findings_clinical_assessments_assessment_id",
                        column: x => x.assessment_id,
                        principalSchema: "triage",
                        principalTable: "clinical_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_answers_episode_questionnaire_version",
                schema: "triage",
                table: "answers",
                columns: new[] { "episode_id", "questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_answers_question_questionnaire_version",
                schema: "triage",
                table: "answers",
                columns: new[] { "question_id", "questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_answers_session_questionnaire_version",
                schema: "triage",
                table: "answers",
                columns: new[] { "session_id", "questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_answers_episode_sequence",
                schema: "triage",
                table: "answers",
                columns: new[] { "episode_id", "sequence" },
                unique: true,
                filter: "episode_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_answers_session_sequence",
                schema: "triage",
                table: "answers",
                columns: new[] { "session_id", "sequence" },
                unique: true,
                filter: "session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_assessments_clinical_rule_set_version_id",
                schema: "triage",
                table: "clinical_assessments",
                column: "clinical_rule_set_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_assessments_episode_rule_set_version",
                schema: "triage",
                table: "clinical_assessments",
                columns: new[] { "episode_id", "clinical_rule_set_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_clinical_assessments_episode_id",
                schema: "triage",
                table: "clinical_assessments",
                column: "episode_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_clinical_findings_assessment_finding_code",
                schema: "triage",
                table: "clinical_findings",
                columns: new[] { "assessment_id", "finding_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clinical_rule_set_versions_code_activation",
                schema: "triage",
                table: "clinical_rule_set_versions",
                columns: new[] { "rule_set_code", "activated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_clinical_rule_set_versions_code_version",
                schema: "triage",
                table: "clinical_rule_set_versions",
                columns: new[] { "rule_set_code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_episodes_clinical_rule_set_version_id",
                schema: "triage",
                table: "pre_triage_episodes",
                column: "clinical_rule_set_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_episodes_patient_completed_at",
                schema: "triage",
                table: "pre_triage_episodes",
                columns: new[] { "patient_profile_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_episodes_questionnaire_version_id",
                schema: "triage",
                table: "pre_triage_episodes",
                column: "questionnaire_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_episodes_source_session_version",
                schema: "triage",
                table: "pre_triage_episodes",
                columns: new[] { "source_session_id", "questionnaire_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_episodes_unclaimed_expiry",
                schema: "triage",
                table: "pre_triage_episodes",
                column: "anonymous_expires_at",
                filter: "patient_profile_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pre_triage_episodes_source_session_id",
                schema: "triage",
                table: "pre_triage_episodes",
                column: "source_session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_sessions_patient_profile_id",
                schema: "triage",
                table: "pre_triage_sessions",
                column: "patient_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_sessions_questionnaire_version_id",
                schema: "triage",
                table: "pre_triage_sessions",
                column: "questionnaire_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_triage_sessions_status_expiry",
                schema: "triage",
                table: "pre_triage_sessions",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_pre_triage_sessions_anonymous_capability_hash",
                schema: "triage",
                table: "pre_triage_sessions",
                column: "anonymous_capability_hash",
                unique: true,
                filter: "anonymous_capability_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_versions_code_activation",
                schema: "triage",
                table: "questionnaire_versions",
                columns: new[] { "questionnaire_code", "activated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_questionnaire_versions_code_version",
                schema: "triage",
                table: "questionnaire_versions",
                columns: new[] { "questionnaire_code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_questions_questionnaire_version_code",
                schema: "triage",
                table: "questions",
                columns: new[] { "questionnaire_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_questions_questionnaire_version_order",
                schema: "triage",
                table: "questions",
                columns: new[] { "questionnaire_version_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reported_symptoms_episode_sequence",
                schema: "triage",
                table: "reported_symptoms",
                columns: new[] { "episode_id", "sequence" },
                unique: true,
                filter: "episode_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_reported_symptoms_session_sequence",
                schema: "triage",
                table: "reported_symptoms",
                columns: new[] { "session_id", "sequence" },
                unique: true,
                filter: "session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answers",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "clinical_findings",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "reported_symptoms",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "questions",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "clinical_assessments",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "pre_triage_episodes",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "clinical_rule_set_versions",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "pre_triage_sessions",
                schema: "triage");

            migrationBuilder.DropTable(
                name: "questionnaire_versions",
                schema: "triage");
        }
    }
}
