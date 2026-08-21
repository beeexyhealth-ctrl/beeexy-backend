using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class CareRelationshipPersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task CareRelationship_PersistsAndReloadsCreationAndRevocationMetadata()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(CareRelationshipType.LegalGuardian);

        await SaveAsync(graph.Account, graph.Manager, graph.Subject, graph.Relationship);

        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.CareRelationships
                .AsNoTracking()
                .SingleAsync(value => value.Id == graph.Relationship.Id);

            Assert.Equal(graph.Manager.Id, saved.ManagerProfileId);
            Assert.Equal(graph.Subject.Id, saved.SubjectProfileId);
            Assert.Equal(CareRelationshipType.LegalGuardian, saved.RelationshipType);
            Assert.Equal(CareRelationshipStatus.Active, saved.Status);
            Assert.Equal(graph.Account.Id, saved.CreatedByAccountId);
            Assert.Equal("phase-3.1-test", saved.Attestation.Version);
            Assert.Equal(UtcNow(), saved.Attestation.AttestedAt);
        }

        var revokedAt = UtcNow().AddMinutes(10);
        graph.Relationship.Revoke(graph.Account.Id, revokedAt);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.CareRelationships.Update(graph.Relationship);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.CareRelationships
                .AsNoTracking()
                .SingleAsync(value => value.Id == graph.Relationship.Id);
            Assert.Equal(CareRelationshipStatus.Revoked, saved.Status);
            Assert.Equal(revokedAt, saved.RevokedAt);
            Assert.Equal(graph.Account.Id, saved.RevokedByAccountId);
            Assert.Equal(revokedAt, saved.UpdatedAt);
        }
    }

    [Fact]
    public async Task CareRelationship_RequiresExistingManagerProfile()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var relationship = CreateRelationship(
            EntityId.New(),
            graph.Subject.Id,
            graph.Account.Id);

        await SaveAsync(graph.Account, graph.Subject);

        await AssertForeignKeyViolationAsync(
            () => SaveAsync(relationship),
            "fk_care_relationships_patient_profiles_manager_profile_id");
    }

    [Fact]
    public async Task CareRelationship_RequiresExistingSubjectProfile()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var relationship = CreateRelationship(
            graph.Manager.Id,
            EntityId.New(),
            graph.Account.Id);

        await SaveAsync(graph.Account, graph.Manager);

        await AssertForeignKeyViolationAsync(
            () => SaveAsync(relationship),
            "fk_care_relationships_patient_profiles_subject_profile_id");
    }

    [Fact]
    public async Task Database_RejectsSelfRelationship()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        await SaveAsync(graph.Account, graph.Manager);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRawRelationshipAsync(
                graph.Manager.Id,
                graph.Manager.Id,
                graph.Account.Id));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_care_relationships_distinct_profiles", exception.ConstraintName);
    }

    [Fact]
    public async Task Database_PreventsTwoActiveRelationshipsForManagerAndSubject()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var duplicate = CreateRelationship(
            graph.Manager.Id,
            graph.Subject.Id,
            graph.Account.Id,
            CareRelationshipType.Caregiver);

        await SaveAsync(graph.Account, graph.Manager, graph.Subject, graph.Relationship);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(duplicate));
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(
            "ux_care_relationships_active_manager_subject",
            postgresException.ConstraintName);
    }

    [Fact]
    public async Task RevokedHistory_CanCoexistWithNewActiveRelationship()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        graph.Relationship.Revoke(graph.Account.Id, UtcNow().AddMinutes(1));
        var replacement = CreateRelationship(
            graph.Manager.Id,
            graph.Subject.Id,
            graph.Account.Id,
            CareRelationshipType.Other,
            UtcNow().AddMinutes(2));

        await SaveAsync(
            graph.Account,
            graph.Manager,
            graph.Subject,
            graph.Relationship,
            replacement);

        await using var dbContext = CreateDbContext();
        var relationships = await dbContext.CareRelationships
            .AsNoTracking()
            .Where(value =>
                value.ManagerProfileId == graph.Manager.Id &&
                value.SubjectProfileId == graph.Subject.Id)
            .ToListAsync();

        Assert.Equal(2, relationships.Count);
        Assert.Single(relationships, value => value.Status == CareRelationshipStatus.Active);
        Assert.Single(relationships, value => value.Status == CareRelationshipStatus.Revoked);
    }

    [Fact]
    public async Task MultipleManagers_CanManageOneSubject()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var account = CreateAccount(suffix);
        var firstManager = CreateProfile($"BXY-MANAGER-A-{suffix}", account.Id);
        var secondManager = CreateProfile($"BXY-MANAGER-B-{suffix}");
        var subject = CreateProfile($"BXY-SUBJECT-{suffix}");
        var first = CreateRelationship(firstManager.Id, subject.Id, account.Id);
        var second = CreateRelationship(
            secondManager.Id,
            subject.Id,
            account.Id,
            CareRelationshipType.Sibling);

        await SaveAsync(account, firstManager, secondManager, subject, first, second);

        await using var dbContext = CreateDbContext();
        Assert.Equal(
            2,
            await dbContext.CareRelationships.CountAsync(value =>
                value.SubjectProfileId == subject.Id &&
                value.Status == CareRelationshipStatus.Active));
    }

    [Fact]
    public async Task OneManager_CanManageMultipleSubjects()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var account = CreateAccount(suffix);
        var manager = CreateProfile($"BXY-MANAGER-{suffix}", account.Id);
        var firstSubject = CreateProfile($"BXY-SUBJECT-A-{suffix}");
        var secondSubject = CreateProfile($"BXY-SUBJECT-B-{suffix}");
        var first = CreateRelationship(manager.Id, firstSubject.Id, account.Id);
        var second = CreateRelationship(
            manager.Id,
            secondSubject.Id,
            account.Id,
            CareRelationshipType.Child);

        await SaveAsync(account, manager, firstSubject, secondSubject, first, second);

        await using var dbContext = CreateDbContext();
        Assert.Equal(
            2,
            await dbContext.CareRelationships.CountAsync(value =>
                value.ManagerProfileId == manager.Id &&
                value.Status == CareRelationshipStatus.Active));
    }

    [Fact]
    public async Task Database_RequiresAttestationData()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        await SaveAsync(graph.Account, graph.Manager, graph.Subject);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO patients.care_relationships " +
            "(id, manager_profile_id, subject_profile_id, relationship_type, status, " +
            "created_by_account_id, attestation_version, attested_at, created_at) " +
            "VALUES (@id, @manager, @subject, 'parent', 'active', @creator, NULL, @now, @now);";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("manager", graph.Manager.Id.Value);
        command.Parameters.AddWithValue("subject", graph.Subject.Id.Value);
        command.Parameters.AddWithValue("creator", graph.Account.Id.Value);
        command.Parameters.AddWithValue("now", UtcNow());

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
        Assert.Equal("attestation_version", exception.ColumnName);
    }

    [Fact]
    public async Task Database_RejectsInconsistentStatusAndRevocationMetadata()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        await SaveAsync(graph.Account, graph.Manager, graph.Subject, graph.Relationship);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE patients.care_relationships SET status = 'revoked' WHERE id = @id;";
        command.Parameters.AddWithValue("id", graph.Relationship.Id.Value);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_care_relationships_revocation", exception.ConstraintName);
    }

    [Fact]
    public async Task Migration_CreatesRequiredCareRelationshipIndexes()
    {
        await EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname, indexdef FROM pg_indexes " +
            "WHERE schemaname = 'patients' AND tablename = 'care_relationships' " +
            "AND indexname IN ('ix_care_relationships_manager_status', " +
            "'ix_care_relationships_subject_status', " +
            "'ux_care_relationships_active_manager_subject');";

        var indexes = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Equal(3, indexes.Count);
        Assert.Contains("manager_profile_id", indexes["ix_care_relationships_manager_status"]);
        Assert.Contains("status", indexes["ix_care_relationships_manager_status"]);
        Assert.Contains("subject_profile_id", indexes["ix_care_relationships_subject_status"]);
        Assert.Contains("status", indexes["ix_care_relationships_subject_status"]);
        var activeIndex = indexes["ux_care_relationships_active_manager_subject"];
        Assert.Contains("UNIQUE", activeIndex);
        Assert.Contains("WHERE", activeIndex);
        Assert.Contains("active", activeIndex);
    }

    [Fact]
    public async Task RelationshipFks_RestrictProfileDeletion_AndRelationshipDeletionPreservesProfiles()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        await SaveAsync(graph.Account, graph.Manager, graph.Subject, graph.Relationship);

        await AssertProfileDeleteRestrictedAsync(
            graph.Manager.Id,
            "fk_care_relationships_patient_profiles_manager_profile_id");
        await AssertProfileDeleteRestrictedAsync(
            graph.Subject.Id,
            "fk_care_relationships_patient_profiles_subject_profile_id");

        await using (var dbContext = CreateDbContext())
        {
            var relationship = await dbContext.CareRelationships
                .SingleAsync(value => value.Id == graph.Relationship.Id);
            dbContext.CareRelationships.Remove(relationship);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            Assert.True(await dbContext.PatientProfiles.AnyAsync(value => value.Id == graph.Manager.Id));
            Assert.True(await dbContext.PatientProfiles.AnyAsync(value => value.Id == graph.Subject.Id));
        }
    }

    private BeeexyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
        return new BeeexyDbContext(options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
    }

    private async Task InsertRawRelationshipAsync(
        EntityId managerId,
        EntityId subjectId,
        EntityId creatorId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO patients.care_relationships " +
            "(id, manager_profile_id, subject_profile_id, relationship_type, status, " +
            "created_by_account_id, attestation_version, attested_at, created_at) " +
            "VALUES (@id, @manager, @subject, 'parent', 'active', @creator, " +
            "'phase-3.1-test', @now, @now);";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("manager", managerId.Value);
        command.Parameters.AddWithValue("subject", subjectId.Value);
        command.Parameters.AddWithValue("creator", creatorId.Value);
        command.Parameters.AddWithValue("now", UtcNow());
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertProfileDeleteRestrictedAsync(
        EntityId profileId,
        string expectedConstraint)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM patients.patient_profiles WHERE id = @id;";
        command.Parameters.AddWithValue("id", profileId.Value);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(expectedConstraint, exception.ConstraintName);
    }

    private static async Task AssertForeignKeyViolationAsync(
        Func<Task> action,
        string expectedConstraint)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
        Assert.Equal(expectedConstraint, postgresException.ConstraintName);
    }

    private static TestGraph CreateGraph(
        CareRelationshipType type = CareRelationshipType.Parent)
    {
        var suffix = UniqueSuffix();
        var account = CreateAccount(suffix);
        var manager = CreateProfile($"BXY-MANAGER-{suffix}", account.Id);
        var subject = CreateProfile($"BXY-SUBJECT-{suffix}");
        var relationship = CreateRelationship(manager.Id, subject.Id, account.Id, type);
        return new TestGraph(account, manager, subject, relationship);
    }

    private static Account CreateAccount(string suffix)
    {
        return Account.Create(
            NormalizedEmail.Create($"care-{suffix}@example.com"),
            UtcNow());
    }

    private static PatientProfile CreateProfile(string beeexyId, EntityId? accountId = null)
    {
        return PatientProfile.Create(BeeexyId.Create(beeexyId), UtcNow(), accountId);
    }

    private static CareRelationship CreateRelationship(
        EntityId managerId,
        EntityId subjectId,
        EntityId creatorId,
        CareRelationshipType type = CareRelationshipType.Parent,
        DateTimeOffset? createdAt = null)
    {
        var creationTime = createdAt ?? UtcNow();
        return CareRelationship.Create(
            managerId,
            subjectId,
            type,
            creatorId,
            AuthorizationAttestation.Create("phase-3.1-test", creationTime),
            creationTime);
    }

    private static DateTimeOffset UtcNow()
    {
        return new DateTimeOffset(2026, 8, 20, 20, 0, 0, TimeSpan.Zero);
    }

    private static string UniqueSuffix()
    {
        return Guid.NewGuid().ToString("N");
    }

    private sealed record TestGraph(
        Account Account,
        PatientProfile Manager,
        PatientProfile Subject,
        CareRelationship Relationship);
}
