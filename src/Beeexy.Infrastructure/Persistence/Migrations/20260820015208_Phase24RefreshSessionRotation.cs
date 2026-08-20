using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase24RefreshSessionRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "family_id",
                schema: "identity",
                table: "refresh_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE identity.refresh_sessions SET family_id = id WHERE family_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "family_id",
                schema: "identity",
                table: "refresh_sessions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_session_id",
                schema: "identity",
                table: "refresh_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_session_id",
                schema: "identity",
                table: "refresh_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rotated_at",
                schema: "identity",
                table: "refresh_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_sessions_family_id",
                schema: "identity",
                table: "refresh_sessions",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_sessions_parent_session_id",
                schema: "identity",
                table: "refresh_sessions",
                column: "parent_session_id",
                unique: true,
                filter: "\"parent_session_id\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_sessions_rotation",
                schema: "identity",
                table: "refresh_sessions",
                sql: "(\"rotated_at\" IS NULL AND \"replaced_by_session_id\" IS NULL) OR (\"rotated_at\" IS NOT NULL AND \"replaced_by_session_id\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_sessions_family_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropIndex(
                name: "ux_refresh_sessions_parent_session_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_sessions_rotation",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "family_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "parent_session_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "replaced_by_session_id",
                schema: "identity",
                table: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "rotated_at",
                schema: "identity",
                table: "refresh_sessions");
        }
    }
}
