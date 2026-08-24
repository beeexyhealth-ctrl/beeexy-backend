using Beeexy.Domain.Common;

namespace Beeexy.Application.Interoperability;

public enum FhirConceptualResource
{
    QuestionnaireResponse = 1,
    RiskAssessment = 2,
    Device = 3,
    Provenance = 4
}

public interface IFhirMapper<in TInput, out TRepresentation>
{
    TRepresentation Map(TInput input);
}

public sealed record FhirLogicalResourceIdentity
{
    public const int MaximumLogicalIdLength = 256;

    private FhirLogicalResourceIdentity(
        FhirConceptualResource resource,
        string logicalId)
    {
        Resource = resource;
        LogicalId = logicalId;
    }

    public FhirConceptualResource Resource { get; }

    public string LogicalId { get; }

    public static FhirLogicalResourceIdentity Create(
        FhirConceptualResource resource,
        string logicalId)
    {
        if (!Enum.IsDefined(resource))
        {
            throw new ArgumentOutOfRangeException(nameof(resource));
        }

        return new FhirLogicalResourceIdentity(
            resource,
            MappingText.Required(
                logicalId,
                MaximumLogicalIdLength,
                nameof(logicalId)));
    }
}

public sealed record FhirGenerationTrace
{
    private FhirGenerationTrace(
        EntityId exportId,
        FhirLogicalResourceIdentity questionnaireResponse,
        FhirLogicalResourceIdentity riskAssessment,
        FhirLogicalResourceIdentity device,
        FhirLogicalResourceIdentity provenance,
        DateTimeOffset recordedAt)
    {
        ExportId = exportId;
        QuestionnaireResponse = questionnaireResponse;
        RiskAssessment = riskAssessment;
        Device = device;
        Provenance = provenance;
        RecordedAt = recordedAt;
    }

    public EntityId ExportId { get; }

    public FhirLogicalResourceIdentity QuestionnaireResponse { get; }

    public FhirLogicalResourceIdentity RiskAssessment { get; }

    public FhirLogicalResourceIdentity Device { get; }

    public FhirLogicalResourceIdentity Provenance { get; }

    public DateTimeOffset RecordedAt { get; }

    public static FhirGenerationTrace Create(
        EntityId exportId,
        FhirLogicalResourceIdentity questionnaireResponse,
        FhirLogicalResourceIdentity riskAssessment,
        FhirLogicalResourceIdentity device,
        FhirLogicalResourceIdentity provenance,
        DateTimeOffset recordedAt)
    {
        EnsureNonEmpty(exportId, nameof(exportId));
        EnsureResource(
            questionnaireResponse,
            FhirConceptualResource.QuestionnaireResponse,
            nameof(questionnaireResponse));
        EnsureResource(
            riskAssessment,
            FhirConceptualResource.RiskAssessment,
            nameof(riskAssessment));
        EnsureResource(device, FhirConceptualResource.Device, nameof(device));
        EnsureResource(
            provenance,
            FhirConceptualResource.Provenance,
            nameof(provenance));
        EnsureUtc(recordedAt, nameof(recordedAt));

        return new FhirGenerationTrace(
            exportId,
            questionnaireResponse,
            riskAssessment,
            device,
            provenance,
            recordedAt);
    }

    private static void EnsureResource(
        FhirLogicalResourceIdentity value,
        FhirConceptualResource expected,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Resource != expected)
        {
            throw new ArgumentException(
                $"The logical identity must represent {expected}.",
                parameterName);
        }
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }

    internal static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The timestamp must be expressed in UTC.",
                parameterName);
        }
    }
}

public sealed class FhirMappingInputException : Exception
{
    public FhirMappingInputException(string message)
        : base(message)
    {
    }
}

internal static class MappingText
{
    public static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value must contain between 1 and {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
