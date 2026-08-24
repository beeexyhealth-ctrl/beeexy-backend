using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;

namespace Beeexy.Application.Interoperability;

public enum FhirValidationDiagnosticSeverity
{
    Error = 1,
    Warning = 2
}

public sealed record FhirValidatorDiagnostic(
    FhirValidationDiagnosticSeverity Severity,
    string Code,
    string? Detail);

public sealed record FhirValidationDiagnosticSummary(
    int ErrorCount,
    int WarningCount,
    string Summary,
    IReadOnlyList<string> Codes)
{
    public static FhirValidationDiagnosticSummary None { get; } =
        new(0, 0, "No validation diagnostics are available.", []);
}

public sealed class FhirValidationDiagnosticSanitizer
{
    public FhirValidationDiagnosticSummary Sanitize(
        IReadOnlyList<FhirValidatorDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var errorCount = diagnostics.Count(diagnostic =>
            diagnostic.Severity == FhirValidationDiagnosticSeverity.Error);
        var warningCount = diagnostics.Count(diagnostic =>
            diagnostic.Severity == FhirValidationDiagnosticSeverity.Warning);
        var codes = new List<string>(2);
        if (errorCount != 0)
        {
            codes.Add("fhir-validation-error");
        }

        if (warningCount != 0)
        {
            codes.Add("fhir-validation-warning");
        }

        return new FhirValidationDiagnosticSummary(
            errorCount,
            warningCount,
            $"FHIR validation completed with {errorCount} error(s) and " +
                $"{warningCount} warning(s).",
            codes.AsReadOnly());
    }
}

public enum FhirValidatorExecutionStatus
{
    Valid = 1,
    Invalid = 2,
    Unavailable = 3,
    UnsupportedSpecification = 4
}

public sealed record FhirValidatorExecutionResult
{
    private FhirValidatorExecutionResult(
        FhirValidatorExecutionStatus status,
        FhirValidatorMetadata? validator,
        IReadOnlyList<FhirValidatorDiagnostic> diagnostics)
    {
        Status = status;
        Validator = validator;
        Diagnostics = diagnostics;
    }

    public FhirValidatorExecutionStatus Status { get; }

    public FhirValidatorMetadata? Validator { get; }

    public IReadOnlyList<FhirValidatorDiagnostic> Diagnostics { get; }

    public static FhirValidatorExecutionResult Valid(
        FhirValidatorMetadata validator,
        IEnumerable<FhirValidatorDiagnostic>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var diagnostics = Snapshot(warnings);
        if (diagnostics.Any(diagnostic =>
            diagnostic.Severity == FhirValidationDiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A valid validator result cannot contain errors.",
                nameof(warnings));
        }

        return new FhirValidatorExecutionResult(
            FhirValidatorExecutionStatus.Valid,
            validator,
            diagnostics);
    }

    public static FhirValidatorExecutionResult Invalid(
        FhirValidatorMetadata validator,
        IEnumerable<FhirValidatorDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var values = Snapshot(diagnostics);
        if (!values.Any(diagnostic =>
            diagnostic.Severity == FhirValidationDiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "An invalid validator result requires at least one error.",
                nameof(diagnostics));
        }

        return new FhirValidatorExecutionResult(
            FhirValidatorExecutionStatus.Invalid,
            validator,
            values);
    }

    public static FhirValidatorExecutionResult Unavailable() => new(
        FhirValidatorExecutionStatus.Unavailable,
        null,
        []);

    public static FhirValidatorExecutionResult UnsupportedSpecification() => new(
        FhirValidatorExecutionStatus.UnsupportedSpecification,
        null,
        []);

    private static IReadOnlyList<FhirValidatorDiagnostic> Snapshot(
        IEnumerable<FhirValidatorDiagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return [];
        }

        var values = diagnostics.ToArray();
        if (values.Any(diagnostic => diagnostic is null ||
            !Enum.IsDefined(diagnostic.Severity)))
        {
            throw new ArgumentException(
                "Validation diagnostics contain an invalid value.",
                nameof(diagnostics));
        }

        return Array.AsReadOnly(values);
    }
}

public sealed record FhirValidatorRequest(
    EntityId ExportId,
    ReadOnlyMemory<byte> ArtifactBytes,
    string ArtifactChecksumAlgorithm,
    string ArtifactChecksum,
    FhirValidationSpecification Specification);

public interface IFhirValidator
{
    Task<FhirValidatorExecutionResult> ValidateAsync(
        FhirValidatorRequest request,
        CancellationToken cancellationToken = default);
}
