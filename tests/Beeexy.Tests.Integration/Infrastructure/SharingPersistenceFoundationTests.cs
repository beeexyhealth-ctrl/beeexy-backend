using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class SharingPersistenceFoundationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    [Trait("Category", "Phase111")]
    public async Task SharingFoundation_RoundTripsGrantItemsEventsAndArtifactLifecycle()
    {
        await EnsureMigratedAsync();
        var now = Utc(10);
        var (account, patient) = CreateAccountAndPatient(now);
        await SaveAsync(account, patient);

        var grant = ShareGrant.Create(
            patient.Id,
            account.Id,
            EntityId.New(),
            Fingerprint(),
            ShareScope.Case,
            Hash('a'),
            now,
            now.AddDays(7));
        var item = ShareGrantItem.Create(
            grant,
            ShareResourceType.Create("pre_triage_episode"),
            EntityId.New(),
            now.AddMinutes(1));
        var createdEvent = ShareAccessEvent.Create(
            grant,
            ShareAccessEventType.ShareCreated,
            ShareAccessOutcome.Succeeded,
            now);
        var accessEvent = ShareAccessEvent.Create(
            grant,
            ShareAccessEventType.ShareAccessed,
            ShareAccessOutcome.Denied,
            now.AddMinutes(2),
            ShareResourceType.Create("shared_profile"));
        var artifact = CreatePendingArtifact(patient.Id, account.Id, now);

        await SaveAsync(grant, item, createdEvent, accessEvent, artifact);

        await using (var dbContext = CreateDbContext())
        {
            var savedGrant = await dbContext.ShareGrants.AsNoTracking().SingleAsync(
                value => value.Id == grant.Id);
            var savedItem = await dbContext.ShareGrantItems.AsNoTracking().SingleAsync(
                value => value.Id == item.Id);
            var savedEvents = await dbContext.ShareAccessEvents.AsNoTracking()
                .Where(value => value.ShareGrantId == grant.Id)
                .OrderBy(value => value.OccurredAt)
                .ThenBy(value => value.Id)
                .ToArrayAsync();
            var savedArtifact = await dbContext.ExportArtifacts.AsNoTracking().SingleAsync(
                value => value.Id == artifact.Id);

            Assert.Equal(ShareScope.Case, savedGrant.Scope);
            Assert.Equal(Hash('a'), savedGrant.CapabilityHash);
            Assert.Equal(ShareGrantStatus.Active, savedGrant.GetStatus(now));
            Assert.Equal("pre_triage_episode", savedItem.ResourceType.Value);
            Assert.Equal(
                [ShareAccessEventType.ShareCreated, ShareAccessEventType.ShareAccessed],
                savedEvents.Select(value => value.EventType));
            Assert.Equal(ShareAccessOutcome.Denied, savedEvents[1].Outcome);
            Assert.Equal("shared_profile", savedEvents[1].ResourceType!.Value);
            Assert.Equal(ExportArtifactStatus.Pending, savedArtifact.Status);
            Assert.Equal(now.AddDays(30), savedArtifact.RetentionEligibleAt);
            Assert.Equal("shareable-health-profile-v1", savedArtifact.SnapshotVersion);
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedGrant = await dbContext.ShareGrants.SingleAsync(value => value.Id == grant.Id);
            var savedArtifact = await dbContext.ExportArtifacts.SingleAsync(
                value => value.Id == artifact.Id);
            Assert.True(savedGrant.Revoke(account.Id, now.AddHours(1)));
            savedArtifact.MarkAvailable(
                ExportArtifactContentMetadata.Create(
                    "SHA-256",
                    new string('b', 64),
                    "private/exports/foundation.json"),
                now.AddHours(1));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedGrant = await dbContext.ShareGrants.AsNoTracking().SingleAsync(
                value => value.Id == grant.Id);
            var savedArtifact = await dbContext.ExportArtifacts.AsNoTracking().SingleAsync(
                value => value.Id == artifact.Id);
            Assert.Equal(ShareGrantStatus.Revoked, savedGrant.GetStatus(now.AddHours(2)));
            Assert.Equal(account.Id, savedGrant.RevokedByAccountId);
            Assert.Equal(2, savedGrant.Version);
            Assert.Equal(ExportArtifactStatus.Available, savedArtifact.Status);
            Assert.Equal(new string('b', 64), savedArtifact.Checksum);
            Assert.Equal("private/exports/foundation.json", savedArtifact.PrivateStorageIdentity);
            Assert.Equal(2, savedArtifact.Version);
        }
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public async Task PostgreSqlConstraints_EnforceCapabilityItemAndArtifactUniqueness()
    {
        await EnsureMigratedAsync();
        var now = Utc(11);
        var (account, patient) = CreateAccountAndPatient(now);
        await SaveAsync(account, patient);

        var firstGrant = ShareGrant.Create(
            patient.Id,
            account.Id,
            EntityId.New(),
            Fingerprint(),
            ShareScope.FullProfile,
            Hash('c'),
            now,
            now.AddDays(1));
        await SaveAsync(firstGrant);

        await AssertDatabaseViolationAsync(
            () => SaveAsync(ShareGrant.Create(
                patient.Id,
                account.Id,
                EntityId.New(),
                Fingerprint(),
                ShareScope.PreTriage,
                Hash('c'),
                now,
                now.AddDays(1))),
            PostgresErrorCodes.UniqueViolation,
            "ux_share_grants_capability_hash");

        var resourceId = EntityId.New();
        await SaveAsync(ShareGrantItem.Create(
            firstGrant,
            ShareResourceType.Create("pre_triage_episode"),
            resourceId,
            now));
        await AssertDatabaseViolationAsync(
            () => SaveAsync(ShareGrantItem.Create(
                firstGrant,
                ShareResourceType.Create("pre_triage_episode"),
                resourceId,
                now.AddMinutes(1))),
            PostgresErrorCodes.UniqueViolation,
            "ux_share_grant_items_grant_resource");

        var idempotencyKey = EntityId.New();
        var firstArtifact = CreatePendingArtifact(
            patient.Id,
            account.Id,
            now,
            idempotencyKey);
        await SaveAsync(firstArtifact);
        await AssertDatabaseViolationAsync(
            () => SaveAsync(CreatePendingArtifact(
                patient.Id,
                account.Id,
                now,
                idempotencyKey)),
            PostgresErrorCodes.UniqueViolation,
            "ux_export_artifacts_patient_idempotency_key");
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public async Task PostgreSqlConstraints_RejectInvalidHashExpiryAndArtifactLifecycle()
    {
        await EnsureMigratedAsync();
        var now = Utc(12);
        var (account, patient) = CreateAccountAndPatient(now);
        await SaveAsync(account, patient);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await AssertPostgreSqlViolationAsync(
            connection,
            "INSERT INTO sharing.share_grants " +
            "(id, patient_profile_id, creator_account_id, scope, capability_hash, " +
            "created_at, expires_at, version) VALUES " +
            "(@id, @patient, @account, 'full_profile', 'short', @created, @expires, 1);",
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                command.Parameters.AddWithValue("patient", patient.Id.Value);
                command.Parameters.AddWithValue("account", account.Id.Value);
                command.Parameters.AddWithValue("created", now);
                command.Parameters.AddWithValue("expires", now.AddHours(1));
            },
            PostgresErrorCodes.CheckViolation,
            "ck_share_grants_capability_hash");

        await AssertPostgreSqlViolationAsync(
            connection,
            "INSERT INTO sharing.share_grants " +
            "(id, patient_profile_id, creator_account_id, scope, capability_hash, " +
            "created_at, expires_at, version) VALUES " +
            "(@id, @patient, @account, 'full_profile', @hash, @created, @created, 1);",
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                command.Parameters.AddWithValue("patient", patient.Id.Value);
                command.Parameters.AddWithValue("account", account.Id.Value);
                command.Parameters.AddWithValue("hash", new string('d', 64));
                command.Parameters.AddWithValue("created", now);
            },
            PostgresErrorCodes.CheckViolation,
            "ck_share_grants_expiry");

        await AssertPostgreSqlViolationAsync(
            connection,
            "INSERT INTO sharing.export_artifacts " +
            "(id, patient_profile_id, requested_by_account_id, idempotency_key, format, " +
            "media_type, snapshot_id, snapshot_version, status, checksum_algorithm, checksum, " +
            "private_storage_identity, created_at, retention_eligible_at, version) VALUES " +
            "(@id, @patient, @account, @key, 'beeexy_json', 'application/json', @snapshot, " +
            "'snapshot-v1', 'pending', 'SHA-256', @checksum, 'private/partial', " +
            "@created, @retention, 1);",
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                command.Parameters.AddWithValue("patient", patient.Id.Value);
                command.Parameters.AddWithValue("account", account.Id.Value);
                command.Parameters.AddWithValue("key", Guid.NewGuid());
                command.Parameters.AddWithValue("snapshot", Guid.NewGuid());
                command.Parameters.AddWithValue("checksum", new string('e', 64));
                command.Parameters.AddWithValue("created", now);
                command.Parameters.AddWithValue("retention", now.AddDays(30));
            },
            PostgresErrorCodes.CheckViolation,
            "ck_export_artifacts_lifecycle");
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public async Task SharingSchema_HasFourUuidTablesSafeColumnsRestrictiveFksAndRequiredIndexes()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        var tables = await QueryStringsAsync(
            connection,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'sharing' AND table_type = 'BASE TABLE' ORDER BY table_name;");
        Assert.Equal(
            ["export_artifacts", "share_access_events", "share_grant_items", "share_grants"],
            tables);

        var uuidPrimaryKeys = await QueryStringsAsync(
            connection,
            "SELECT c.table_name FROM information_schema.columns c " +
            "JOIN information_schema.table_constraints tc " +
            "ON tc.table_schema = c.table_schema AND tc.table_name = c.table_name " +
            "JOIN information_schema.key_column_usage kcu " +
            "ON kcu.constraint_schema = tc.constraint_schema " +
            "AND kcu.constraint_name = tc.constraint_name AND kcu.column_name = c.column_name " +
            "WHERE c.table_schema = 'sharing' AND c.column_name = 'id' " +
            "AND c.data_type = 'uuid' AND tc.constraint_type = 'PRIMARY KEY' " +
            "ORDER BY c.table_name;");
        Assert.Equal(tables, uuidPrimaryKeys);

        var nonRestrictiveForeignKeys = await QueryStringsAsync(
            connection,
            "SELECT conname FROM pg_constraint WHERE connamespace = 'sharing'::regnamespace " +
            "AND contype = 'f' AND confdeltype <> 'r';");
        Assert.Empty(nonRestrictiveForeignKeys);

        var columns = await QueryStringsAsync(
            connection,
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'sharing' ORDER BY table_name, ordinal_position;");
        Assert.Contains("capability_hash", columns);
        Assert.Contains("private_storage_identity", columns);
        Assert.DoesNotContain(columns, value => value is
            "capability" or "raw_token" or "plain_token" or "share_token" or
            "jwt" or "ip_address" or "user_agent" or "payload");

        var nonTimestampInstants = await QueryStringsAsync(
            connection,
            "SELECT table_name || '.' || column_name FROM information_schema.columns " +
            "WHERE table_schema = 'sharing' " +
            "AND column_name LIKE '%\\_at' ESCAPE '\\' " +
            "AND data_type <> 'timestamp with time zone';");
        Assert.Empty(nonTimestampInstants);

        var indexes = await QueryStringsAsync(
            connection,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'sharing' ORDER BY indexname;");
        var requiredIndexes = new[]
        {
            "ix_export_artifacts_patient_created_id",
            "ix_export_artifacts_status_retention_id",
            "ix_share_access_events_grant_occurred_id",
            "ix_share_grants_active_expiry_id",
            "ix_share_grants_patient_created_id",
            "ux_export_artifacts_patient_idempotency_key",
            "ux_export_artifacts_private_storage_identity",
            "ux_share_grant_items_grant_resource",
            "ux_share_grants_capability_hash"
        };
        Assert.All(requiredIndexes, value => Assert.Contains(value, indexes));

        var triggerNames = await QueryStringsAsync(
            connection,
            "SELECT tgname FROM pg_trigger WHERE tgrelid IN " +
            "('sharing.share_grants'::regclass, 'sharing.share_grant_items'::regclass, " +
            "'sharing.share_access_events'::regclass, 'sharing.export_artifacts'::regclass) " +
            "AND NOT tgisinternal ORDER BY tgname;");
        Assert.Equal(
            [
                "trg_export_artifacts_protect_history",
                "trg_share_access_events_append_only",
                "trg_share_grant_items_append_only",
                "trg_share_grants_protect_history"
            ],
            triggerNames);
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public async Task ApplicationAndDatabaseGuards_ProtectAppendOnlyAndLifecycleHistory()
    {
        await EnsureMigratedAsync();
        var now = Utc(13);
        var (account, patient) = CreateAccountAndPatient(now);
        await SaveAsync(account, patient);
        var grant = ShareGrant.Create(
            patient.Id,
            account.Id,
            EntityId.New(),
            Fingerprint(),
            ShareScope.SpecificRecords,
            Hash('f'),
            now,
            now.AddDays(1));
        var item = ShareGrantItem.Create(
            grant,
            ShareResourceType.Create("export_artifact"),
            EntityId.New(),
            now);
        var accessEvent = ShareAccessEvent.Create(
            grant,
            ShareAccessEventType.ShareCreated,
            ShareAccessOutcome.Succeeded,
            now);
        var artifact = CreatePendingArtifact(patient.Id, account.Id, now);
        await SaveAsync(grant, item, accessEvent, artifact);

        await using (var dbContext = CreateDbContext())
        {
            var trackedEvent = await dbContext.ShareAccessEvents.SingleAsync(
                value => value.Id == accessEvent.Id);
            dbContext.Entry(trackedEvent).Property(value => value.Outcome).CurrentValue =
                ShareAccessOutcome.Denied;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dbContext.SaveChangesAsync());
        }

        await using (var dbContext = CreateDbContext())
        {
            var trackedGrant = await dbContext.ShareGrants.SingleAsync(value => value.Id == grant.Id);
            dbContext.ShareGrants.Remove(trackedGrant);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dbContext.SaveChangesAsync());
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await AssertPostgreSqlViolationAsync(
            connection,
            "UPDATE sharing.share_access_events SET outcome = 'denied' WHERE id = @id;",
            command => command.Parameters.AddWithValue("id", accessEvent.Id.Value),
            "55000");
        await AssertPostgreSqlViolationAsync(
            connection,
            "UPDATE sharing.share_grants SET scope = 'full_profile', version = version + 1 " +
            "WHERE id = @id;",
            command => command.Parameters.AddWithValue("id", grant.Id.Value),
            "55000");
        await AssertPostgreSqlViolationAsync(
            connection,
            "DELETE FROM sharing.export_artifacts WHERE id = @id;",
            command => command.Parameters.AddWithValue("id", artifact.Id.Value),
            "55000");
        await AssertPostgreSqlViolationAsync(
            connection,
            "DELETE FROM identity.accounts WHERE id = @id;",
            command => command.Parameters.AddWithValue("id", account.Id.Value),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    [Trait("Category", "Phase111")]
    public async Task ExportArtifact_PrivateStorageIdentityIsUniqueAndSnapshotMetadataSurvivesDeletionState()
    {
        await EnsureMigratedAsync();
        var now = Utc(14);
        var (account, patient) = CreateAccountAndPatient(now);
        await SaveAsync(account, patient);
        var first = CreatePendingArtifact(patient.Id, account.Id, now);
        var second = CreatePendingArtifact(patient.Id, account.Id, now, EntityId.New());
        await SaveAsync(first, second);

        await using (var dbContext = CreateDbContext())
        {
            var trackedFirst = await dbContext.ExportArtifacts.SingleAsync(value => value.Id == first.Id);
            trackedFirst.MarkAvailable(
                ExportArtifactContentMetadata.Create(
                    "SHA-256",
                    new string('a', 64),
                    "private/exports/shared-key"),
                now.AddHours(1));
            await dbContext.SaveChangesAsync();
        }

        await AssertDatabaseViolationAsync(
            async () =>
            {
                await using var dbContext = CreateDbContext();
                var trackedSecond = await dbContext.ExportArtifacts.SingleAsync(
                    value => value.Id == second.Id);
                trackedSecond.MarkAvailable(
                    ExportArtifactContentMetadata.Create(
                        "SHA-256",
                        new string('b', 64),
                        "private/exports/shared-key"),
                    now.AddHours(1));
                await dbContext.SaveChangesAsync();
            },
            PostgresErrorCodes.UniqueViolation,
            "ux_export_artifacts_private_storage_identity");

        await using (var dbContext = CreateDbContext())
        {
            var trackedFirst = await dbContext.ExportArtifacts.SingleAsync(value => value.Id == first.Id);
            trackedFirst.MarkDeleted(now.AddDays(30));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.ExportArtifacts.AsNoTracking().SingleAsync(
                value => value.Id == first.Id);
            Assert.Equal(ExportArtifactStatus.Deleted, saved.Status);
            Assert.Equal(first.SnapshotId, saved.SnapshotId);
            Assert.Equal("shareable-health-profile-v1", saved.SnapshotVersion);
            Assert.Equal(new string('a', 64), saved.Checksum);
            Assert.Equal("private/exports/shared-key", saved.PrivateStorageIdentity);
            Assert.Equal(now.AddDays(30), saved.DeletedAt);
        }
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
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

    private static (Account Account, PatientProfile Patient) CreateAccountAndPatient(
        DateTimeOffset now)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"sharing-{suffix}@example.com"),
            now);
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-SHARE-{suffix}"),
            now,
            account.Id);
        return (account, patient);
    }

    private static ExportArtifact CreatePendingArtifact(
        EntityId patientId,
        EntityId accountId,
        DateTimeOffset now,
        EntityId? idempotencyKey = null)
    {
        return ExportArtifact.CreatePending(
            patientId,
            accountId,
            idempotencyKey ?? EntityId.New(),
            ExportArtifactFormat.BeeexyJson,
            "application/json",
            EntityId.New(),
            "shareable-health-profile-v1",
            now,
            now.AddDays(30));
    }

    private static TokenHash Hash(char value)
    {
        return TokenHash.FromHash(new string(value, 64));
    }

    private static ShareRequestFingerprint Fingerprint()
    {
        return ShareRequestFingerprint.Create(new string('0', 64));
    }

    private static async Task AssertDatabaseViolationAsync(
        Func<Task> action,
        string sqlState,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    private static async Task AssertPostgreSqlViolationAsync(
        NpgsqlConnection connection,
        string sql,
        Action<NpgsqlCommand> configure,
        string sqlState,
        string? constraintName = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, exception.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, exception.ConstraintName);
        }
    }

    private static async Task<string[]> QueryStringsAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 9, 9, hour, 0, 0, TimeSpan.Zero);
    }
}
