using Beeexy.Application.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Application.Directory;

public sealed class SearchDoctors(IDoctorDirectoryReadRepository repository)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public async Task<SearchDoctorsResult> ExecuteAsync(
        SearchDoctorsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filter = new DoctorDirectoryFilter(
            NormalizeCode(query.SpecialtyCode, "specialtyCode"),
            NormalizeCode(query.LanguageCode, "languageCode"),
            NormalizeLocationPart(query.Locality, "locality"),
            NormalizeLocationPart(query.AdministrativeArea, "administrativeArea"),
            NormalizeLocationPart(query.Country, "country"),
            NormalizeCode(query.InsurancePlanCode, "insurancePlanCode"));
        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "doctor_directory.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var cursor = query.Cursor is null
            ? null
            : DirectoryCursorCodec.DecodeDoctor(query.Cursor, filter);
        if (cursor is not null &&
            !await repository.CursorExistsAsync(cursor, cancellationToken))
        {
            throw DirectoryCursorCodec.CreateInvalidDoctorCursorException();
        }

        var page = await repository.SearchAsync(
            filter,
            cursor,
            pageSize + 1,
            cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? DirectoryCursorCodec.EncodeDoctor(new DoctorDirectoryPageCursor(
                filter,
                items[^1].DoctorId))
            : null;

        return new SearchDoctorsResult(items, nextCursor);
    }

    private static string? NormalizeCode(string? value, string filterName)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return DirectoryCode.Create(value).Value;
        }
        catch (ArgumentException)
        {
            throw InvalidFilter(filterName);
        }
    }

    private static string? NormalizeLocationPart(string? value, string filterName)
    {
        if (value is null)
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.Length is 0 or > ClinicLocation.MaximumLocationPartLength)
        {
            throw InvalidFilter(filterName);
        }

        return candidate;
    }

    private static RequestValidationException InvalidFilter(string filterName) =>
        new(
            "doctor_directory.filter_invalid",
            $"The {filterName} filter is invalid.");
}

public sealed record SearchDoctorsQuery(
    string? Cursor = null,
    int? PageSize = null,
    string? SpecialtyCode = null,
    string? LanguageCode = null,
    string? Locality = null,
    string? AdministrativeArea = null,
    string? Country = null,
    string? InsurancePlanCode = null);

public sealed record SearchDoctorsResult(
    IReadOnlyList<DoctorDirectoryProfile> Items,
    string? NextCursor);
