using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ClaimAnonymousPreTriage(
    IClock clock,
    CurrentAccountProfileResolver currentAccountProfileResolver,
    IAnonymousPreTriageCapabilityService capabilityService,
    IPreTriageClaimRepository repository,
    IPreTriageClaimAuditLogger auditLogger)
{
    public async Task<ClaimAnonymousPreTriageResult> ExecuteAsync(
        ClaimAnonymousPreTriageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await currentAccountProfileResolver.ResolveAsync(cancellationToken);
        var patientProfileId = current.PrimaryProfile.Id;
        var now = ToPostgreSqlPrecision(clock.UtcNow);

        var outcome = await repository.ExecuteLockedAsync(
            command.SessionId,
            graph => ClaimLocked(
                graph,
                patientProfileId,
                command.AnonymousCapability,
                now),
            cancellationToken) ?? throw new PreTriageSessionNotFoundException();

        if (outcome.IsNewlyClaimed)
        {
            auditLogger.ClaimTransitioned(
                outcome.Result.SessionId,
                outcome.Result.EpisodeId,
                outcome.Result.PatientProfileId,
                outcome.Result.ClaimedAt);
        }

        return outcome.Result;
    }

    private ClaimAnonymousPreTriageMutation ClaimLocked(
        ClaimablePreTriageGraph graph,
        EntityId patientProfileId,
        string? anonymousCapability,
        DateTimeOffset now)
    {
        var session = graph.Session;
        if (!session.IsAnonymous || session.AnonymousCapabilityHash is null)
        {
            throw new PreTriageSessionNotFoundException();
        }

        if (!capabilityService.Verify(
                anonymousCapability,
                session.AnonymousCapabilityHash))
        {
            throw new SessionAuthenticationException();
        }

        if (session.Status != PreTriageSessionStatus.Completed)
        {
            if (now >= session.ExpiresAt)
            {
                throw new PreTriageSessionNotFoundException();
            }

            throw new PreTriageSessionStateConflictException(
                "Only a completed anonymous pre-triage session can be claimed.");
        }

        var episode = graph.Episode ?? throw new InvalidOperationException(
            "A completed anonymous pre-triage session is missing its episode.");
        var assessment = graph.Assessment ?? throw new InvalidOperationException(
            "A completed anonymous pre-triage episode is missing its assessment.");
        EnsureCompletedGraphIntegrity(session, episode, assessment);

        if (episode.PatientProfileId.HasValue)
        {
            if (!episode.IsClaimed || episode.ClaimedAt is null)
            {
                throw new InvalidOperationException(
                    "The anonymous pre-triage claim metadata is inconsistent.");
            }

            if (episode.PatientProfileId != patientProfileId)
            {
                throw new PreTriageClaimConflictException();
            }

            return Result(session, episode, patientProfileId, isNewlyClaimed: false);
        }

        if (episode.ClaimedAt.HasValue)
        {
            throw new InvalidOperationException(
                "The anonymous pre-triage claim metadata is inconsistent.");
        }

        if (!episode.AnonymousExpiresAt.HasValue || now >= episode.AnonymousExpiresAt.Value)
        {
            throw new PreTriageSessionNotFoundException();
        }

        if (!episode.Claim(patientProfileId, now))
        {
            throw new InvalidOperationException(
                "The anonymous pre-triage claim transition was not applied.");
        }

        return Result(session, episode, patientProfileId, isNewlyClaimed: true);
    }

    private static void EnsureCompletedGraphIntegrity(
        PreTriageSession session,
        PreTriageEpisode episode,
        ClinicalAssessment assessment)
    {
        if (episode.SourceSessionId != session.Id ||
            episode.QuestionnaireVersionId != session.QuestionnaireVersionId ||
            episode.AnonymousExpiresAt != session.ExpiresAt ||
            assessment.EpisodeId != episode.Id ||
            assessment.ClinicalRuleSetVersionId != episode.ClinicalRuleSetVersionId ||
            assessment.UrgencyCode is not null ||
            assessment.Findings.Count != 0)
        {
            throw new InvalidOperationException(
                "The completed anonymous pre-triage graph is inconsistent.");
        }
    }

    private static ClaimAnonymousPreTriageMutation Result(
        PreTriageSession session,
        PreTriageEpisode episode,
        EntityId patientProfileId,
        bool isNewlyClaimed) => new(
        new ClaimAnonymousPreTriageResult(
            session.Id,
            episode.Id,
            patientProfileId,
            episode.ClaimedAt ?? throw new InvalidOperationException(
                "The claimed episode is missing its claim timestamp.")),
        isNewlyClaimed);

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}

public sealed record ClaimAnonymousPreTriageCommand(
    EntityId SessionId,
    string? AnonymousCapability);

public sealed record ClaimAnonymousPreTriageResult(
    EntityId SessionId,
    EntityId EpisodeId,
    EntityId PatientProfileId,
    DateTimeOffset ClaimedAt);

public sealed record ClaimablePreTriageGraph(
    PreTriageSession Session,
    PreTriageEpisode? Episode,
    ClinicalAssessment? Assessment);

public sealed record ClaimAnonymousPreTriageMutation(
    ClaimAnonymousPreTriageResult Result,
    bool IsNewlyClaimed);

public interface IPreTriageClaimRepository
{
    Task<ClaimAnonymousPreTriageMutation?> ExecuteLockedAsync(
        EntityId sessionId,
        Func<ClaimablePreTriageGraph, ClaimAnonymousPreTriageMutation> mutation,
        CancellationToken cancellationToken = default);
}

public interface IPreTriageClaimAuditLogger
{
    void ClaimTransitioned(
        EntityId sessionId,
        EntityId episodeId,
        EntityId patientProfileId,
        DateTimeOffset claimedAt);
}

public sealed class PreTriageClaimConflictException : Exception;
