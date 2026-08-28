using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseBackedPrivateAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "private_access_login_windows",
                schema: "identity",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    window_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_private_access_login_windows", x => x.key_hash);
                    table.CheckConstraint("ck_private_access_login_windows_attempt_count", "attempt_count > 0");
                });

            migrationBuilder.CreateTable(
                name: "private_access_credentials",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tester_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    keyword_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_private_access_credentials", x => x.id);
                    table.CheckConstraint("ck_private_access_credentials_status", "status IN ('active','disabled','revoked')");
                    table.CheckConstraint("ck_private_access_credentials_timestamps", "(status = 'active' AND disabled_at IS NULL AND revoked_at IS NULL) OR (status = 'disabled' AND disabled_at IS NOT NULL AND revoked_at IS NULL) OR (status = 'revoked' AND disabled_at IS NULL AND revoked_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_private_access_credentials_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "private_access_sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_refresh_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_private_access_sessions", x => x.id);
                    table.CheckConstraint("ck_private_access_sessions_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_private_access_sessions_revoked", "(status = 'revoked' AND revoked_at IS NOT NULL) OR (status <> 'revoked' AND revoked_at IS NULL)");
                    table.CheckConstraint("ck_private_access_sessions_status", "status IN ('active','revoked','expired')");
                    table.ForeignKey(
                        name: "fk_private_access_sessions_credentials_credential_id",
                        column: x => x.credential_id,
                        principalSchema: "identity",
                        principalTable: "private_access_credentials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_private_access_sessions_refresh_sessions_root_id",
                        column: x => x.root_refresh_session_id,
                        principalSchema: "identity",
                        principalTable: "refresh_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_private_access_credentials_account_id",
                schema: "identity",
                table: "private_access_credentials",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_private_access_credentials_tester_key",
                schema: "identity",
                table: "private_access_credentials",
                column: "tester_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_private_access_credentials_username",
                schema: "identity",
                table: "private_access_credentials",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_private_access_sessions_active_credential_expiry",
                schema: "identity",
                table: "private_access_sessions",
                columns: new[] { "credential_id", "expires_at" },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_private_access_sessions_root_refresh_session_id",
                schema: "identity",
                table: "private_access_sessions",
                column: "root_refresh_session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_private_access_sessions_token_hash",
                schema: "identity",
                table: "private_access_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "private_access_login_windows",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "private_access_sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "private_access_credentials",
                schema: "identity");
        }
    }
}
