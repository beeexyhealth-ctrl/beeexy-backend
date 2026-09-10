using System.Text.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed record ShareAccessIdentity(
    EntityId ShareGrantId,
    ShareScope TokenScope,
    EntityId TokenId);

public sealed record SharedProfileGrantState(
    ShareGrant Grant,
    IReadOnlyList<ShareGrantItem> Items);

public interface ISharedProfileGrantRepository
{
    Task<SharedProfileGrantState?> FindAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken = default);
}

public interface IShareAccessEventRecorder
{
    Task RecordSuccessfulAccessAsync(
        ShareGrant grant,
        EntityId eventId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}

public interface IShareScopeEvaluator
{
    SharedProfileSelection Evaluate(
        ShareGrant grant,
        IReadOnlyList<ShareGrantItem> items,
        ShareScope tokenScope);
}

public sealed record SharedProfileSelection(
    ShareScope Scope,
    bool IncludeFullProfile,
    IReadOnlyList<ShareItemReference> Items);

public interface ICanonicalSharedHealthSnapshotBuilder
{
    Task<CanonicalSharedHealthSnapshot> BuildAsync(
        EntityId patientProfileId,
        SharedProfileSelection selection,
        CancellationToken cancellationToken = default);
}

public sealed record BuildSharedProfileResult(
    ShareScope Scope,
    CanonicalSharedHealthSnapshot Profile);

public sealed record CanonicalSharedHealthSnapshot(
    SharedPatientDemographics? Demographics,
    IReadOnlyList<SharedClinicalHistoryEvent> ClinicalHistory,
    IReadOnlyList<SharedPreTriageRecord> PreTriage,
    IReadOnlyList<SharedSymptomDiaryEntry> SymptomDiaryEntries,
    IReadOnlyList<SharedSymptomDiaryContent> SymptomDiaryContent,
    IReadOnlyList<SharedSecondOpinionResult> SecondOpinions);

public sealed record SharedPatientDemographics(
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? SexAssignedAtBirth,
    string? State,
    long Version);

public sealed record SharedClinicalHistoryEvent(
    EntityId EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    SharedClinicalProvenance Provenance);

public sealed record SharedClinicalProvenance(
    string SourceType,
    EntityId SourceId,
    EntityId QuestionnaireVersionId,
    EntityId ClinicalRuleSetVersionId);

public sealed record SharedPreTriageRecord(
    EntityId EpisodeId,
    DateTimeOffset CompletedAt,
    SharedPreTriagePrimarySymptom PrimarySymptom,
    SharedPreTriageDuration Duration,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms,
    EntityId QuestionnaireVersionId,
    EntityId ClinicalRuleSetVersionId);

public sealed record SharedPreTriagePrimarySymptom(string Code, string Display);

public sealed record SharedPreTriageDuration(decimal Value, string Unit);

public sealed record SharedSymptomDiaryEntry(
    EntityId CheckInId,
    EntityId EpisodeId,
    DateTimeOffset RecordedAt,
    string Pathway,
    SharedSymptomDiaryPackageVersion Package,
    IReadOnlyList<SharedSymptomDiaryAnswer> Answers);

public sealed record SharedSymptomDiaryAnswer(
    string QuestionCode,
    string PromptText,
    JsonElement Value);

public sealed record SharedSymptomDiaryContent(
    SharedSymptomDiaryPackageVersion Package,
    string Pathway,
    string? InformationalHeading,
    string? InformationalBody,
    IReadOnlyList<SharedSymptomWarningSign> WarningSigns);

public sealed record SharedSymptomDiaryPackageVersion(
    EntityId PackageVersionId,
    string PackageCode,
    string PackageVersion,
    string CanonicalContentHash,
    string QuestionSetCode,
    string QuestionSetVersion,
    string SymptomInformationCode,
    string SymptomInformationVersion,
    string ContentSource,
    string ReviewStatus,
    string ApprovalStatus,
    DateTimeOffset ApprovedAt);

public sealed record SharedSymptomWarningSign(
    string Code,
    string DisplayText,
    int SourceOrder);

public sealed record SharedSecondOpinionResult(
    EntityId ResultId,
    DateTimeOffset GeneratedAt,
    string ResultVersion,
    string Summary,
    IReadOnlyList<string> ImportantPoints,
    IReadOnlyList<string> PossibleQuestionsForDoctor,
    IReadOnlyList<string> MissingInformation,
    string Disclaimer);

public sealed record SharedSecondOpinionStoredResult(
    EntityId ResultId,
    EntityId AnalysisId,
    DateTimeOffset GeneratedAt,
    string ResultSchemaVersion,
    string ContentJson);

public interface ISharedSecondOpinionReadRepository
{
    Task<IReadOnlyList<SharedSecondOpinionStoredResult>> ListDisplayableAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default);

    Task<SharedSecondOpinionStoredResult?> GetDisplayableAsync(
        EntityId patientProfileId,
        EntityId resultId,
        CancellationToken cancellationToken = default);
}
