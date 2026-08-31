using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase81SchedulingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "availability_slots",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clinic_timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_slots", x => x.id);
                    table.CheckConstraint("ck_availability_slots_modality", "modality IN ('in_person', 'virtual')");
                    table.CheckConstraint("ck_availability_slots_time_range", "ends_at > starts_at");
                    table.CheckConstraint("ck_availability_slots_timezone", "length(btrim(clinic_timezone)) > 0");
                    table.ForeignKey(
                        name: "fk_availability_slots_clinic_locations",
                        columns: x => new { x.clinic_id, x.clinic_location_id },
                        principalSchema: "directory",
                        principalTable: "clinic_locations",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_availability_slots_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "directory",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_availability_slots_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalSchema: "directory",
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    availability_slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requesting_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_modality", "modality IN ('in_person', 'virtual')");
                    table.CheckConstraint("ck_appointments_reason", "reason IS NULL OR (length(btrim(reason)) > 0 AND length(reason) <= 500)");
                    table.CheckConstraint("ck_appointments_request_fingerprint", "request_fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_appointments_status", "status IN ('requested','confirmed','cancelled','completed','no_show','rejected')");
                    table.CheckConstraint("ck_appointments_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_appointments_accounts_requesting_account_id",
                        column: x => x.requesting_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_availability_slots_slot_id",
                        column: x => x.availability_slot_id,
                        principalSchema: "scheduling",
                        principalTable: "availability_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_patient_profiles_patient_profile_id",
                        column: x => x.patient_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_reschedule_history",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_reschedule_history", x => x.id);
                    table.CheckConstraint("ck_appointment_reschedule_history_distinct_slots", "previous_slot_id <> new_slot_id");
                    table.ForeignKey(
                        name: "fk_appointment_reschedule_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_reschedule_history_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_reschedule_history_new_slot_id",
                        column: x => x.new_slot_id,
                        principalSchema: "scheduling",
                        principalTable: "availability_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_reschedule_history_previous_slot_id",
                        column: x => x.previous_slot_id,
                        principalSchema: "scheduling",
                        principalTable: "availability_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_history",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    new_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_history", x => x.id);
                    table.CheckConstraint("ck_appointment_status_history_action", "action IN ('creation','confirmation','rejection','cancellation','completion','no_show')");
                    table.CheckConstraint("ck_appointment_status_history_actor_type", "actor_type IN ('patient_authority','appointment_scheduler')");
                    table.CheckConstraint("ck_appointment_status_history_creation_semantics", "(action = 'creation' AND sequence = 1 AND previous_status IS NULL AND new_status = 'requested') OR (action <> 'creation' AND sequence > 1 AND previous_status IS NOT NULL AND previous_status <> new_status)");
                    table.CheckConstraint("ck_appointment_status_history_new_status", "new_status IN ('requested','confirmed','cancelled','completed','no_show','rejected')");
                    table.CheckConstraint("ck_appointment_status_history_previous_status", "previous_status IS NULL OR previous_status IN ('requested','confirmed','cancelled','completed','no_show','rejected')");
                    table.ForeignKey(
                        name: "fk_appointment_status_history_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reschedule_history_actor_account_id",
                schema: "scheduling",
                table: "appointment_reschedule_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reschedule_history_appointment_occurred_id",
                schema: "scheduling",
                table: "appointment_reschedule_history",
                columns: new[] { "appointment_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reschedule_history_new_slot_id",
                schema: "scheduling",
                table: "appointment_reschedule_history",
                column: "new_slot_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reschedule_history_previous_slot_id",
                schema: "scheduling",
                table: "appointment_reschedule_history",
                column: "previous_slot_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_actor_account_id",
                schema: "scheduling",
                table: "appointment_status_history",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_appointment_status_history_appointment_sequence",
                schema: "scheduling",
                table: "appointment_status_history",
                columns: new[] { "appointment_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_start_status",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "patient_profile_id", "scheduled_start_at", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_slot_status",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "availability_slot_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_status",
                schema: "scheduling",
                table: "appointments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_appointments_account_idempotency_key",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "requesting_account_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_appointments_reserving_slot",
                schema: "scheduling",
                table: "appointments",
                column: "availability_slot_id",
                unique: true,
                filter: "status IN ('requested', 'confirmed')");

            migrationBuilder.CreateIndex(
                name: "ix_availability_slots_clinic_published_start",
                schema: "scheduling",
                table: "availability_slots",
                columns: new[] { "clinic_id", "is_published", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_availability_slots_doctor_published_start",
                schema: "scheduling",
                table: "availability_slots",
                columns: new[] { "doctor_id", "is_published", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_availability_slots_location_start",
                schema: "scheduling",
                table: "availability_slots",
                columns: new[] { "clinic_id", "clinic_location_id", "starts_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_reschedule_history",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointment_status_history",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "availability_slots",
                schema: "scheduling");
        }
    }
}
