using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;

namespace Beeexy.Api.DoctorDirectory;

internal static class DoctorDirectoryEndpointExtensions
{
    private static readonly HashSet<string> SupportedSearchQueryParameters =
    [
        "cursor",
        "pageSize",
        "specialtyCode",
        "languageCode",
        "locality",
        "administrativeArea",
        "country",
        "insurancePlanCode"
    ];

    public static IEndpointRouteBuilder MapBeeexyDoctorDirectoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/doctors", SearchDoctorsAsync)
            .WithName("SearchDoctors")
            .WithTags("Doctor Directory")
            .WithDescription(
                "Lists published doctors from Beeexy's product-approved synthetic demo " +
                "directory in neutral UUID order using opaque cursor pagination (default " +
                "page size 20, maximum 100). specialtyCode, languageCode, locality, " +
                "administrativeArea, country, and insurancePlanCode use exact stored-value " +
                "matches with intersection semantics. Location parts must match one eligible " +
                "stored affiliation location. Insurance participation is stored demo data " +
                "only, not current coverage, payer confirmation, or real-time network " +
                "membership. Results are not rankings or recommendations, and these synthetic " +
                "records are not authoritative or externally verified professional data.")
            .AllowAnonymous()
            .Produces<DoctorDirectoryPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/doctors/{id:guid}", GetDoctorAsync)
            .WithName("GetDoctor")
            .WithTags("Doctor Directory")
            .WithDescription(
                "Returns one published doctor from Beeexy's product-approved synthetic demo " +
                "directory with exact stored specialties, languages, eligible affiliations " +
                "and locations, stored insurance participation, and credentials verified only " +
                "within the approved demo dataset. Missing and unpublished doctors both return " +
                "the same 404. The profile is not authoritative professional data, an external " +
                "credential verification, a live insurance statement, or a recommendation.")
            .AllowAnonymous()
            .Produces<DoctorDirectoryProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> SearchDoctorsAsync(
        HttpRequest request,
        string? cursor,
        int? pageSize,
        string? specialtyCode,
        string? languageCode,
        string? locality,
        string? administrativeArea,
        string? country,
        string? insurancePlanCode,
        SearchDoctors useCase,
        CancellationToken cancellationToken)
    {
        ValidateQueryParameters(request.Query);
        var result = await useCase.ExecuteAsync(
            new SearchDoctorsQuery(
                cursor,
                pageSize,
                specialtyCode,
                languageCode,
                locality,
                administrativeArea,
                country,
                insurancePlanCode),
            cancellationToken);

        return Results.Ok(new DoctorDirectoryPageResponse(
            result.Items.Select(ToResponse).ToArray(),
            result.NextCursor));
    }

    private static async Task<IResult> GetDoctorAsync(
        Guid id,
        GetDoctor useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new DoctorNotFoundException();
        }

        var doctor = await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        return Results.Ok(ToResponse(doctor));
    }

    private static DoctorDirectoryProfileResponse ToResponse(DoctorDirectoryProfile doctor) =>
        new(
            doctor.DoctorId.Value,
            doctor.Code,
            doctor.DisplayName,
            doctor.Specialties.Select(value => new DoctorDirectoryCatalogValueResponse(
                value.Code,
                value.Name)).ToArray(),
            doctor.Languages.Select(value => new DoctorDirectoryCatalogValueResponse(
                value.Code,
                value.Name)).ToArray(),
            doctor.Affiliations.Select(value => new DoctorDirectoryAffiliationResponse(
                value.ClinicId.Value,
                value.ClinicCode,
                value.ClinicName,
                value.Location is null
                    ? null
                    : new DoctorDirectoryLocationResponse(
                        value.Location.LocationId.Value,
                        value.Location.Name,
                        value.Location.Locality,
                        value.Location.AdministrativeArea,
                        value.Location.Country,
                        value.Location.TimeZone))).ToArray(),
            doctor.StoredInsuranceParticipations.Select(value =>
                new DoctorDirectoryCatalogValueResponse(value.Code, value.Name)).ToArray(),
            doctor.Credentials.Select(value =>
                new DoctorDirectoryCredentialResponse(value.Name)).ToArray());

    private static void ValidateQueryParameters(IQueryCollection query)
    {
        if (query.Keys.Any(key => !SupportedSearchQueryParameters.Contains(key)))
        {
            throw new RequestValidationException(
                "doctor_directory.filter_unsupported",
                "The doctor directory request contains an unsupported filter.");
        }

        if (query.Any(parameter => parameter.Value.Count != 1))
        {
            throw new RequestValidationException(
                "doctor_directory.filter_invalid",
                "Doctor directory query parameters cannot be repeated.");
        }
    }
}

internal sealed record DoctorDirectoryPageResponse(
    IReadOnlyList<DoctorDirectoryProfileResponse> Items,
    string? NextCursor);

internal sealed record DoctorDirectoryProfileResponse(
    Guid DoctorId,
    string Code,
    string DisplayName,
    IReadOnlyList<DoctorDirectoryCatalogValueResponse> Specialties,
    IReadOnlyList<DoctorDirectoryCatalogValueResponse> Languages,
    IReadOnlyList<DoctorDirectoryAffiliationResponse> Affiliations,
    IReadOnlyList<DoctorDirectoryCatalogValueResponse> StoredInsuranceParticipations,
    IReadOnlyList<DoctorDirectoryCredentialResponse> Credentials);

internal sealed record DoctorDirectoryCatalogValueResponse(string Code, string Name);

internal sealed record DoctorDirectoryAffiliationResponse(
    Guid ClinicId,
    string ClinicCode,
    string ClinicName,
    DoctorDirectoryLocationResponse? Location);

internal sealed record DoctorDirectoryLocationResponse(
    Guid LocationId,
    string Name,
    string Locality,
    string AdministrativeArea,
    string Country,
    string TimeZone);

internal sealed record DoctorDirectoryCredentialResponse(string Name);
