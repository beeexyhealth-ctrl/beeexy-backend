using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task AllMigrations_ApplyToFreshPostgreSqlThroughPart31Idempotency()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

            Assert.Equal(
                [
                    "20260819193818_InitialFoundation",
                    "20260819214410_Phase21IdentityPersistenceFoundation",
                    "20260820015208_Phase24RefreshSessionRotation",
                    "20260820053544_Phase26ProfileOptimisticConcurrency",
                    "20260821015511_Phase31CareRelationshipFoundation",
                    "20260821065021_Phase36ApprovedPatientDemographics",
                    "20260821203135_Phase41PreTriagePersistenceFoundation",
                    "20260822035009_Phase42ClinicalDefinitionPackages",
                    "20260822061610_Phase45ConfirmedDemoPackages",
                    "20260822163355_Phase47NeutralClinicalAssessment",
                    "20260822182341_Phase410ClinicalHistoryProjectionBoundary",
                    "20260823192326_Phase51ClinicalHistoryFoundation",
                    "20260824035248_Phase55TraceablePreTriageAmendments",
                    "20260824202650_Phase61FhirExportPersistenceFoundation",
                    "20260826171318_Part31DurableIntakeIdempotency"
                ],
                appliedMigrations);
            Assert.Empty(pendingMigrations);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_schema || '.' || table_name " +
            "FROM information_schema.tables " +
            "WHERE table_type = 'BASE TABLE' " +
            "AND table_schema NOT IN ('pg_catalog', 'information_schema') " +
            "ORDER BY 1;";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "history.clinical_amendments",
                "history.clinical_history_events",
                "history.pre_triage_projection_records",
                "identity.accounts",
                "identity.email_authentication_challenges",
                "identity.external_identities",
                "identity.refresh_sessions",
                "interoperability.fhir_exports",
                "interoperability.fhir_validation_results",
                "patients.care_relationships",
                "patients.patient_profiles",
                "patients.user_preferences",
                "public.__EFMigrationsHistory",
                "triage.answers",
                "triage.clinical_assessments",
                "triage.clinical_findings",
                "triage.clinical_rule_set_versions",
                "triage.pre_triage_episodes",
                "triage.pre_triage_intake_idempotency",
                "triage.pre_triage_sessions",
                "triage.questionnaire_versions",
                "triage.questions",
                "triage.reported_symptoms"
            ],
            tables);
    }
}
