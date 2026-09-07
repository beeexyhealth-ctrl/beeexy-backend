using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Care;

public sealed record SymptomDiaryPackageDefinition(
    SymptomDiaryCode PackageCode,
    DefinitionVersion PackageVersion,
    SymptomDiaryCode QuestionSetCode,
    DefinitionVersion QuestionSetVersion,
    SymptomDiaryCode SymptomInformationCode,
    DefinitionVersion SymptomInformationVersion,
    ClinicalPathwayCode Pathway,
    ClinicalContentStatus ContentStatus,
    string SourceReference,
    DateTimeOffset ImportedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ActivatedAt,
    string? InformationalHeading,
    string? InformationalBody,
    IReadOnlyList<SymptomDiaryQuestionDefinition> Questions,
    IReadOnlyList<SymptomWarningSignDefinition> WarningSigns,
    SymptomDiarySha256? ExpectedContentHash = null);

public sealed record SymptomDiaryQuestionDefinition(
    SymptomDiaryCode Code,
    string PromptText,
    int SourceOrder,
    string AnswerSchemaJson,
    bool IsRequired,
    IReadOnlyList<SymptomDiaryQuestionOptionDefinition> Options);

public sealed record SymptomDiaryQuestionOptionDefinition(
    SymptomDiaryCode Code,
    string Value,
    string DisplayText,
    int SourceOrder);

public sealed record SymptomWarningSignDefinition(
    SymptomDiaryCode Code,
    string DisplayText,
    int SourceOrder);

public sealed record SymptomDiaryPackageContent(
    EntityId PackageVersionId,
    SymptomDiarySha256 CanonicalContentHash,
    SymptomDiaryPackageDefinition Definition);

public enum SymptomDiaryPackageImportOutcome
{
    Imported,
    AlreadyImported
}

public sealed record SymptomDiaryPackageImportResult(
    SymptomDiaryPackageImportOutcome Outcome,
    EntityId PackageVersionId,
    SymptomDiaryCode PackageCode,
    DefinitionVersion PackageVersion,
    SymptomDiarySha256 CanonicalContentHash);

public interface ISymptomDiaryContentImporter
{
    Task<SymptomDiaryPackageImportResult> ImportAsync(
        SymptomDiaryPackageDefinition package,
        CancellationToken cancellationToken = default);
}

public interface ISymptomDiaryContentProvider
{
    Task<SymptomDiaryPackageContent?> GetActivePackageAsync(
        ClinicalPathwayCode pathway,
        CancellationToken cancellationToken = default);

    Task<SymptomDiaryPackageContent?> GetExactPackageAsync(
        EntityId packageVersionId,
        CancellationToken cancellationToken = default);
}

public sealed class SymptomDiaryPackageValidationException(string message)
    : InvalidOperationException(message);

public sealed class SymptomDiaryPackageConflictException(string message)
    : InvalidOperationException(message);

public sealed class SymptomDiaryPackageIntegrityException(string message)
    : InvalidOperationException(message);
