using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EducationalVideoOfferWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "educational_video_decision",
                schema: "triage",
                table: "pre_triage_sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "educational_video_offer_required",
                schema: "triage",
                table: "pre_triage_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "educational_video_offer_resolved_at",
                schema: "triage",
                table: "pre_triage_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pre_triage_sessions_educational_video_decision",
                schema: "triage",
                table: "pre_triage_sessions",
                sql: "(educational_video_decision IS NULL AND educational_video_offer_resolved_at IS NULL) OR (educational_video_offer_required = TRUE AND educational_video_decision IN ('watch', 'skip') AND educational_video_offer_resolved_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pre_triage_sessions_educational_video_decision",
                schema: "triage",
                table: "pre_triage_sessions");

            migrationBuilder.DropColumn(
                name: "educational_video_decision",
                schema: "triage",
                table: "pre_triage_sessions");

            migrationBuilder.DropColumn(
                name: "educational_video_offer_required",
                schema: "triage",
                table: "pre_triage_sessions");

            migrationBuilder.DropColumn(
                name: "educational_video_offer_resolved_at",
                schema: "triage",
                table: "pre_triage_sessions");
        }
    }
}
