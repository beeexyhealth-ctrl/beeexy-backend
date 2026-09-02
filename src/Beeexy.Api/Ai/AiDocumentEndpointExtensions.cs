using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Beeexy.Api.Ai;

internal static class AiDocumentEndpointExtensions
{
    private const long MultipartRequestCeiling =
        AiDocumentOptions.MaximumAllowedBytes + 1_048_576;

    public static IEndpointRouteBuilder MapBeeexyAiDocumentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/ai/documents", UploadAsync)
            .WithName("UploadAiDocument")
            .WithTags("AI Documents")
            .WithDescription(
                "Uploads one private, temporary text-native PDF or UTF-8 TXT document. " +
                "The exact file limit is 26,214,400 bytes; OCR is not performed; accepted " +
                "artifacts expire no later than 24 hours after upload.")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithMetadata(new RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = MultipartRequestCeiling
            })
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<AiDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapDelete("/api/v1/ai/documents/{id:guid}", DeleteAsync)
            .WithName("DeleteAiDocument")
            .WithTags("AI Documents")
            .WithDescription(
                "Physically deletes an owner-uploaded temporary artifact and retains only " +
                "minimal lifecycle metadata. Repeated owner deletion is idempotent.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> UploadAsync(
        IFormFile file,
        HttpRequest request,
        AiDocumentOptions options,
        UploadAiDocument useCase,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.Count != 1 || form.Count != 0)
        {
            throw new AiDocumentValidationException(
                "ai.document.single_file_required",
                "Upload exactly one document per request.");
        }

        if (file.Length > options.MaximumBytes)
        {
            throw new AiDocumentTooLargeException();
        }

        var content = await ReadBoundedAsync(file, options.MaximumBytes, cancellationToken);
        var result = await useCase.ExecuteAsync(
            new UploadAiDocumentCommand(
                Path.GetFileName(file.FileName),
                file.ContentType,
                file.Length,
                content),
            cancellationToken);
        return Results.Created(
            $"/api/v1/ai/documents/{result.DocumentId.Value:D}",
            ToResponse(result));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteAiDocument useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new AiDocumentNotFoundException();
        }

        await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<byte[]> ReadBoundedAsync(
        IFormFile file,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)Math.Min(file.Length, maximumBytes));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new AiDocumentTooLargeException();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static AiDocumentResponse ToResponse(AiDocumentMetadata metadata) => new(
        metadata.DocumentId.Value,
        metadata.ContentType,
        metadata.SizeBytes,
        metadata.UploadedAt,
        metadata.ExpiresAt,
        metadata.Status switch
        {
            AiDocumentStatus.Active => "active",
            AiDocumentStatus.Deleted => "deleted",
            _ => "expired"
        });
}

internal sealed record AiDocumentResponse(
    Guid DocumentId,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    DateTimeOffset ExpiresAt,
    string Status);
