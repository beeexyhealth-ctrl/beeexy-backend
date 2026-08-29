using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;

namespace Beeexy.Api.ClinicDirectory;

internal static class ClinicDirectoryEndpointExtensions
{
    private static readonly HashSet<string> SupportedListQueryParameters =
    [
        "cursor",
        "pageSize",
        "code",
        "locality",
        "administrativeArea",
        "country"
    ];

    public static IEndpointRouteBuilder MapBeeexyClinicDirectoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/clinics", ListClinicsAsync)
            .WithName("ListClinics")
            .WithTags("Clinic Directory")
            .WithDescription(
                "Lists published clinics from Beeexy's product-approved synthetic demo " +
                "directory using opaque cursor pagination (default page size 20, maximum " +
                "100). Optional code, locality, administrativeArea, and country filters " +
                "are exact stored-value matches. These demo records are not authoritative " +
                "healthcare-provider data and do not represent current availability, " +
                "insurance acceptance, ratings, reviews, or external verification.")
            .AllowAnonymous()
            .Produces<ClinicDirectoryPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/clinics/{id:guid}", GetClinicAsync)
            .WithName("GetClinic")
            .WithTags("Clinic Directory")
            .WithDescription(
                "Returns one published clinic and only its published stored locations from " +
                "Beeexy's product-approved synthetic demo directory. Missing and " +
                "unpublished clinics both return the same 404. These demo records are not " +
                "authoritative healthcare-provider data and do not imply current " +
                "availability, insurance acceptance, ratings, reviews, or external " +
                "verification.")
            .AllowAnonymous()
            .Produces<ClinicDirectoryDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ListClinicsAsync(
        HttpRequest request,
        string? cursor,
        int? pageSize,
        string? code,
        string? locality,
        string? administrativeArea,
        string? country,
        ListClinics useCase,
        CancellationToken cancellationToken)
    {
        ValidateQueryParameters(request.Query);
        var result = await useCase.ExecuteAsync(
            new ListClinicsQuery(
                cursor,
                pageSize,
                code,
                locality,
                administrativeArea,
                country),
            cancellationToken);

        return Results.Ok(new ClinicDirectoryPageResponse(
            result.Items.Select(item => new ClinicDirectoryItemResponse(
                item.ClinicId.Value,
                item.Code,
                item.Name)).ToArray(),
            result.NextCursor));
    }

    private static async Task<IResult> GetClinicAsync(
        Guid id,
        GetClinic useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ClinicNotFoundException();
        }

        var clinic = await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        return Results.Ok(new ClinicDirectoryDetailResponse(
            clinic.ClinicId.Value,
            clinic.Code,
            clinic.Name,
            clinic.Locations.Select(location => new ClinicDirectoryLocationResponse(
                location.LocationId.Value,
                location.Name,
                location.Locality,
                location.AdministrativeArea,
                location.Country,
                location.TimeZone)).ToArray()));
    }

    private static void ValidateQueryParameters(IQueryCollection query)
    {
        if (query.Keys.Any(key => !SupportedListQueryParameters.Contains(key)))
        {
            throw new RequestValidationException(
                "clinic_directory.filter_unsupported",
                "The clinic directory request contains an unsupported filter.");
        }

        if (query.Any(parameter => parameter.Value.Count != 1))
        {
            throw new RequestValidationException(
                "clinic_directory.filter_invalid",
                "Clinic directory query parameters cannot be repeated.");
        }
    }
}

internal sealed record ClinicDirectoryPageResponse(
    IReadOnlyList<ClinicDirectoryItemResponse> Items,
    string? NextCursor);

internal sealed record ClinicDirectoryItemResponse(
    Guid ClinicId,
    string Code,
    string Name);

internal sealed record ClinicDirectoryDetailResponse(
    Guid ClinicId,
    string Code,
    string Name,
    IReadOnlyList<ClinicDirectoryLocationResponse> Locations);

internal sealed record ClinicDirectoryLocationResponse(
    Guid LocationId,
    string Name,
    string Locality,
    string AdministrativeArea,
    string Country,
    string TimeZone);
