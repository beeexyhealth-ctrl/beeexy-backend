using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Api.Sharing;

internal static class ShareEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexySharingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/shares", CreateAsync)
            .WithName("CreateShare")
            .WithTags("Sharing")
            .WithDescription(
                "Creates an expiring external share for the authenticated account's own " +
                "Primary Patient. First creation returns 201 and discloses one capability " +
                "in a configured frontend URL fragment. An exact idempotent replay returns " +
                "200 without capability or URL; Case and Visit remain unavailable.")
            .RequireAuthorization()
            .Accepts<CreateShareRequest>("application/json")
            .Produces<CreateShareResponse>(StatusCodes.Status201Created)
            .Produces<CreateShareResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/shares", ListAsync)
            .WithName("ListShares")
            .WithTags("Sharing")
            .WithDescription(
                "Lists deterministic, secret-free metadata for the authenticated account's " +
                "own Primary Patient shares. Status is derived from revocation, expiry, and " +
                "the authoritative server clock.")
            .RequireAuthorization()
            .Produces<ShareListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateShareRequest request,
        CreateShare useCase,
        CancellationToken cancellationToken)
    {
        ValidateRequestShape(request);
        var items = (request.Items ?? [])
            .Select(item => new ShareItemReference(
                CreateResourceType(item!.ResourceType),
                EntityId.From(item.ResourceId)))
            .ToArray();
        var result = await useCase.ExecuteAsync(
            new CreateShareCommand(
                ParseScope(request.Scope),
                request.LifetimeMinutes,
                EntityId.From(request.IdempotencyKey),
                items),
            cancellationToken);
        var response = ToCreateResponse(result);
        return result.NewlyCreated
            ? Results.Created("/api/v1/shares", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        ListShares useCase,
        CancellationToken cancellationToken)
    {
        if (request.Query.Count != 0)
        {
            throw new RequestValidationException(
                "sharing.list_filter_unsupported",
                "The share list does not accept query parameters.");
        }

        var shares = await useCase.ExecuteAsync(cancellationToken);
        return Results.Ok(new ShareListResponse(shares.Select(ToListResponse).ToArray()));
    }

    private static void ValidateRequestShape(CreateShareRequest request)
    {
        if (request.AdditionalFields is { Count: > 0 } ||
            request.Items?.Any(item =>
                item is null || item.AdditionalFields is { Count: > 0 }) == true)
        {
            throw new RequestValidationException(
                "sharing.unsupported_field",
                "The share request contains an unsupported field.");
        }

        if (request.IdempotencyKey == Guid.Empty)
        {
            throw new RequestValidationException(
                "sharing.idempotency_key_required",
                "A non-empty idempotency key is required.");
        }

        if (request.Items?.Any(item => item is null || item.ResourceId == Guid.Empty) == true)
        {
            throw new RequestValidationException(
                "sharing.item_invalid",
                "Every share item requires a non-empty resource identifier.");
        }
    }

    private static ShareScope ParseScope(string? value) => value switch
    {
        "FullProfile" => ShareScope.FullProfile,
        "Case" => ShareScope.Case,
        "PreTriage" => ShareScope.PreTriage,
        "Visit" => ShareScope.Visit,
        "SpecificRecords" => ShareScope.SpecificRecords,
        _ => throw new RequestValidationException(
            "sharing.scope_invalid",
            "The scope must be FullProfile, PreTriage, or SpecificRecords; " +
            "reserved Case and Visit values remain unavailable.")
    };

    private static ShareResourceType CreateResourceType(string? value)
    {
        try
        {
            return ShareResourceType.Create(value ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException(
                "sharing.item_type_invalid",
                "Every share item must use a supported stable resource type.");
        }
    }

    private static CreateShareResponse ToCreateResponse(CreateShareResult result) => new(
        result.Share.ShareGrantId.Value,
        ToApiValue(result.Share.Scope),
        result.Share.CreatedAt,
        result.Share.ExpiresAt,
        result.Share.ItemCount,
        CapabilityPreviouslyIssued: !result.NewlyCreated,
        result.Capability,
        result.ShareUrl);

    private static ShareListItemResponse ToListResponse(ShareSummary value) => new(
        value.ShareGrantId.Value,
        ToApiValue(value.Scope),
        ToApiValue(value.Status),
        value.CreatedAt,
        value.ExpiresAt,
        value.RevokedAt,
        value.ItemCount);

    private static string ToApiValue(ShareScope value) => value switch
    {
        ShareScope.FullProfile => "FullProfile",
        ShareScope.PreTriage => "PreTriage",
        ShareScope.SpecificRecords => "SpecificRecords",
        ShareScope.Case => "Case",
        ShareScope.Visit => "Visit",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToApiValue(ShareGrantStatus value) => value switch
    {
        ShareGrantStatus.Active => "Active",
        ShareGrantStatus.Revoked => "Revoked",
        ShareGrantStatus.Expired => "Expired",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal sealed record CreateShareRequest
{
    public string? Scope { get; init; }

    public int? LifetimeMinutes { get; init; }

    public Guid IdempotencyKey { get; init; }

    public IReadOnlyList<CreateShareItemRequest?>? Items { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record CreateShareItemRequest
{
    public string? ResourceType { get; init; }

    public Guid ResourceId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record CreateShareResponse(
    Guid ShareGrantId,
    string Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int ItemCount,
    bool CapabilityPreviouslyIssued,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Capability,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ShareUrl);

internal sealed record ShareListResponse(IReadOnlyList<ShareListItemResponse> Shares);

internal sealed record ShareListItemResponse(
    Guid ShareGrantId,
    string Scope,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? RevokedAt,
    int ItemCount);
