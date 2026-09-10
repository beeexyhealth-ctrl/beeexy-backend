using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Interoperability;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;

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
                "Creates one immutable private Beeexy JSON, human-readable PDF, or validated " +
                "FHIR JSON artifact for the authenticated account's own Primary Patient. " +
                "A first creation returns 201 and an exact idempotent replay returns 200. " +
                "FHIR uses only the existing Phase 6 validated export pipeline.")
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

        endpoints.MapGet("/api/v1/exports/{id:guid}/content", DownloadAsync)
            .WithName("DownloadExport")
            .WithTags("Exports")
            .WithDescription(
                "Returns exact immutable stored artifact bytes after either current Bearer " +
                "patient authorization or current ShareAccess grant revalidation with an " +
                "explicit ExportArtifact item. FullProfile does not authorize historical " +
                "artifacts. Range processing is disabled and responses are not cached.")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes =
                    $"{JwtBearerDefaults.AuthenticationScheme}," +
                    ShareAccessAuthenticationDefaults.Scheme
            })
            .Produces<byte[]>(
                StatusCodes.Status200OK,
                BeeexyJsonExportRenderer.MediaType,
                PdfExportContract.MediaType,
                FhirR4BaseMvp.MediaType)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
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

    private static async Task<IResult> DownloadAsync(
        Guid id,
        HttpContext httpContext,
        DownloadExport useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ExportArtifactNotFoundException();
        }

        ShareAccessIdentity? shareAccess = null;
        if (ShareAccessAuthenticationDefaults.TryGetIdentity(
                httpContext.User,
                out var parsedShareAccess))
        {
            shareAccess = parsedShareAccess;
        }

        var result = await useCase.ExecuteAsync(
            new DownloadExportCommand(EntityId.From(id), shareAccess),
            cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(
            result.ArtifactBytes,
            result.MediaType,
            result.FileName,
            enableRangeProcessing: false);
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
