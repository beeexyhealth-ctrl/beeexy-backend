using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ResolvePreTriageEducationalVideoOffer(
    IClock clock,
    AuthorizePatientAccess authorizePatientAccess,
    IAnonymousPreTriageCapabilityService capabilityService,
    IClinicalDefinitionProvider definitionProvider,
    IPreTriageEducationalVideoOfferRepository repository,
    IPreTriageEducationalVideoCatalog educationalVideos)
{
    public async Task<ResolvePreTriageEducationalVideoOfferResult> ExecuteAsync(
        ResolvePreTriageEducationalVideoOfferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedDecision = command.Decision switch
        {
            "WATCH" => PreTriageEducationalVideoDecision.Watch,
            "SKIP" => PreTriageEducationalVideoDecision.Skip,
            _ => (PreTriageEducationalVideoDecision?)null
        };
        if (command.UnsupportedFields.Count > 0 || !requestedDecision.HasValue)
        {
            throw new RequestValidationException(
                "pre_triage.educational_video_decision_invalid",
                "The educational video decision must be WATCH or SKIP.");
        }

        return await repository.MutateLockedAsync(
            command.SessionId,
            async session =>
            {
                await AuthorizeAsync(session, command, cancellationToken);
                var now = ToPostgreSqlPrecision(clock.UtcNow);
                if (session.Status != PreTriageSessionStatus.Active)
                {
                    throw new PreTriageSessionStateConflictException(
                        "A completed pre-triage session cannot be changed.");
                }

                if (now >= session.ExpiresAt)
                {
                    throw new PreTriageSessionNotFoundException();
                }

                if (!session.EducationalVideoOfferRequired)
                {
                    throw new PreTriageSessionStateConflictException(
                        "This pre-triage session has no educational video offer.");
                }

                var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
                    session.QuestionnaireVersionId,
                    cancellationToken) ?? throw new InvalidOperationException(
                        "The session's pinned questionnaire package is unavailable.");
                if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
                    package.RuleDefinitions.DemoIntake is null ||
                    package.Questionnaire.Id != session.QuestionnaireVersionId ||
                    educationalVideos.Find(package.Pathway) is null)
                {
                    throw new InvalidOperationException(
                        "The session's educational video configuration is inconsistent.");
                }

                var newlyResolved = session.ResolveEducationalVideoOffer(
                    requestedDecision.Value,
                    now);
                var projection = PreTriageConversationProjectionBuilder.Build(
                    session,
                    session.Answers,
                    package,
                    educationalVideos);
                return new ResolvePreTriageEducationalVideoOfferResult(
                    session.Id,
                    session.EducationalVideoDecision!.Value,
                    session.EducationalVideoOfferResolvedAt!.Value,
                    newlyResolved,
                    projection);
            },
            cancellationToken) ?? throw new PreTriageSessionNotFoundException();
    }

    private async Task AuthorizeAsync(
        PreTriageSession session,
        ResolvePreTriageEducationalVideoOfferCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CallerMode == PreTriageCallerMode.Anonymous)
        {
            if (!session.IsAnonymous || session.AnonymousCapabilityHash is null ||
                !capabilityService.Verify(
                    command.AnonymousCapability,
                    session.AnonymousCapabilityHash))
            {
                throw new SessionAuthenticationException();
            }

            return;
        }

        if (session.PatientProfileId is null)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            session.PatientProfileId.Value,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PreTriageSessionNotFoundException();
        }
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}

public sealed record ResolvePreTriageEducationalVideoOfferCommand(
    EntityId SessionId,
    PreTriageCallerMode CallerMode,
    string? AnonymousCapability,
    string? Decision,
    IReadOnlyCollection<string> UnsupportedFields);

public sealed record ResolvePreTriageEducationalVideoOfferResult(
    EntityId SessionId,
    PreTriageEducationalVideoDecision Decision,
    DateTimeOffset ResolvedAt,
    bool NewlyResolved,
    PreTriageConversationProjection Conversation);

public interface IPreTriageEducationalVideoOfferRepository
{
    Task<TResult?> MutateLockedAsync<TResult>(
        EntityId sessionId,
        Func<PreTriageSession, Task<TResult>> mutation,
        CancellationToken cancellationToken = default)
        where TResult : class;
}
