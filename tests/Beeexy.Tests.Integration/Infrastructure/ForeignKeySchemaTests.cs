using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ForeignKeySchemaTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task PersistenceForeignKeys_ExistAndUseRestrictedDeleteBehavior()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tc.constraint_name, rc.delete_rule " +
            "FROM information_schema.table_constraints tc " +
            "JOIN information_schema.referential_constraints rc " +
            "ON rc.constraint_catalog = tc.constraint_catalog " +
            "AND rc.constraint_schema = tc.constraint_schema " +
            "AND rc.constraint_name = tc.constraint_name " +
            "WHERE tc.constraint_type = 'FOREIGN KEY' " +
            "AND tc.table_schema IN ('identity', 'patients', 'triage') " +
            "ORDER BY tc.constraint_name;";

        var foreignKeys = new List<(string Name, string DeleteRule)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Equal(
            [
                "fk_answers_pre_triage_episodes_episode_version",
                "fk_answers_pre_triage_sessions_session_version",
                "fk_answers_questions_question_version",
                "fk_care_relationships_accounts_created_by_account_id",
                "fk_care_relationships_accounts_revoked_by_account_id",
                "fk_care_relationships_patient_profiles_manager_profile_id",
                "fk_care_relationships_patient_profiles_subject_profile_id",
                "fk_clinical_assessments_episodes_episode_rule_set_version",
                "fk_clinical_assessments_rule_set_versions_version_id",
                "fk_clinical_findings_clinical_assessments_assessment_id",
                "fk_external_identities_accounts_account_id",
                "fk_patient_profiles_accounts_account_id",
                "fk_pre_triage_episodes_patient_profiles_patient_profile_id",
                "fk_pre_triage_episodes_questionnaire_versions_version_id",
                "fk_pre_triage_episodes_rule_set_versions_version_id",
                "fk_pre_triage_episodes_sessions_source_session_version",
                "fk_pre_triage_sessions_patient_profiles_patient_profile_id",
                "fk_pre_triage_sessions_questionnaire_versions_version_id",
                "fk_questions_questionnaire_versions_questionnaire_version_id",
                "fk_refresh_sessions_accounts_account_id",
                "fk_reported_symptoms_pre_triage_episodes_episode_id",
                "fk_reported_symptoms_pre_triage_sessions_session_id",
                "fk_user_preferences_accounts_account_id"
            ],
            foreignKeys.Select(foreignKey => foreignKey.Name));
        Assert.Equal(
            [
                "fk_answers_pre_triage_sessions_session_version",
                "fk_reported_symptoms_pre_triage_sessions_session_id"
            ],
            foreignKeys
                .Where(foreignKey => foreignKey.DeleteRule == "CASCADE")
                .Select(foreignKey => foreignKey.Name));
        Assert.All(
            foreignKeys.Where(foreignKey => foreignKey.DeleteRule != "CASCADE"),
            foreignKey => Assert.Equal("RESTRICT", foreignKey.DeleteRule));
    }
}
