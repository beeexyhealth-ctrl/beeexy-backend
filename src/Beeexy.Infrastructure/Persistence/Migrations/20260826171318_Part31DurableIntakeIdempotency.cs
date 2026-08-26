using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Part31DurableIntakeIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pre_triage_intake_idempotency",
                schema: "triage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                    reservation_alias_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: true),
                    request_fingerprint = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initial_answer_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pre_triage_intake_idempotency", x => x.id);
                    table.CheckConstraint("ck_pre_triage_intake_idempotency_timestamps", "completed_at >= created_at");
                    table.ForeignKey(
                        name: "fk_pre_triage_intake_idempotency_pre_triage_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "triage",
                        principalTable: "pre_triage_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_pre_triage_intake_idempotency_operation_key_hash",
                schema: "triage",
                table: "pre_triage_intake_idempotency",
                column: "operation_key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pre_triage_intake_idempotency_reservation_alias_hash",
                schema: "triage",
                table: "pre_triage_intake_idempotency",
                column: "reservation_alias_hash",
                unique: true,
                filter: "reservation_alias_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pre_triage_intake_idempotency_session_id",
                schema: "triage",
                table: "pre_triage_intake_idempotency",
                column: "session_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_triage_intake_idempotency",
                schema: "triage");
        }
    }
}
