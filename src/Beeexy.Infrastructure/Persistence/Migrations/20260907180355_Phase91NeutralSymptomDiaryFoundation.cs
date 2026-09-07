using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase91NeutralSymptomDiaryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.EnsureSchema(
                name: "care");

            migrationBuilder.CreateTable(
                name: "symptom_diary_package_versions",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    package_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    question_set_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    question_set_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    symptom_information_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symptom_information_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pathway_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    canonical_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    clinical_content_source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    clinical_review_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    clinical_approval_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    informational_heading = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    informational_body = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_symptom_diary_package_versions", x => x.id);
                    table.CheckConstraint("ck_symptom_diary_packages_activation", "activated_at IS NULL OR (activated_at >= imported_at AND (approved_at IS NULL OR activated_at >= approved_at))");
                    table.CheckConstraint("ck_symptom_diary_packages_approval", "(clinical_approval_status = 'APPROVED' AND approved_at IS NOT NULL) OR (clinical_approval_status <> 'APPROVED' AND approved_at IS NULL)");
                    table.CheckConstraint("ck_symptom_diary_packages_approval_status", "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW', 'NOT_CLINICALLY_APPROVED')");
                    table.CheckConstraint("ck_symptom_diary_packages_content_hash", "canonical_content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_symptom_diary_packages_content_source", "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED', 'MEDICAL_TEAM_PROVIDED')");
                    table.CheckConstraint("ck_symptom_diary_packages_information_body", "informational_body IS NULL OR length(btrim(informational_body)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_information_code", "length(btrim(symptom_information_code)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_information_heading", "informational_heading IS NULL OR length(btrim(informational_heading)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_information_version", "length(btrim(symptom_information_version)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_package_code", "length(btrim(package_code)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_package_version", "length(btrim(package_version)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_pathway", "length(btrim(pathway_code)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_question_set_code", "length(btrim(question_set_code)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_question_set_version", "length(btrim(question_set_version)) > 0");
                    table.CheckConstraint("ck_symptom_diary_packages_review_status", "clinical_review_status IN ('REVIEWED', 'PROVISIONAL', 'NOT_APPLICABLE')");
                });

            migrationBuilder.CreateTable(
                name: "symptom_check_ins",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitting_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_symptom_check_ins", x => x.id);
                    table.UniqueConstraint("ak_symptom_check_ins_id_package", x => new { x.id, x.package_version_id });
                    table.CheckConstraint("ck_symptom_check_ins_request_hash", "canonical_request_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_symptom_check_ins_episode",
                        column: x => x.episode_id,
                        principalSchema: "triage",
                        principalTable: "pre_triage_episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_symptom_check_ins_package",
                        column: x => x.package_version_id,
                        principalSchema: "care",
                        principalTable: "symptom_diary_package_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_symptom_check_ins_submitting_account",
                        column: x => x.submitting_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "symptom_diary_questions",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    source_order = table.Column<int>(type: "integer", nullable: false),
                    answer_schema = table.Column<string>(type: "jsonb", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_symptom_diary_questions", x => x.id);
                    table.UniqueConstraint("ak_symptom_diary_questions_id_package", x => new { x.id, x.package_version_id });
                    table.CheckConstraint("ck_symptom_diary_questions_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_symptom_diary_questions_order", "source_order > 0");
                    table.CheckConstraint("ck_symptom_diary_questions_prompt", "length(btrim(prompt_text)) > 0");
                    table.CheckConstraint("ck_symptom_diary_questions_schema", "jsonb_typeof(answer_schema) = 'object'");
                    table.ForeignKey(
                        name: "fk_symptom_diary_questions_package",
                        column: x => x.package_version_id,
                        principalSchema: "care",
                        principalTable: "symptom_diary_package_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "symptom_warning_signs",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    source_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_symptom_warning_signs", x => x.id);
                    table.CheckConstraint("ck_symptom_warning_signs_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_symptom_warning_signs_display", "length(btrim(display_text)) > 0");
                    table.CheckConstraint("ck_symptom_warning_signs_order", "source_order > 0");
                    table.ForeignKey(
                        name: "fk_symptom_warning_signs_package",
                        column: x => x.package_version_id,
                        principalSchema: "care",
                        principalTable: "symptom_diary_package_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "symptom_check_in_answers",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_value = table.Column<string>(type: "jsonb", nullable: false),
                    source_order = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_symptom_check_in_answers", x => x.id);
                    table.CheckConstraint("ck_symptom_check_in_answers_order", "source_order > 0");
                    table.CheckConstraint("ck_symptom_check_in_answers_value", "submitted_value IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_symptom_check_in_answers_check_in_package",
                        columns: x => new { x.check_in_id, x.package_version_id },
                        principalSchema: "care",
                        principalTable: "symptom_check_ins",
                        principalColumns: new[] { "id", "package_version_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_symptom_check_in_answers_question_package",
                        columns: x => new { x.question_id, x.package_version_id },
                        principalSchema: "care",
                        principalTable: "symptom_diary_questions",
                        principalColumns: new[] { "id", "package_version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "symptom_diary_question_options",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    display_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    source_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_symptom_diary_question_options", x => x.id);
                    table.CheckConstraint("ck_symptom_diary_options_code", "length(btrim(code)) > 0");
                    table.CheckConstraint("ck_symptom_diary_options_display", "length(btrim(display_text)) > 0");
                    table.CheckConstraint("ck_symptom_diary_options_order", "source_order > 0");
                    table.CheckConstraint("ck_symptom_diary_options_value", "length(btrim(value)) > 0");
                    table.ForeignKey(
                        name: "fk_symptom_diary_question_options_question",
                        column: x => x.question_id,
                        principalSchema: "care",
                        principalTable: "symptom_diary_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED', 'MEDICAL_TEAM_PROVIDED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED', 'MEDICAL_TEAM_PROVIDED')");

            migrationBuilder.CreateIndex(
                name: "ix_symptom_check_in_answers_check_in_package",
                schema: "care",
                table: "symptom_check_in_answers",
                columns: new[] { "check_in_id", "package_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_symptom_check_in_answers_question_package",
                schema: "care",
                table: "symptom_check_in_answers",
                columns: new[] { "question_id", "package_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_symptom_check_in_answers_check_in_question",
                schema: "care",
                table: "symptom_check_in_answers",
                columns: new[] { "check_in_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_symptom_check_ins_episode_created_id",
                schema: "care",
                table: "symptom_check_ins",
                columns: new[] { "episode_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_symptom_check_ins_package",
                schema: "care",
                table: "symptom_check_ins",
                column: "package_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_symptom_check_ins_submitting_account",
                schema: "care",
                table: "symptom_check_ins",
                column: "submitting_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_symptom_check_ins_episode_idempotency",
                schema: "care",
                table: "symptom_check_ins",
                columns: new[] { "episode_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_symptom_diary_packages_pathway_activation",
                schema: "care",
                table: "symptom_diary_package_versions",
                columns: new[] { "pathway_code", "activated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_symptom_diary_packages_code_version",
                schema: "care",
                table: "symptom_diary_package_versions",
                columns: new[] { "package_code", "package_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_symptom_diary_options_question_code",
                schema: "care",
                table: "symptom_diary_question_options",
                columns: new[] { "question_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_symptom_diary_options_question_order",
                schema: "care",
                table: "symptom_diary_question_options",
                columns: new[] { "question_id", "source_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_symptom_diary_questions_package_code",
                schema: "care",
                table: "symptom_diary_questions",
                columns: new[] { "package_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_symptom_diary_questions_package_order",
                schema: "care",
                table: "symptom_diary_questions",
                columns: new[] { "package_version_id", "source_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_symptom_warning_signs_package_code",
                schema: "care",
                table: "symptom_warning_signs",
                columns: new[] { "package_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_symptom_warning_signs_package_order",
                schema: "care",
                table: "symptom_warning_signs",
                columns: new[] { "package_version_id", "source_order" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION care.reject_symptom_diary_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'Symptom-diary records are append-only.'
                        USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER trg_symptom_diary_packages_append_only
                    BEFORE UPDATE OR DELETE ON care.symptom_diary_package_versions
                    FOR EACH ROW EXECUTE FUNCTION care.reject_symptom_diary_mutation();
                CREATE TRIGGER trg_symptom_diary_questions_append_only
                    BEFORE UPDATE OR DELETE ON care.symptom_diary_questions
                    FOR EACH ROW EXECUTE FUNCTION care.reject_symptom_diary_mutation();
                CREATE TRIGGER trg_symptom_diary_options_append_only
                    BEFORE UPDATE OR DELETE ON care.symptom_diary_question_options
                    FOR EACH ROW EXECUTE FUNCTION care.reject_symptom_diary_mutation();
                CREATE TRIGGER trg_symptom_warning_signs_append_only
                    BEFORE UPDATE OR DELETE ON care.symptom_warning_signs
                    FOR EACH ROW EXECUTE FUNCTION care.reject_symptom_diary_mutation();
                CREATE TRIGGER trg_symptom_check_ins_append_only
                    BEFORE UPDATE OR DELETE ON care.symptom_check_ins
                    FOR EACH ROW EXECUTE FUNCTION care.reject_symptom_diary_mutation();
                CREATE TRIGGER trg_symptom_check_in_answers_append_only
                    BEFORE UPDATE OR DELETE ON care.symptom_check_in_answers
                    FOR EACH ROW EXECUTE FUNCTION care.reject_symptom_diary_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "symptom_check_in_answers",
                schema: "care");

            migrationBuilder.DropTable(
                name: "symptom_diary_question_options",
                schema: "care");

            migrationBuilder.DropTable(
                name: "symptom_warning_signs",
                schema: "care");

            migrationBuilder.DropTable(
                name: "symptom_check_ins",
                schema: "care");

            migrationBuilder.DropTable(
                name: "symptom_diary_questions",
                schema: "care");

            migrationBuilder.DropTable(
                name: "symptom_diary_package_versions",
                schema: "care");

            migrationBuilder.Sql("DROP FUNCTION care.reject_symptom_diary_mutation();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_questionnaire_versions_content_source",
                schema: "triage",
                table: "questionnaire_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinical_rule_set_versions_content_source",
                schema: "triage",
                table: "clinical_rule_set_versions",
                sql: "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED')");
        }
    }
}
