using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase82AvailabilityInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demo_availability_imports",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demo_availability_imports", x => x.id);
                    table.CheckConstraint("ck_demo_availability_imports_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_demo_availability_imports_package_code", "length(btrim(package_code)) > 0");
                    table.CheckConstraint("ck_demo_availability_imports_version", "length(btrim(version)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_demo_availability_imports_package_version_reference_date",
                schema: "scheduling",
                table: "demo_availability_imports",
                columns: new[] { "package_code", "version", "reference_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_availability_imports",
                schema: "scheduling");
        }
    }
}
