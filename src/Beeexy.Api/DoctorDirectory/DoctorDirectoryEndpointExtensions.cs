using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using System.Text.Json.Serialization;

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
                "directory using opaque criteria-bound cursor pagination (default page size " +
                "20, maximum 100). Without search criteria, results retain neutral UUID order " +
                "and omit match data. Supplying any specialtyCode, languageCode, locality, " +
                "administrativeArea, country, or insurancePlanCode activates deterministic " +
                "demo matching with rule version 2026.08.29-demo.1 after all supplied criteria " +
                "have constrained the candidate set as exact hard filters. Location parts must " +
                "match one eligible stored affiliation location. Matched results expose a " +
                "structured matchScore and factor explanations, order by score descending then " +
                "canonical doctor UUID text ascending, and use a ranked cursor bound to the " +
                "criteria, score boundary, and exact rule version. The score is deterministic " +
                "demo/MVP logic: it is not a probability, confidence, provider-quality measure, " +
                "medical suitability assessment, clinically validated result, or production " +
                "recommendation. Insurance matching means exact stored synthetic participation " +
                "only, not current eligibility or coverage, payer confirmation, or real-time " +
                "in-network status. Location matching is exact stored data only, with no " +
                "distance or geocoding. These records are not authoritative professional data.")
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
            result.Items.Select(ToSearchResponse).ToArray(),
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

    private static DoctorDirectorySearchItemResponse ToSearchResponse(
        DoctorDirectorySearchItem item)
    {
        var doctor = ToResponse(item.Profile);
        return new DoctorDirectorySearchItemResponse(
            doctor.DoctorId,
            doctor.Code,
            doctor.DisplayName,
            doctor.Specialties,
            doctor.Languages,
            doctor.Affiliations,
            doctor.StoredInsuranceParticipations,
            doctor.Credentials,
            item.Match is null
                ? null
                : new DoctorDirectoryMatchResponse(
                    item.Match.RuleVersion,
                    item.Match.MatchScore,
                    item.Match.Factors.Select(factor =>
                        new DoctorDirectoryMatchFactorResponse(
                            factor.FactorCode,
                            factor.SemanticsCode,
                            factor.WeightPoints,
                            ToStateCode(factor.State),
                            factor.ContributionPoints,
                            factor.ExplanationCode,
                            factor.ExplanationData.Select(value =>
                                new DoctorDirectoryMatchExplanationValueResponse(
                                    value.Key,
                                    value.Value)).ToArray())).ToArray()));
    }

    private static string ToStateCode(DoctorMatchFactorState state) => state switch
    {
        DoctorMatchFactorState.Matched => "matched",
        DoctorMatchFactorState.NotMatched => "not_matched",
        DoctorMatchFactorState.NotApplicable => "not_applicable",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

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
    IReadOnlyList<DoctorDirectorySearchItemResponse> Items,
    string? NextCursor);

internal sealed record DoctorDirectorySearchItemResponse(
    Guid DoctorId,
    string Code,
    string DisplayName,
    IReadOnlyList<DoctorDirectoryCatalogValueResponse> Specialties,
    IReadOnlyList<DoctorDirectoryCatalogValueResponse> Languages,
    IReadOnlyList<DoctorDirectoryAffiliationResponse> Affiliations,
    IReadOnlyList<DoctorDirectoryCatalogValueResponse> StoredInsuranceParticipations,
    IReadOnlyList<DoctorDirectoryCredentialResponse> Credentials,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DoctorDirectoryMatchResponse? Match);

internal sealed record DoctorDirectoryMatchResponse(
    string RuleVersion,
    int MatchScore,
    IReadOnlyList<DoctorDirectoryMatchFactorResponse> Factors);

internal sealed record DoctorDirectoryMatchFactorResponse(
    string FactorCode,
    string SemanticsVersion,
    int ConfiguredWeightPoints,
    string State,
    int ContributionPoints,
    string ExplanationCode,
    IReadOnlyList<DoctorDirectoryMatchExplanationValueResponse> ExplanationData);

internal sealed record DoctorDirectoryMatchExplanationValueResponse(string Key, string Value);

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
