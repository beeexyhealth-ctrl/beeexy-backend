using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase26ProfileOptimisticConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "patients",
                table: "user_preferences",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_preferences_version_positive",
                schema: "patients",
                table: "user_preferences",
                sql: "version > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_preferences_version_positive",
                schema: "patients",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "patients",
                table: "user_preferences");
        }
    }
}
