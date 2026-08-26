using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class StartPreTriage(
    IClock clock,
    CurrentAccountProfileResolver currentAccountProfileResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IClinicalPathwayRegistry pathwayRegistry,
    IAnonymousPreTriageCapabilityService capabilityService,
    IPreTriageSessionRepository repository,
    IPreTriageSessionAuditLogger auditLogger)
{
    public static readonly TimeSpan AnonymousSessionLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan AuthenticatedSessionLifetime = TimeSpan.FromHours(24);

    public async Task<StartPreTriageResult> ExecuteAsync(
        StartPreTriageCommand command,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(command, auditAfterSave: true, cancellationToken);

    internal async Task<StartPreTriageResult> ExecuteForOrchestrationAsync(
        StartPreTriageCommand command,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(command, auditAfterSave: false, cancellationToken);

    private async Task<StartPreTriageResult> ExecuteCoreAsync(
        StartPreTriageCommand command,
        bool auditAfterSave,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var patientProfileId = await ResolvePatientAsync(command, cancellationToken);
        ValidateUnsupportedFields(command.UnsupportedFields);
        var resolution = await pathwayRegistry.ResolveAsync(
            command.Pathway ?? string.Empty,
            cancellationToken);
        var package = ResolveUsablePackage(command, resolution);
        var now = clock.UtcNow;
        var lifetime = command.CallerMode == PreTriageCallerMode.Anonymous
            ? AnonymousSessionLifetime
            : AuthenticatedSessionLifetime;
        var expiresAt = now.Add(lifetime);

        GeneratedAnonymousCapability? generatedCapability = null;
        PreTriageSession session;
        if (command.CallerMode == PreTriageCallerMode.Anonymous)
        {
            generatedCapability = capabilityService.Generate();
            session = PreTriageSession.CreateAnonymous(
                package.Questionnaire.Id,
                generatedCapability.Hash,
                expiresAt,
                now);
        }
        else
        {
            session = PreTriageSession.CreateForPatient(
                patientProfileId!.Value,
                package.Questionnaire.Id,
                expiresAt,
                now);
        }

        repository.Add(session);
        await repository.SaveChangesAsync(cancellationToken);

        var result = new StartPreTriageResult(
            session.Id,
            patientProfileId,
            package.Pathway,
            session.Status,
            expiresAt,
            package.Questionnaire.QuestionnaireCode,
            package.Questionnaire.Version,
            package.RuleSet.RuleSetCode,
            package.RuleSet.Version,
            package.ContentStatus,
            generatedCapability?.Value,
            now);
        if (auditAfterSave)
        {
            AuditCreated(result, command.CallerMode);
        }

        return result;
    }

    internal void AuditCreated(
        StartPreTriageResult result,
        PreTriageCallerMode callerMode) => auditLogger.SessionCreated(
            result.SessionId,
            callerMode,
            result.Pathway,
            result.QuestionnaireCode,
            result.QuestionnaireVersion,
            result.RuleSetCode,
            result.RuleSetVersion,
            result.PatientProfileId,
            result.CreatedAt,
            result.ExpiresAt);

    private async Task<EntityId?> ResolvePatientAsync(
        StartPreTriageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CallerMode == PreTriageCallerMode.Anonymous)
        {
            if (command.PatientProfileId.HasValue)
            {
                throw new SessionAuthenticationException();
            }

            return null;
        }

        if (!command.PatientProfileId.HasValue)
        {
            var current = await currentAccountProfileResolver.ResolveAsync(cancellationToken);
            return current.PrimaryProfile.Id;
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            command.PatientProfileId.Value,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PatientProfileNotFoundException();
        }

        return command.PatientProfileId.Value;
    }

    private ClinicalDefinitionPackage ResolveUsablePackage(
        StartPreTriageCommand command,
        ClinicalPathwayResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(command.Pathway))
        {
            auditLogger.SessionRejected(
                command.CallerMode,
                null,
                PreTriageStartRejectionCategory.InvalidPathway);
            throw new RequestValidationException(
                "pre_triage.pathway_required",
                "A supported clinical pathway is required.");
        }

        if (resolution.Status == ClinicalPathwayResolutionStatus.Unknown)
        {
            auditLogger.SessionRejected(
                command.CallerMode,
                null,
                PreTriageStartRejectionCategory.UnknownPathway);
            throw new RequestValidationException(
                "pre_triage.pathway_unknown",
                "The requested clinical pathway is unavailable.");
        }

        if (resolution.Status == ClinicalPathwayResolutionStatus.RecognizedButUnsupported)
        {
            auditLogger.SessionRejected(
                command.CallerMode,
                resolution.Pathway?.Value,
                PreTriageStartRejectionCategory.UnsupportedPathway);
            throw new RequestValidationException(
                "pre_triage.pathway_unsupported",
                "The requested clinical pathway is not currently supported.");
        }

        if (resolution.ActiveDefinition is null)
        {
            auditLogger.SessionRejected(
                command.CallerMode,
                resolution.Pathway?.Value,
                PreTriageStartRejectionCategory.DefinitionUnavailable);
            throw new RequestValidationException(
                "pre_triage.definition_unavailable",
                "No usable clinical definition is available for the requested pathway.");
        }

        return resolution.ActiveDefinition;
    }

    private static void ValidateUnsupportedFields(IReadOnlyCollection<string> unsupportedFields)
    {
        if (unsupportedFields.Count > 0)
        {
            throw new RequestValidationException(
                "pre_triage.unsupported_field",
                "The session-start request contains an unsupported field.");
        }
    }
}

public enum PreTriageCallerMode
{
    Anonymous = 0,
    Authenticated = 1
}

public sealed record StartPreTriageCommand(
    string? Pathway,
    EntityId? PatientProfileId,
    PreTriageCallerMode CallerMode,
    IReadOnlyCollection<string> UnsupportedFields);

public sealed record StartPreTriageResult(
    EntityId SessionId,
    EntityId? PatientProfileId,
    ClinicalPathwayCode Pathway,
    PreTriageSessionStatus Status,
    DateTimeOffset ExpiresAt,
    QuestionnaireCode QuestionnaireCode,
    DefinitionVersion QuestionnaireVersion,
    RuleSetCode RuleSetCode,
    DefinitionVersion RuleSetVersion,
    ClinicalContentStatus ClinicalContentStatus,
    string? AnonymousCapability,
    DateTimeOffset CreatedAt);

public interface IPreTriageSessionRepository
{
    void Add(PreTriageSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPreTriageSessionAuditLogger
{
    void SessionCreated(
        EntityId sessionId,
        PreTriageCallerMode callerMode,
        ClinicalPathwayCode pathway,
        QuestionnaireCode questionnaireCode,
        DefinitionVersion questionnaireVersion,
        RuleSetCode ruleSetCode,
        DefinitionVersion ruleSetVersion,
        EntityId? patientProfileId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt);

    void SessionRejected(
        PreTriageCallerMode callerMode,
        string? pathway,
        PreTriageStartRejectionCategory category);
}

public enum PreTriageStartRejectionCategory
{
    InvalidPathway = 0,
    UnknownPathway = 1,
    UnsupportedPathway = 2,
    DefinitionUnavailable = 3
}
