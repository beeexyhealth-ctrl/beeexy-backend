using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8OpsAppointmentAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_appointment_status_history_actor_type",
                schema: "scheduling",
                table: "appointment_status_history");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_account_id",
                schema: "scheduling",
                table: "appointment_status_history",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "operational_actor_identifier",
                schema: "scheduling",
                table: "appointment_status_history",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointment_status_history_actor_identity",
                schema: "scheduling",
                table: "appointment_status_history",
                sql: "(actor_type = 'beeexy_operations' AND actor_account_id IS NULL AND operational_actor_identifier IS NOT NULL AND length(btrim(operational_actor_identifier)) > 0) OR (actor_type <> 'beeexy_operations' AND actor_account_id IS NOT NULL AND operational_actor_identifier IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointment_status_history_actor_type",
                schema: "scheduling",
                table: "appointment_status_history",
                sql: "actor_type IN ('patient_authority','appointment_scheduler','beeexy_operations')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_appointment_status_history_actor_identity",
                schema: "scheduling",
                table: "appointment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointment_status_history_actor_type",
                schema: "scheduling",
                table: "appointment_status_history");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_account_id",
                schema: "scheduling",
                table: "appointment_status_history",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "operational_actor_identifier",
                schema: "scheduling",
                table: "appointment_status_history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointment_status_history_actor_type",
                schema: "scheduling",
                table: "appointment_status_history",
                sql: "actor_type IN ('patient_authority','appointment_scheduler')");
        }
    }
}
