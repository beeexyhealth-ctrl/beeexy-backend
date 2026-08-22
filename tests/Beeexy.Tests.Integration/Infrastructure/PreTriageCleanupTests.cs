using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriageCleanupTests(PostgreSqlContainerFixture postgres)
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task AnonymousActive_UsesExactExpiryBoundaryAndCascadesTemporaryData(
        int offsetMilliseconds,
        bool remains)
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var session = CreateActiveSession(definitions, anonymous: true, Expiry);
        await SaveAsync(definitions, session);

        var result = await RunCleanupAsync(Expiry.AddMilliseconds(offsetMilliseconds));

        await using var verify = CreateDbContext();
        Assert.Equal(remains, await verify.PreTriageSessions.AnyAsync(
            value => value.Id == session.Session.Id));
        Assert.Equal(remains, await verify.TriageAnswers.AnyAsync(
            value => value.Id == session.AnswerId));
        Assert.Equal(remains, await verify.ReportedSymptoms.AnyAsync(
            value => value.Id == session.SymptomId));
        Assert.False(await verify.PreTriageEpisodes.AnyAsync(
            value => value.SourceSessionId == session.Session.Id));
        Assert.Empty(await verify.ClinicalAssessments.ToListAsync());
        Assert.Empty(await verify.ClinicalFindings.ToListAsync());
        Assert.True(await verify.QuestionnaireVersions.AnyAsync(
            value => value.Id == definitions.Questionnaire.Id));
        Assert.Equal(remains ? 0 : 1, result.AnonymousActiveRemoved);
    }

    [Fact]
    public async Task CompletedAnonymousUnclaimed_DeletesRestrictedGraphInExplicitOrder()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var graph = CreateCompletedSession(definitions, anonymous: true, Expiry);
        await SaveAsync(definitions, graph);

        var result = await RunCleanupAsync(Expiry);

        await using var verify = CreateDbContext();
        Assert.Equal(1, result.AnonymousCompletedUnclaimedRemoved);
        Assert.False(await verify.PreTriageSessions.AnyAsync(
            value => value.Id == graph.Session.Id));
        Assert.False(await verify.PreTriageEpisodes.AnyAsync(
            value => value.Id == graph.Episode.Id));
        Assert.False(await verify.ClinicalAssessments.AnyAsync(
            value => value.Id == graph.Assessment.Id));
        Assert.False(await verify.TriageAnswers.AnyAsync(
            value => value.EpisodeId == graph.Episode.Id));
        Assert.False(await verify.ReportedSymptoms.AnyAsync(
            value => value.EpisodeId == graph.Episode.Id));
        Assert.True(await verify.QuestionnaireVersions.AnyAsync(
            value => value.Id == definitions.Questionnaire.Id));
        Assert.True(await verify.ClinicalRuleSetVersions.AnyAsync(
            value => value.Id == definitions.RuleSet.Id));
    }

    [Fact]
    public async Task ClaimedAndAuthenticatedCompletedGraphs_SurviveFarPastOriginalExpiry()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var claimOwner = CreatePatient();
        var primary = CreatePatient();
        var managed = CreatePatient();
        var claimed = CreateCompletedSession(
            definitions,
            anonymous: true,
            Expiry,
            claimOwner: claimOwner);
        var primaryCompleted = CreateCompletedSession(
            definitions,
            anonymous: false,
            Expiry,
            patient: primary);
        var managedCompleted = CreateCompletedSession(
            definitions,
            anonymous: false,
            Expiry,
            patient: managed);
        await SaveAsync(
            definitions,
            claimed,
            primaryCompleted,
            managedCompleted,
            claimOwner,
            primary,
            managed);
        var claimedAt = claimed.Episode.ClaimedAt;

        var result = await RunCleanupAsync(Expiry.AddDays(30));

        await using var verify = CreateDbContext();
        var savedClaimed = await verify.PreTriageEpisodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == claimed.Episode.Id);
        Assert.Equal(claimOwner.Id, savedClaimed.PatientProfileId);
        Assert.Equal(claimedAt, savedClaimed.ClaimedAt);
        Assert.Equal(3, await verify.PreTriageEpisodes.CountAsync());
        Assert.Equal(3, await verify.ClinicalAssessments.CountAsync());
        Assert.Equal(3, await verify.TriageAnswers.CountAsync());
        Assert.Equal(3, await verify.ReportedSymptoms.CountAsync());
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public async Task AuthenticatedAbandonment_RemovesPrimaryAndManagedTemporaryOnly()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var primary = CreatePatient();
        var managed = CreatePatient();
        var first = CreateActiveSession(definitions, false, Expiry, primary);
        var second = CreateActiveSession(definitions, false, Expiry, managed);
        await SaveAsync(definitions, first, second, primary, managed);

        var result = await RunCleanupAsync(Expiry);

        await using var verify = CreateDbContext();
        Assert.Equal(2, result.AuthenticatedAbandonedRemoved);
        Assert.Empty(await verify.PreTriageSessions.ToListAsync());
        Assert.Empty(await verify.TriageAnswers.ToListAsync());
        Assert.Empty(await verify.ReportedSymptoms.ToListAsync());
        Assert.Empty(await verify.PreTriageEpisodes.ToListAsync());
        Assert.Empty(await verify.ClinicalAssessments.ToListAsync());
        Assert.Empty(await verify.ClinicalFindings.ToListAsync());
        Assert.True(await verify.PatientProfiles.AnyAsync(value => value.Id == primary.Id));
        Assert.True(await verify.PatientProfiles.AnyAsync(value => value.Id == managed.Id));
    }

    [Fact]
    public async Task BatchingAndRepeatedRun_AreBoundedDeterministicAndIdempotent()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var expired = Enumerable.Range(0, 5)
            .Select(index => CreateActiveSession(
                definitions,
                anonymous: true,
                Expiry.AddMinutes(index)))
            .ToArray();
        var future = CreateActiveSession(
            definitions,
            anonymous: true,
            Expiry.AddHours(1));
        await SaveAsync(definitions, expired.Append(future).Cast<object>().ToArray());

        var first = await RunCleanupAsync(Expiry.AddMinutes(4), batchSize: 2);
        var second = await RunCleanupAsync(Expiry.AddMinutes(4), batchSize: 2);

        await using var verify = CreateDbContext();
        Assert.Equal(3, first.Batches);
        Assert.Equal(5, first.Selected);
        Assert.Equal(5, first.Removed);
        Assert.Equal(0, second.Selected);
        Assert.Equal(0, second.Removed);
        Assert.True(await verify.PreTriageSessions.AnyAsync(
            value => value.Id == future.Session.Id));
        Assert.Equal(1, await verify.PreTriageSessions.CountAsync());
    }

    [Fact]
    public async Task ConcurrentCleanupExecutions_ProduceOneIdempotentFinalState()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var expired = Enumerable.Range(0, 4)
            .Select(index => CreateActiveSession(
                definitions,
                anonymous: true,
                Expiry.AddMinutes(-index)))
            .ToArray();
        await SaveAsync(definitions, expired.Cast<object>().ToArray());

        var firstTask = RunCleanupAsync(Expiry, batchSize: 2);
        var secondTask = RunCleanupAsync(Expiry, batchSize: 2);
        var results = await Task.WhenAll(firstTask, secondTask);

        await using var verify = CreateDbContext();
        Assert.Equal(4, results.Sum(value => value.Removed));
        Assert.Empty(await verify.PreTriageSessions.ToListAsync());
        Assert.Empty(await verify.TriageAnswers.ToListAsync());
        Assert.Empty(await verify.ReportedSymptoms.ToListAsync());
        Assert.Empty(await verify.PreTriageEpisodes.ToListAsync());
    }

    [Fact]
    public async Task CompletionRace_ProducesOnlyCompletedPermanentOrCleanedTerminalState()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var patient = CreatePatient();
        var active = CreateActiveSession(definitions, false, Expiry, patient);
        await SaveAsync(definitions, active, patient);
        PreTriageCleanupCandidate candidate;
        await using (var selectionContext = CreateDbContext())
        {
            candidate = Assert.Single(await new PreTriageCleanupRepository(selectionContext)
                .FindCandidatesAsync(Expiry, 10, null));
        }

        await using var completionContext = CreateDbContext();
        await using var cleanupContext = CreateDbContext();
        var completionRepository = new PreTriageCompletionRepository(completionContext);
        var cleanupRepository = new PreTriageCleanupRepository(cleanupContext);
        var completionTask = completionRepository.ExecuteLockedAsync(
            active.Session.Id,
            (session, existing) =>
            {
                Assert.Null(existing);
                var completedAt = Expiry.AddMilliseconds(-1);
                var episode = PreTriageEpisode.CreateFrom(
                    session,
                    definitions.RuleSet.Id,
                    completedAt);
                var assessment = ClinicalAssessment.CreateNeutral(episode, completedAt);
                return Task.FromResult(new PreTriageCompletionMutation<object>(
                    new object(),
                    episode,
                    assessment));
            });
        var cleanupTask = cleanupRepository.CleanupLockedAsync(candidate, Expiry);
        await Task.WhenAll(completionTask, cleanupTask);
        var completionResult = await completionTask;
        var cleanupOutcome = await cleanupTask;

        await using var verify = CreateDbContext();
        var sessionExists = await verify.PreTriageSessions.AnyAsync(
            value => value.Id == active.Session.Id);
        var episodeCount = await verify.PreTriageEpisodes.CountAsync(
            value => value.SourceSessionId == active.Session.Id);
        var assessmentCount = await verify.ClinicalAssessments.CountAsync();
        Assert.Equal(completionResult is not null, sessionExists);
        Assert.Equal(sessionExists ? 1 : 0, episodeCount);
        Assert.Equal(sessionExists ? 1 : 0, assessmentCount);
        Assert.True(cleanupOutcome is PreTriageCleanupOutcome.Removed or
            PreTriageCleanupOutcome.PreservedPermanent);
    }

    [Fact]
    public async Task ClaimRace_ProducesClaimedPreservedOrExpiredRemovedTerminalState()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var owner = CreatePatient();
        var capabilities = new CryptographicAnonymousPreTriageCapabilityService();
        var generated = capabilities.Generate();
        var graph = CreateCompletedSession(
            definitions,
            anonymous: true,
            Expiry,
            capabilityHash: generated.Hash);
        await SaveAsync(definitions, graph, owner);
        PreTriageCleanupCandidate candidate;
        await using (var selectionContext = CreateDbContext())
        {
            candidate = Assert.Single(await new PreTriageCleanupRepository(selectionContext)
                .FindCandidatesAsync(Expiry, 10, null));
        }

        await using var claimContext = CreateDbContext();
        await using var cleanupContext = CreateDbContext();
        var claim = new ClaimAnonymousPreTriage(
            new FixedClock(Expiry.AddMilliseconds(-1)),
            CreateResolver(owner),
            capabilities,
            new PreTriageClaimRepository(claimContext),
            new NoOpClaimAudit());
        var cleanupRepository = new PreTriageCleanupRepository(cleanupContext);
        var claimTask = CaptureClaimAsync(claim, graph.Session.Id, generated.Value);
        var cleanupTask = cleanupRepository.CleanupLockedAsync(candidate, Expiry);
        await Task.WhenAll(claimTask, cleanupTask);
        var claimOutcome = await claimTask;
        var cleanupOutcome = await cleanupTask;

        await using var verify = CreateDbContext();
        var episode = await verify.PreTriageEpisodes
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == graph.Episode.Id);
        if (claimOutcome.Result is not null)
        {
            Assert.NotNull(episode);
            Assert.Equal(owner.Id, episode.PatientProfileId);
            Assert.NotNull(episode.ClaimedAt);
            Assert.Equal(PreTriageCleanupOutcome.PreservedPermanent, cleanupOutcome);
        }
        else
        {
            Assert.IsType<PreTriageSessionNotFoundException>(claimOutcome.Exception);
            Assert.Null(episode);
            Assert.Equal(PreTriageCleanupOutcome.Removed, cleanupOutcome);
        }

        Assert.Equal(episode is null ? 0 : 1, await verify.ClinicalAssessments.CountAsync());
        Assert.Equal(episode is null ? 0 : 1, await verify.TriageAnswers.CountAsync());
        Assert.Equal(episode is null ? 0 : 1, await verify.ReportedSymptoms.CountAsync());
    }

    [Fact]
    public async Task DeleteFailure_RollsBackEntireCompletedAnonymousGraphForRetry()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var graph = CreateCompletedSession(definitions, true, Expiry);
        await SaveAsync(definitions, graph);
        await SetEpisodeDeleteFailureTriggerAsync(enabled: true);

        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => RunCleanupAsync(Expiry));
        }
        finally
        {
            await SetEpisodeDeleteFailureTriggerAsync(enabled: false);
        }

        await using var verify = CreateDbContext();
        Assert.True(await verify.PreTriageSessions.AnyAsync(
            value => value.Id == graph.Session.Id));
        Assert.True(await verify.PreTriageEpisodes.AnyAsync(
            value => value.Id == graph.Episode.Id));
        Assert.True(await verify.ClinicalAssessments.AnyAsync(
            value => value.Id == graph.Assessment.Id));
        Assert.True(await verify.TriageAnswers.AnyAsync(
            value => value.EpisodeId == graph.Episode.Id));
        Assert.True(await verify.ReportedSymptoms.AnyAsync(
            value => value.EpisodeId == graph.Episode.Id));

        var retry = await RunCleanupAsync(Expiry);
        Assert.Equal(1, retry.Removed);
    }

    [Fact]
    public async Task CleanupTelemetry_ContainsOnlyAggregateOperationalData()
    {
        await ResetTriageAsync();
        var definitions = CreateDefinitions();
        var graph = CreateActiveSession(definitions, true, Expiry);
        await SaveAsync(definitions, graph);
        var loggerProvider = new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(loggerProvider));

        await using (var context = CreateDbContext())
        {
            var repository = new PreTriageCleanupRepository(context);
            var service = new PreTriageCleanupService(
                new FixedClock(Expiry),
                new PreTriageCleanupPolicy(10, 10),
                new ExpireAnonymousPreTriage(repository),
                repository,
                new PreTriageCleanupTelemetry(
                    loggerFactory.CreateLogger<PreTriageCleanupTelemetry>()));
            await service.ExecuteAsync();
        }

        var logs = string.Join('\n', loggerProvider.Messages);
        Assert.DoesNotContain(graph.Session.Id.Value.ToString(), logs);
        Assert.DoesNotContain("private symptom narrative", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answer-secret", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capability", logs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selected 1", logs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("removed 1", logs, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PreTriageCleanupResult> RunCleanupAsync(
        DateTimeOffset now,
        int batchSize = 100)
    {
        await using var context = CreateDbContext();
        var repository = new PreTriageCleanupRepository(context);
        var service = new PreTriageCleanupService(
            new FixedClock(now),
            new PreTriageCleanupPolicy(batchSize, 10),
            new ExpireAnonymousPreTriage(repository),
            repository,
            new NoOpTelemetry());
        return await service.ExecuteAsync();
    }

    private async Task ResetTriageAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE triage.questionnaire_versions, " +
            "triage.clinical_rule_set_versions CASCADE;");
    }

    private async Task SetEpisodeDeleteFailureTriggerAsync(bool enabled)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = enabled
            ? """
              CREATE OR REPLACE FUNCTION triage.reject_cleanup_episode_delete()
              RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                  RAISE EXCEPTION 'forced cleanup delete failure';
              END;
              $$;
              CREATE TRIGGER reject_cleanup_episode_delete
              BEFORE DELETE ON triage.pre_triage_episodes
              FOR EACH ROW EXECUTE FUNCTION triage.reject_cleanup_episode_delete();
              """
            : """
              DROP TRIGGER IF EXISTS reject_cleanup_episode_delete
                  ON triage.pre_triage_episodes;
              DROP FUNCTION IF EXISTS triage.reject_cleanup_episode_delete();
              """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task SaveAsync(DefinitionGraph definitions, params object[] values)
    {
        await using var context = CreateDbContext();
        context.Add(definitions.Questionnaire);
        context.Add(definitions.RuleSet);
        foreach (var value in values)
        {
            switch (value)
            {
                case ActiveGraph active:
                    context.Add(active.Session);
                    break;
                case CompletedGraph completed:
                    context.Add(completed.Session);
                    context.Add(completed.Episode);
                    context.Add(completed.Assessment);
                    break;
                default:
                    context.Add(value);
                    break;
            }
        }

        await context.SaveChangesAsync();
    }

    private static DefinitionGraph CreateDefinitions()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var importedAt = Expiry.AddDays(-2);
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"cleanup-{suffix}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('a', 64)),
            importedAt,
            importedAt,
            questions: [new TriageQuestionInput(
                QuestionCode.Create("cleanup-answer"),
                "Cleanup test prompt",
                1)]);
        var ruleSet = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"cleanup-{suffix}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('b', 64)),
            importedAt,
            importedAt);
        return new DefinitionGraph(questionnaire, ruleSet);
    }

    private static ActiveGraph CreateActiveSession(
        DefinitionGraph definitions,
        bool anonymous,
        DateTimeOffset expiresAt,
        PatientProfile? patient = null,
        AnonymousCapabilityHash? capabilityHash = null)
    {
        var createdAt = expiresAt.AddHours(-24);
        var session = anonymous
            ? PreTriageSession.CreateAnonymous(
                definitions.Questionnaire.Id,
                capabilityHash ?? AnonymousCapabilityHash.FromHash(UniqueHash()),
                expiresAt,
                createdAt)
            : PreTriageSession.CreateForPatient(
                patient?.Id ?? throw new ArgumentNullException(nameof(patient)),
                definitions.Questionnaire.Id,
                expiresAt,
                createdAt);
        var question = Assert.Single(definitions.Questionnaire.Questions);
        var answer = session.RecordAnswer(
            question,
            "{\"value\":\"answer-secret\"}",
            1,
            createdAt.AddMinutes(1));
        var symptom = session.ReportSymptom(
            SymptomText.Create("private symptom narrative"),
            1,
            createdAt.AddMinutes(1));
        return new ActiveGraph(session, answer.Id, symptom.Id);
    }

    private static CompletedGraph CreateCompletedSession(
        DefinitionGraph definitions,
        bool anonymous,
        DateTimeOffset expiresAt,
        PatientProfile? patient = null,
        PatientProfile? claimOwner = null,
        AnonymousCapabilityHash? capabilityHash = null)
    {
        var active = CreateActiveSession(
            definitions,
            anonymous,
            expiresAt,
            patient,
            capabilityHash);
        var completedAt = expiresAt.AddHours(-1);
        var episode = PreTriageEpisode.CreateFrom(
            active.Session,
            definitions.RuleSet.Id,
            completedAt,
            anonymous ? expiresAt : null);
        var assessment = ClinicalAssessment.CreateNeutral(episode, completedAt);
        if (claimOwner is not null)
        {
            episode.Claim(claimOwner.Id, expiresAt.AddMinutes(-30));
        }

        return new CompletedGraph(active.Session, episode, assessment);
    }

    private static PatientProfile CreatePatient() => PatientProfile.Create(
        BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
        Expiry.AddDays(-2));

    private static CurrentAccountProfileResolver CreateResolver(PatientProfile patient)
    {
        var account = Account.Create(
            NormalizedEmail.Create($"cleanup-{Guid.NewGuid():N}@example.com"),
            Expiry.AddDays(-2));
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            Expiry.AddDays(-2));
        return new CurrentAccountProfileResolver(
            new FixedIdentity(account.Id),
            new FixedCurrentAccountRepository(
                new CurrentAccountProfileState(account, [patient], [preference])),
            new NoOpAccountAudit());
    }

    private static async Task<ClaimCapture> CaptureClaimAsync(
        ClaimAnonymousPreTriage claim,
        EntityId sessionId,
        string capability)
    {
        try
        {
            return new ClaimCapture(
                await claim.ExecuteAsync(new ClaimAnonymousPreTriageCommand(
                    sessionId,
                    capability)),
                null);
        }
        catch (Exception exception)
        {
            return new ClaimCapture(null, exception);
        }
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static string UniqueHash() =>
        Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private sealed record DefinitionGraph(
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet);

    private sealed record ActiveGraph(
        PreTriageSession Session,
        EntityId AnswerId,
        EntityId SymptomId);

    private sealed record CompletedGraph(
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment);

    private sealed record ClaimCapture(
        ClaimAnonymousPreTriageResult? Result,
        Exception? Exception);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoOpTelemetry : IPreTriageCleanupTelemetry
    {
        public void RunStarted(DateTimeOffset cutoff, int batchSize, int maximumBatches)
        {
        }

        public void RunCompleted(PreTriageCleanupResult result)
        {
        }
    }

    private sealed class NoOpClaimAudit : IPreTriageClaimAuditLogger
    {
        public void ClaimTransitioned(
            EntityId sessionId,
            EntityId episodeId,
            EntityId patientProfileId,
            DateTimeOffset claimedAt)
        {
        }
    }

    private sealed class FixedIdentity(EntityId accountId) : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class FixedCurrentAccountRepository(CurrentAccountProfileState state)
        : ICurrentAccountProfileRepository
    {
        public Task<CurrentAccountProfileState> LoadAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(state);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpAccountAudit : IAccountProfileAuditLogger
    {
        public void InvariantViolation(EntityId accountId, string category)
        {
        }

        public void ProfileUpdateSucceeded(
            EntityId accountId,
            EntityId profileId,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt)
        {
        }

        public void ProfileUpdateConflict(EntityId accountId, EntityId profileId)
        {
        }
    }
}
