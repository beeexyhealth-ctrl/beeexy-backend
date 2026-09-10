using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase112ShareCreationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "idempotency_key",
                schema: "sharing",
                table: "share_grants",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                schema: "sharing",
                table: "share_grants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "repeat('0', 64)");

            migrationBuilder.CreateIndex(
                name: "ux_share_grants_patient_idempotency_key",
                schema: "sharing",
                table: "share_grants",
                columns: new[] { "patient_profile_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_share_grants_request_fingerprint",
                schema: "sharing",
                table: "share_grants",
                sql: "request_fingerprint ~ '^[0-9a-f]{64}$'");

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION sharing.protect_share_grant_history()
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
                        OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
                        OR NEW.request_fingerprint IS DISTINCT FROM OLD.request_fingerprint
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION sharing.protect_share_grant_history()
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
                """);

            migrationBuilder.DropIndex(
                name: "ux_share_grants_patient_idempotency_key",
                schema: "sharing",
                table: "share_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_share_grants_request_fingerprint",
                schema: "sharing",
                table: "share_grants");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "sharing",
                table: "share_grants");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                schema: "sharing",
                table: "share_grants");
        }
    }
}
