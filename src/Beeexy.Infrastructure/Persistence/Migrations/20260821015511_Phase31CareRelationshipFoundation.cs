using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beeexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase31CareRelationshipFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_relationships",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attestation_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_relationships", x => x.id);
                    table.CheckConstraint("ck_care_relationships_attestation_timestamp", "attested_at <= created_at");
                    table.CheckConstraint("ck_care_relationships_attestation_version", "length(btrim(attestation_version)) > 0");
                    table.CheckConstraint("ck_care_relationships_distinct_profiles", "manager_profile_id <> subject_profile_id");
                    table.CheckConstraint("ck_care_relationships_revocation", "(status = 'active' AND revoked_at IS NULL AND revoked_by_account_id IS NULL AND updated_at IS NULL) OR (status = 'revoked' AND revoked_at IS NOT NULL AND revoked_by_account_id IS NOT NULL AND updated_at = revoked_at)");
                    table.CheckConstraint("ck_care_relationships_revocation_timestamp", "revoked_at IS NULL OR revoked_at >= created_at");
                    table.CheckConstraint("ck_care_relationships_status", "status IN ('active', 'revoked')");
                    table.CheckConstraint("ck_care_relationships_type", "relationship_type IN ('parent', 'legal_guardian', 'caregiver', 'spouse', 'child', 'sibling', 'other')");
                    table.ForeignKey(
                        name: "fk_care_relationships_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_relationships_accounts_revoked_by_account_id",
                        column: x => x.revoked_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_relationships_patient_profiles_manager_profile_id",
                        column: x => x.manager_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_relationships_patient_profiles_subject_profile_id",
                        column: x => x.subject_profile_id,
                        principalSchema: "patients",
                        principalTable: "patient_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_relationships_created_by_account_id",
                schema: "patients",
                table: "care_relationships",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_relationships_manager_status",
                schema: "patients",
                table: "care_relationships",
                columns: new[] { "manager_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_care_relationships_revoked_by_account_id",
                schema: "patients",
                table: "care_relationships",
                column: "revoked_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_relationships_subject_status",
                schema: "patients",
                table: "care_relationships",
                columns: new[] { "subject_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_care_relationships_active_manager_subject",
                schema: "patients",
                table: "care_relationships",
                columns: new[] { "manager_profile_id", "subject_profile_id" },
                unique: true,
                filter: "status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_relationships",
                schema: "patients");
        }
    }
}
