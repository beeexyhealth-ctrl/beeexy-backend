using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase111SharingPersistenceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sharing");

            migrationBuilder.CreateTable(
                name: "export_artifacts",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    media_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    checksum_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    checksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    private_storage_identity = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    failure_category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retention_eligible_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_artifacts", x => x.id);
                    table.CheckConstraint("ck_export_artifacts_checksum", "(checksum_algorithm IS NULL AND checksum IS NULL) OR (checksum_algorithm !~ '[[:space:]]' AND checksum !~ '[[:space:]]')");
                    table.CheckConstraint("ck_export_artifacts_format", "format IN ('beeexy_json', 'pdf', 'fhir_json')");
                    table.CheckConstraint("ck_export_artifacts_lifecycle", "(status = 'pending' AND checksum_algorithm IS NULL AND checksum IS NULL AND private_storage_identity IS NULL AND failure_category IS NULL AND completed_at IS NULL AND failed_at IS NULL AND deleted_at IS NULL) OR (status = 'available' AND length(btrim(checksum_algorithm)) > 0 AND length(btrim(checksum)) > 0 AND length(btrim(private_storage_identity)) > 0 AND failure_category IS NULL AND completed_at >= created_at AND failed_at IS NULL AND deleted_at IS NULL) OR (status = 'failed' AND checksum_algorithm IS NULL AND checksum IS NULL AND private_storage_identity IS NULL AND length(btrim(failure_category)) > 0 AND completed_at IS NULL AND failed_at >= created_at AND deleted_at IS NULL) OR (status = 'deleted' AND length(btrim(checksum_algorithm)) > 0 AND length(btrim(checksum)) > 0 AND length(btrim(private_storage_identity)) > 0 AND failure_category IS NULL AND completed_at >= created_at AND failed_at IS NULL AND deleted_at >= retention_eligible_at)");
                    table.CheckConstraint("ck_export_artifacts_media_type", "media_type ~ '^[^[:space:]/]+/[^[:space:]/]+$'");
                    table.CheckConstraint("ck_export_artifacts_retention", "retention_eligible_at > created_at");
                    table.CheckConstraint("ck_export_artifacts_snapshot_version", "length(btrim(snapshot_version)) > 0");
                    table.CheckConstraint("ck_export_artifacts_status", "status IN ('pending', 'available', 'failed', 'deleted')");
                    table.CheckConstraint("ck_export_artifacts_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_export_artifacts_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_export_artifacts_requester_account",
                        column: x => x.requested_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "share_grants",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    capability_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_grants", x => x.id);
                    table.CheckConstraint("ck_share_grants_capability_hash", "length(btrim(capability_hash)) >= 32 AND capability_hash !~ '[[:space:]]'");
                    table.CheckConstraint("ck_share_grants_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_share_grants_revocation", "(revoked_at IS NULL AND revoked_by_account_id IS NULL) OR (revoked_at >= created_at AND revoked_by_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_share_grants_scope", "scope IN ('full_profile', 'case', 'pre_triage', 'visit', 'specific_records')");
                    table.CheckConstraint("ck_share_grants_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_share_grants_creator_account",
                        column: x => x.creator_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_share_grants_patient_profile",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_share_grants_revoker_account",
                        column: x => x.revoked_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "share_access_events",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_access_events", x => x.id);
                    table.CheckConstraint("ck_share_access_events_outcome", "outcome IN ('succeeded', 'denied')");
                    table.CheckConstraint("ck_share_access_events_resource_type", "resource_type IS NULL OR resource_type ~ '^[a-z][a-z0-9_]*$'");
                    table.CheckConstraint("ck_share_access_events_type", "event_type IN ('share_created', 'share_accessed', 'share_downloaded', 'share_revoked', 'share_expired')");
                    table.ForeignKey(
                        name: "fk_share_access_events_share_grant",
                        column: x => x.share_grant_id,
                        principalSchema: "sharing",
                        principalTable: "share_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "share_grant_items",
                schema: "sharing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_grant_items", x => x.id);
                    table.CheckConstraint("ck_share_grant_items_resource_type", "resource_type ~ '^[a-z][a-z0-9_]*$'");
                    table.ForeignKey(
                        name: "fk_share_grant_items_share_grant",
                        column: x => x.share_grant_id,
                        principalSchema: "sharing",
                        principalTable: "share_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_export_artifacts_patient_created_id",
                schema: "sharing",
                table: "export_artifacts",
                columns: new[] { "patient_profile_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_export_artifacts_requester_account",
                schema: "sharing",
                table: "export_artifacts",
                column: "requested_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_export_artifacts_status_retention_id",
                schema: "sharing",
                table: "export_artifacts",
                columns: new[] { "status", "retention_eligible_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_export_artifacts_patient_idempotency_key",
                schema: "sharing",
                table: "export_artifacts",
                columns: new[] { "patient_profile_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_export_artifacts_private_storage_identity",
                schema: "sharing",
                table: "export_artifacts",
                column: "private_storage_identity",
                unique: true,
                filter: "private_storage_identity IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_share_access_events_grant_occurred_id",
                schema: "sharing",
                table: "share_access_events",
                columns: new[] { "share_grant_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_share_grant_items_grant_resource",
                schema: "sharing",
                table: "share_grant_items",
                columns: new[] { "share_grant_id", "resource_type", "resource_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_share_grants_active_expiry_id",
                schema: "sharing",
                table: "share_grants",
                columns: new[] { "expires_at", "id" },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_share_grants_creator_account",
                schema: "sharing",
                table: "share_grants",
                column: "creator_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_grants_patient_created_id",
                schema: "sharing",
                table: "share_grants",
                columns: new[] { "patient_profile_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_share_grants_revoker_account",
                schema: "sharing",
                table: "share_grants",
                column: "revoked_by_account_id",
                filter: "revoked_by_account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_share_grants_capability_hash",
                schema: "sharing",
                table: "share_grants",
                column: "capability_hash",
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION sharing.reject_share_append_only_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'Share items and access events are append-only.'
                        USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER trg_share_grant_items_append_only
                    BEFORE UPDATE OR DELETE ON sharing.share_grant_items
                    FOR EACH ROW EXECUTE FUNCTION sharing.reject_share_append_only_mutation();
                CREATE TRIGGER trg_share_access_events_append_only
                    BEFORE UPDATE OR DELETE ON sharing.share_access_events
                    FOR EACH ROW EXECUTE FUNCTION sharing.reject_share_append_only_mutation();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION sharing.protect_share_grant_history()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Share grants are permanent lifecycle records.'
                            USING ERRCODE = '55000';
                    END IF;

                    IF NEW.id IS DISTINCT FROM OLD.id
                        OR NEW.patient_profile_id IS DISTINCT FROM OLD.patient_profile_id
                        OR NEW.creator_account_id IS DISTINCT FROM OLD.creator_account_id
                        OR NEW.scope IS DISTINCT FROM OLD.scope
                        OR NEW.capability_hash IS DISTINCT FROM OLD.capability_hash
                        OR NEW.created_at IS DISTINCT FROM OLD.created_at
                        OR NEW.expires_at IS DISTINCT FROM OLD.expires_at THEN
                        RAISE EXCEPTION 'Immutable share grant metadata cannot be changed.'
                            USING ERRCODE = '55000';
                    END IF;

                    IF OLD.revoked_at IS NOT NULL
                        OR NEW.revoked_at IS NULL
                        OR NEW.revoked_by_account_id IS NULL
                        OR NEW.version <> OLD.version + 1 THEN
                        RAISE EXCEPTION 'Only the first valid share revocation transition is allowed.'
                            USING ERRCODE = '55000';
                    END IF;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_share_grants_protect_history
                    BEFORE UPDATE OR DELETE ON sharing.share_grants
                    FOR EACH ROW EXECUTE FUNCTION sharing.protect_share_grant_history();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION sharing.protect_export_artifact_history()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Export artifact metadata is a permanent lifecycle record.'
                            USING ERRCODE = '55000';
                    END IF;

                    IF NEW.id IS DISTINCT FROM OLD.id
                        OR NEW.patient_profile_id IS DISTINCT FROM OLD.patient_profile_id
                        OR NEW.requested_by_account_id IS DISTINCT FROM OLD.requested_by_account_id
                        OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
                        OR NEW.format IS DISTINCT FROM OLD.format
                        OR NEW.media_type IS DISTINCT FROM OLD.media_type
                        OR NEW.snapshot_id IS DISTINCT FROM OLD.snapshot_id
                        OR NEW.snapshot_version IS DISTINCT FROM OLD.snapshot_version
                        OR NEW.created_at IS DISTINCT FROM OLD.created_at
                        OR NEW.retention_eligible_at IS DISTINCT FROM OLD.retention_eligible_at
                        OR NEW.version <> OLD.version + 1 THEN
                        RAISE EXCEPTION 'Immutable export artifact metadata cannot be changed.'
                            USING ERRCODE = '55000';
                    END IF;

                    IF OLD.status = 'pending' AND NEW.status IN ('available', 'failed') THEN
                        RETURN NEW;
                    END IF;

                    IF OLD.status = 'available' AND NEW.status = 'deleted'
                        AND NEW.checksum_algorithm IS NOT DISTINCT FROM OLD.checksum_algorithm
                        AND NEW.checksum IS NOT DISTINCT FROM OLD.checksum
                        AND NEW.private_storage_identity IS NOT DISTINCT FROM OLD.private_storage_identity
                        AND NEW.completed_at IS NOT DISTINCT FROM OLD.completed_at THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'The export artifact lifecycle transition is invalid.'
                        USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER trg_export_artifacts_protect_history
                    BEFORE UPDATE OR DELETE ON sharing.export_artifacts
                    FOR EACH ROW EXECUTE FUNCTION sharing.protect_export_artifact_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_artifacts",
                schema: "sharing");

            migrationBuilder.DropTable(
                name: "share_access_events",
                schema: "sharing");

            migrationBuilder.DropTable(
                name: "share_grant_items",
                schema: "sharing");

            migrationBuilder.DropTable(
                name: "share_grants",
                schema: "sharing");

            migrationBuilder.Sql(
                """
                DROP FUNCTION sharing.protect_export_artifact_history();
                DROP FUNCTION sharing.protect_share_grant_history();
                DROP FUNCTION sharing.reject_share_append_only_mutation();
                """);
        }
    }
}
