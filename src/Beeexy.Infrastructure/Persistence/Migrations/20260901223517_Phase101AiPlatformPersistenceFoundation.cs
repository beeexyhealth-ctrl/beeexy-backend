using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase101AiPlatformPersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "ai_conversations",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_conversations", x => x.id);
                    table.CheckConstraint("ck_ai_conversations_deleted_at", "deleted_at IS NULL OR deleted_at >= created_at");
                    table.ForeignKey(
                        name: "fk_ai_conversations_account",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_conversations_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_analysis_requests",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_input_schema_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    original_input_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_analysis_requests", x => x.id);
                    table.CheckConstraint("ck_ai_analysis_requests_input_schema", "length(btrim(original_input_schema_version)) > 0");
                    table.CheckConstraint("ck_ai_analysis_requests_input_snapshot", "jsonb_typeof(original_input_snapshot) = 'object'");
                    table.CheckConstraint("ck_ai_analysis_requests_purpose", "purpose IN ('conversation', 'second_opinion')");
                    table.ForeignKey(
                        name: "fk_ai_analysis_requests_account",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_analysis_requests_conversation",
                        column: x => x.conversation_id,
                        principalSchema: "ai",
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_analysis_requests_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_messages",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_messages", x => x.id);
                    table.CheckConstraint("ck_ai_messages_content", "length(btrim(content)) > 0");
                    table.CheckConstraint("ck_ai_messages_role", "role IN ('user', 'assistant')");
                    table.CheckConstraint("ck_ai_messages_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_ai_messages_conversation",
                        column: x => x.conversation_id,
                        principalSchema: "ai",
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_executions",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    model_identifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    latency_milliseconds = table.Column<long>(type: "bigint", nullable: true),
                    sanitized_failure_category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_executions", x => x.id);
                    table.UniqueConstraint("ak_ai_executions_id_analysis_request", x => new { x.id, x.analysis_request_id });
                    table.CheckConstraint("ck_ai_executions_lifecycle", "(status = 'pending' AND provider_identifier IS NULL AND model_identifier IS NULL AND prompt_version IS NULL AND started_at IS NULL AND completed_at IS NULL AND latency_milliseconds IS NULL AND sanitized_failure_category IS NULL) OR (status = 'running' AND length(btrim(provider_identifier)) > 0 AND length(btrim(model_identifier)) > 0 AND length(btrim(prompt_version)) > 0 AND started_at IS NOT NULL AND completed_at IS NULL AND latency_milliseconds IS NULL AND sanitized_failure_category IS NULL) OR (status IN ('succeeded', 'rejected') AND length(btrim(provider_identifier)) > 0 AND length(btrim(model_identifier)) > 0 AND length(btrim(prompt_version)) > 0 AND started_at IS NOT NULL AND completed_at IS NOT NULL AND latency_milliseconds >= 0 AND sanitized_failure_category IS NULL) OR (status = 'failed' AND length(btrim(provider_identifier)) > 0 AND length(btrim(model_identifier)) > 0 AND length(btrim(prompt_version)) > 0 AND started_at IS NOT NULL AND completed_at IS NOT NULL AND latency_milliseconds >= 0 AND length(btrim(sanitized_failure_category)) > 0)");
                    table.CheckConstraint("ck_ai_executions_status", "status IN ('pending', 'running', 'succeeded', 'failed', 'rejected')");
                    table.CheckConstraint("ck_ai_executions_timestamps", "(started_at IS NULL OR started_at >= created_at) AND (completed_at IS NULL OR completed_at >= started_at)");
                    table.ForeignKey(
                        name: "fk_ai_executions_analysis_request",
                        column: x => x.analysis_request_id,
                        principalSchema: "ai",
                        principalTable: "ai_analysis_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_uploaded_documents",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analysis_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_uploaded_documents", x => x.id);
                    table.CheckConstraint("ck_ai_uploaded_documents_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_ai_uploaded_documents_lifecycle", "(status = 'active' AND deleted_at IS NULL) OR (status = 'deleted' AND deleted_at >= created_at) OR (status = 'expired' AND deleted_at >= expires_at)");
                    table.CheckConstraint("ck_ai_uploaded_documents_size", "size_bytes > 0");
                    table.CheckConstraint("ck_ai_uploaded_documents_status", "status IN ('active', 'deleted', 'expired')");
                    table.ForeignKey(
                        name: "fk_ai_uploaded_documents_account",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_uploaded_documents_analysis_request",
                        column: x => x.analysis_request_id,
                        principalSchema: "ai",
                        principalTable: "ai_analysis_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_uploaded_documents_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_result_snapshots",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    result_schema_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_result_snapshots", x => x.id);
                    table.UniqueConstraint("ak_ai_result_snapshots_id_execution", x => new { x.id, x.execution_id });
                    table.CheckConstraint("ck_ai_result_snapshots_content", "jsonb_typeof(content) = 'object'");
                    table.CheckConstraint("ck_ai_result_snapshots_schema", "length(btrim(result_schema_version)) > 0");
                    table.CheckConstraint("ck_ai_result_snapshots_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_ai_result_snapshots_analysis_request",
                        column: x => x.analysis_request_id,
                        principalSchema: "ai",
                        principalTable: "ai_analysis_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_result_snapshots_execution_analysis",
                        columns: x => new { x.execution_id, x.analysis_request_id },
                        principalSchema: "ai",
                        principalTable: "ai_executions",
                        principalColumns: new[] { "id", "analysis_request_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_safety_validations",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_content_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    display_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    restricted_audit_output = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_safety_validations", x => x.id);
                    table.CheckConstraint("ck_ai_safety_validations_category", "category IN ('approved', 'unsafe_medical_advice', 'diagnosis', 'prescription', 'unsupported', 'malformed')");
                    table.CheckConstraint("ck_ai_safety_validations_display", "(category = 'approved' AND display_eligible AND result_snapshot_id IS NOT NULL AND restricted_audit_output IS NULL) OR (category <> 'approved' AND NOT display_eligible AND result_snapshot_id IS NULL AND length(btrim(restricted_audit_output)) > 0)");
                    table.CheckConstraint("ck_ai_safety_validations_policy", "length(btrim(policy_version)) > 0 AND (product_content_version IS NULL OR length(btrim(product_content_version)) > 0)");
                    table.ForeignKey(
                        name: "fk_ai_safety_validations_execution",
                        column: x => x.execution_id,
                        principalSchema: "ai",
                        principalTable: "ai_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_safety_validations_result_execution",
                        columns: x => new { x.result_snapshot_id, x.execution_id },
                        principalSchema: "ai",
                        principalTable: "ai_result_snapshots",
                        principalColumns: new[] { "id", "execution_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_analysis_requests_account_created_id",
                schema: "ai",
                table: "ai_analysis_requests",
                columns: new[] { "account_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_analysis_requests_conversation",
                schema: "ai",
                table: "ai_analysis_requests",
                column: "conversation_id",
                filter: "conversation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_analysis_requests_patient_created_id",
                schema: "ai",
                table: "ai_analysis_requests",
                columns: new[] { "patient_profile_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "patient_profile_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_conversations_account_created_id",
                schema: "ai",
                table: "ai_conversations",
                columns: new[] { "account_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_conversations_patient_created_id",
                schema: "ai",
                table: "ai_conversations",
                columns: new[] { "patient_profile_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "patient_profile_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_executions_analysis_created_id",
                schema: "ai",
                table: "ai_executions",
                columns: new[] { "analysis_request_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_executions_status_created_id",
                schema: "ai",
                table: "ai_executions",
                columns: new[] { "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_messages_conversation_sequence",
                schema: "ai",
                table: "ai_messages",
                columns: new[] { "conversation_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_result_snapshots_analysis_created_id",
                schema: "ai",
                table: "ai_result_snapshots",
                columns: new[] { "analysis_request_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_result_snapshots_execution_analysis",
                schema: "ai",
                table: "ai_result_snapshots",
                columns: new[] { "execution_id", "analysis_request_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_result_snapshots_analysis_sequence",
                schema: "ai",
                table: "ai_result_snapshots",
                columns: new[] { "analysis_request_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ai_result_snapshots_execution",
                schema: "ai",
                table: "ai_result_snapshots",
                column: "execution_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_safety_validations_result_execution",
                schema: "ai",
                table: "ai_safety_validations",
                columns: new[] { "result_snapshot_id", "execution_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_safety_validations_execution",
                schema: "ai",
                table: "ai_safety_validations",
                column: "execution_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ai_safety_validations_result_snapshot",
                schema: "ai",
                table: "ai_safety_validations",
                column: "result_snapshot_id",
                unique: true,
                filter: "result_snapshot_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_uploaded_documents_account_created_id",
                schema: "ai",
                table: "ai_uploaded_documents",
                columns: new[] { "account_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_uploaded_documents_analysis_request",
                schema: "ai",
                table: "ai_uploaded_documents",
                column: "analysis_request_id",
                filter: "analysis_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_uploaded_documents_patient_created_id",
                schema: "ai",
                table: "ai_uploaded_documents",
                columns: new[] { "patient_profile_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "patient_profile_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_uploaded_documents_status_expiry_id",
                schema: "ai",
                table: "ai_uploaded_documents",
                columns: new[] { "status", "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_uploaded_documents_storage_key",
                schema: "ai",
                table: "ai_uploaded_documents",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_messages",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ai_safety_validations",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ai_uploaded_documents",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ai_result_snapshots",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ai_executions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ai_analysis_requests",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "ai_conversations",
                schema: "ai");
        }
    }
}
