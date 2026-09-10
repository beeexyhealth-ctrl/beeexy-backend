using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Api.Sharing;

internal static class ExportEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/patients/{id:guid}/exports", CreateAsync)
            .WithName("CreateExport")
            .WithTags("Exports")
            .WithDescription(
                "Creates one immutable private Beeexy JSON artifact from the authenticated " +
                "account's own Primary Patient canonical FullProfile snapshot. A first " +
                "creation returns 201 and an exact idempotent replay returns 200. PDF and " +
                "FHIR JSON remain unavailable with 422; content download is not exposed.")
            .RequireAuthorization()
            .Accepts<CreateExportRequest>("application/json")
            .Produces<ExportArtifactResponse>(StatusCodes.Status201Created)
            .Produces<ExportArtifactResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        Guid id,
        CreateExportRequest request,
        HttpResponse response,
        GenerateExport useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ExportPatientNotFoundException();
        }

        if (request.AdditionalFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "sharing.export_unsupported_field",
                "The export request contains an unsupported field.");
        }

        if (request.IdempotencyKey == Guid.Empty)
        {
            throw new RequestValidationException(
                "sharing.export_idempotency_key_required",
                "A non-empty idempotency key is required.");
        }

        var result = await useCase.ExecuteAsync(
            new GenerateExportCommand(
                EntityId.From(id),
                ParseFormat(request.Format),
                EntityId.From(request.IdempotencyKey)),
            cancellationToken);
        response.Headers.CacheControl = "no-store";
        var resultResponse = ToResponse(result.Artifact);
        return result.NewlyCreated
            ? Results.Json(resultResponse, statusCode: StatusCodes.Status201Created)
            : Results.Ok(resultResponse);
    }

    private static ExportArtifactFormat ParseFormat(string? value) => value switch
    {
        "BeeexyJson" => ExportArtifactFormat.BeeexyJson,
        "Pdf" => ExportArtifactFormat.Pdf,
        "FhirJson" => ExportArtifactFormat.FhirJson,
        _ => throw new RequestValidationException(
            "sharing.export_format_invalid",
            "The format must be BeeexyJson, Pdf, or FhirJson.")
    };

    private static ExportArtifactResponse ToResponse(ExportArtifact artifact) => new(
        artifact.Id.Value,
        artifact.Format switch
        {
            ExportArtifactFormat.BeeexyJson => "BeeexyJson",
            ExportArtifactFormat.Pdf => "Pdf",
            ExportArtifactFormat.FhirJson => "FhirJson",
            _ => throw new ArgumentOutOfRangeException(nameof(artifact))
        },
        artifact.MediaType,
        artifact.ChecksumAlgorithm!,
        artifact.Checksum!,
        artifact.SnapshotVersion,
        artifact.CreatedAt,
        artifact.CompletedAt!.Value,
        artifact.RetentionEligibleAt,
        artifact.Status switch
        {
            ExportArtifactStatus.Available => "Available",
            _ => throw new ExportArtifactStateConflictException()
        });
}

internal sealed record CreateExportRequest
{
    public string? Format { get; init; }

    public Guid IdempotencyKey { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record ExportArtifactResponse(
    Guid ExportArtifactId,
    string Format,
    string MediaType,
    string ChecksumAlgorithm,
    string Checksum,
    string SnapshotVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset RetentionEligibleAt,
    string Status);
