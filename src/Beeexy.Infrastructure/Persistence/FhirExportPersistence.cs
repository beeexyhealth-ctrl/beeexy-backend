using Beeexy.Domain.Interoperability;

namespace Beeexy.Infrastructure.Persistence;

internal static class FhirExportPersistence
{
    public static string StoreStatus(FhirExportStatus status)
    {
        return status switch
        {
            FhirExportStatus.Pending => "pending",
            FhirExportStatus.Generated => "generated",
            FhirExportStatus.ValidationFailed => "validation_failed",
            FhirExportStatus.Validated => "validated",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    public static FhirExportStatus LoadStatus(string value)
    {
        return value switch
        {
            "pending" => FhirExportStatus.Pending,
            "generated" => FhirExportStatus.Generated,
            "validation_failed" => FhirExportStatus.ValidationFailed,
            "validated" => FhirExportStatus.Validated,
            _ => throw new InvalidOperationException($"Unsupported FHIR export status '{value}'.")
        };
    }

    public static string StoreValidationOutcome(FhirValidationOutcome outcome)
    {
        return outcome switch
        {
            FhirValidationOutcome.Failed => "failed",
            FhirValidationOutcome.Passed => "passed",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    public static FhirValidationOutcome LoadValidationOutcome(string value)
    {
        return value switch
        {
            "failed" => FhirValidationOutcome.Failed,
            "passed" => FhirValidationOutcome.Passed,
            _ => throw new InvalidOperationException(
                $"Unsupported FHIR validation outcome '{value}'.")
        };
    }
}
