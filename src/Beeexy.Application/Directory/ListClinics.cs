using Beeexy.Application.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Application.Directory;

public sealed class ListClinics(IClinicDirectoryReadRepository repository)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public async Task<ListClinicsResult> ExecuteAsync(
        ListClinicsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filter = new ClinicDirectoryFilter(
            NormalizeCode(query.Code),
            NormalizeLocationPart(query.Locality, "locality"),
            NormalizeLocationPart(query.AdministrativeArea, "administrativeArea"),
            NormalizeLocationPart(query.Country, "country"));
        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "clinic_directory.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var cursor = query.Cursor is null
            ? null
            : ClinicDirectoryCursorCodec.Decode(query.Cursor, filter);
        if (cursor is not null &&
            !await repository.CursorExistsAsync(cursor, cancellationToken))
        {
            throw ClinicDirectoryCursorCodec.CreateInvalidCursorException();
        }

        var page = await repository.ListAsync(
            filter,
            cursor,
            pageSize + 1,
            cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? ClinicDirectoryCursorCodec.Encode(new ClinicDirectoryPageCursor(
                filter,
                items[^1].ClinicId))
            : null;

        return new ListClinicsResult(items, nextCursor);
    }

    private static string? NormalizeCode(string? value)
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
            throw new RequestValidationException(
                "clinic_directory.filter_invalid",
                "The code filter is invalid.");
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
            throw new RequestValidationException(
                "clinic_directory.filter_invalid",
                $"The {filterName} filter is invalid.");
        }

        return candidate;
    }
}

public sealed record ListClinicsQuery(
    string? Cursor = null,
    int? PageSize = null,
    string? Code = null,
    string? Locality = null,
    string? AdministrativeArea = null,
    string? Country = null);

public sealed record ListClinicsResult(
    IReadOnlyList<ClinicDirectoryListItem> Items,
    string? NextCursor);
