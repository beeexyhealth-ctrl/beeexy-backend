using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;

namespace Beeexy.Api.Interoperability;

internal static class FhirExportEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyFhirExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/patients/{patientId:guid}/fhir-exports",
                CreateAsync)
            .WithName("CreateFhirExport")
            .WithTags("FHIR Exports")
            .WithDescription(
                "Generates and validates the current server-owned FHIR R4 4.0.1 base " +
                "collection mapping from one authorized completed Clinical History event. " +
                "The request requires a UUID idempotency key scoped to the patient. A new " +
                "export returns 201 and a replay returns 200. Artifact bytes and private " +
                "storage metadata are never returned here.")
            .RequireAuthorization()
            .Accepts<CreateFhirExportRequest>("application/json")
            .Produces<FhirExportMetadataResponse>(StatusCodes.Status201Created)
            .Produces<FhirExportMetadataResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet(
                "/api/v1/fhir-exports/{id:guid}",
                GetAsync)
            .WithName("GetFhirExport")
            .WithTags("FHIR Exports")
            .WithDescription(
                "Returns privacy-safe lifecycle metadata after re-authorizing access to the " +
                "export's source patient. Missing and inaccessible exports both return a " +
                "concealed 404.")
            .RequireAuthorization()
            .Produces<FhirExportMetadataResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet(
                "/api/v1/fhir-exports/{id:guid}/content",
                DownloadAsync)
            .WithName("DownloadFhirExport")
            .WithTags("FHIR Exports")
            .WithDescription(
                "Returns the exact immutable bytes of an authorized, validated current R4 " +
                "export after SHA-256 integrity verification. Pending, generated, failed, " +
                "and legacy release-neutral artifacts return 409.")
            .RequireAuthorization()
            .Produces<byte[]>(
                StatusCodes.Status200OK,
                contentType: FhirR4BaseMvp.MediaType)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        Guid patientId,
        CreateFhirExportRequest request,
        CreateFhirExport useCase,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
        {
            throw new FhirExportNotFoundException();
        }

        if (request.AdditionalFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "fhir_export.unsupported_field",
                "The FHIR export request contains an unsupported field.");
        }

        if (request.SourceClinicalHistoryEventId == Guid.Empty ||
            request.IdempotencyKey == Guid.Empty)
        {
            throw new RequestValidationException(
                "fhir_export.identifiers_required",
                "A source Clinical History event ID and idempotency key are required.");
        }

        var result = await useCase.ExecuteAsync(
            new CreateFhirExportCommand(
                EntityId.From(patientId),
                EntityId.From(request.SourceClinicalHistoryEventId),
                EntityId.From(request.IdempotencyKey)),
            cancellationToken);
        var response = ToResponse(result.Metadata);
        return result.NewlyCreated
            ? Results.Created(
                $"/api/v1/fhir-exports/{result.Metadata.Id.Value:D}",
                response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetFhirExport useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new FhirExportNotFoundException();
        }

        return Results.Ok(ToResponse(await useCase.ExecuteAsync(
            EntityId.From(id),
            cancellationToken)));
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        DownloadFhirExport useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new FhirExportNotFoundException();
        }

        var result = await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        return Results.File(
            result.ArtifactBytes,
            result.MediaType,
            result.FileName,
            enableRangeProcessing: false);
    }

    private static FhirExportMetadataResponse ToResponse(FhirExportMetadata value) =>
        new(
            value.Id.Value,
            ToApiValue(value.Status),
            value.FhirVersion,
            value.MappingVersion,
            value.CreatedAt,
            value.GeneratedAt,
            value.ValidationCompletedAt,
            value.Validation is null
                ? null
                : new FhirExportValidationResponse(
                    ToApiValue(value.Validation.Outcome),
                    value.Validation.ErrorCount,
                    value.Validation.WarningCount,
                    value.Validation.CompletedAt));

    private static string ToApiValue(FhirExportStatus value) => value switch
    {
        FhirExportStatus.Pending => "Pending",
        FhirExportStatus.Generated => "Generated",
        FhirExportStatus.ValidationFailed => "ValidationFailed",
        FhirExportStatus.Validated => "Validated",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToApiValue(FhirValidationOutcome value) => value switch
    {
        FhirValidationOutcome.Failed => "Failed",
        FhirValidationOutcome.Passed => "Passed",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal sealed record CreateFhirExportRequest
{
    public Guid SourceClinicalHistoryEventId { get; init; }

    public Guid IdempotencyKey { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record FhirExportMetadataResponse(
    Guid Id,
    string Status,
    string FhirVersion,
    string MappingVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? ValidationCompletedAt,
    FhirExportValidationResponse? Validation);

internal sealed record FhirExportValidationResponse(
    string Outcome,
    int ErrorCount,
    int WarningCount,
    DateTimeOffset CompletedAt);
