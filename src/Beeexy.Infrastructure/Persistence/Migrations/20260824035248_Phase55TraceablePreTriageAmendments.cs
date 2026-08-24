using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase55TraceablePreTriageAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "idempotency_key",
                schema: "history",
                table: "clinical_amendments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_clinical_amendments_event_idempotency_key",
                schema: "history",
                table: "clinical_amendments",
                columns: new[] { "clinical_history_event_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_clinical_amendments_event_idempotency_key",
                schema: "history",
                table: "clinical_amendments");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "history",
                table: "clinical_amendments");
        }
    }
}
