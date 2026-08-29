using Beeexy.Application.Common;

namespace Beeexy.Application.Directory;

public sealed class SearchDoctors(
    IDoctorDirectoryReadRepository repository,
    CalculateDoctorMatch calculateDoctorMatch)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public async Task<SearchDoctorsResult> ExecuteAsync(
        SearchDoctorsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filter = DoctorDirectoryInputNormalizer.NormalizeFilter(
            query.SpecialtyCode,
            query.LanguageCode,
            query.Locality,
            query.AdministrativeArea,
            query.Country,
            query.InsurancePlanCode,
            "doctor_directory.filter_invalid");
        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "doctor_directory.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        return HasMatchingCriteria(filter)
            ? await SearchRankedAsync(filter, query.Cursor, pageSize, cancellationToken)
            : await SearchNeutralAsync(filter, query.Cursor, pageSize, cancellationToken);
    }

    private async Task<SearchDoctorsResult> SearchNeutralAsync(
        DoctorDirectoryFilter filter,
        string? encodedCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var cursor = encodedCursor is null
            ? null
            : DirectoryCursorCodec.DecodeDoctor(encodedCursor, filter);
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
        var profiles = page.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? DirectoryCursorCodec.EncodeDoctor(new DoctorDirectoryPageCursor(
                filter,
                profiles[^1].DoctorId))
            : null;

        return new SearchDoctorsResult(
            profiles.Select(profile => new DoctorDirectorySearchItem(profile, null)).ToArray(),
            nextCursor);
    }

    private async Task<SearchDoctorsResult> SearchRankedAsync(
        DoctorDirectoryFilter filter,
        string? encodedCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var cursor = encodedCursor is null
            ? null
            : DirectoryCursorCodec.DecodeRankedDoctor(
                encodedCursor,
                filter,
                ProductApprovedDoctorMatchRule.Version);
        var candidateIds = await repository.ListFilteredDoctorIdsAsync(
            filter,
            cancellationToken);
        var calculation = await calculateDoctorMatch.ExecuteAsync(
            new CalculateDoctorMatchQuery(
                ProductApprovedDoctorMatchRule.Version,
                filter.SpecialtyCode,
                filter.LanguageCode,
                filter.Locality,
                filter.AdministrativeArea,
                filter.Country,
                filter.InsurancePlanCode),
            candidateIds,
            cancellationToken);
        var candidates = calculation.Candidates;

        if (cursor is not null)
        {
            var boundary = candidates.SingleOrDefault(candidate =>
                candidate.DoctorId == cursor.DoctorId);
            if (boundary is null ||
                boundary.TotalDemoMatchScorePoints != cursor.MatchScore)
            {
                throw DirectoryCursorCodec.CreateInvalidDoctorCursorException();
            }

            candidates = candidates.Where(candidate =>
                    candidate.TotalDemoMatchScorePoints < cursor.MatchScore ||
                    (candidate.TotalDemoMatchScorePoints == cursor.MatchScore &&
                        string.CompareOrdinal(
                            candidate.DoctorId.Value.ToString("D"),
                            cursor.DoctorId.Value.ToString("D")) > 0))
                .ToArray();
        }

        var hasMore = candidates.Count > pageSize;
        var rankedPage = candidates.Take(pageSize).ToArray();
        var profiles = await repository.GetManyAsync(
            rankedPage.Select(candidate => candidate.DoctorId).ToArray(),
            cancellationToken);
        var items = profiles.Zip(rankedPage, (profile, candidate) =>
            new DoctorDirectorySearchItem(
                profile,
                new DoctorDirectoryMatch(
                    calculation.Rule.Version,
                    candidate.TotalDemoMatchScorePoints,
                    candidate.Factors))).ToArray();
        var nextCursor = hasMore
            ? DirectoryCursorCodec.EncodeRankedDoctor(new RankedDoctorDirectoryPageCursor(
                filter,
                calculation.Rule.Version,
                rankedPage[^1].TotalDemoMatchScorePoints,
                rankedPage[^1].DoctorId))
            : null;

        return new SearchDoctorsResult(items, nextCursor);
    }

    private static bool HasMatchingCriteria(DoctorDirectoryFilter filter) =>
        filter.SpecialtyCode is not null ||
        filter.LanguageCode is not null ||
        filter.Locality is not null ||
        filter.AdministrativeArea is not null ||
        filter.Country is not null ||
        filter.InsurancePlanCode is not null;
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
    IReadOnlyList<DoctorDirectorySearchItem> Items,
    string? NextCursor);

public sealed record DoctorDirectorySearchItem(
    DoctorDirectoryProfile Profile,
    DoctorDirectoryMatch? Match);

public sealed record DoctorDirectoryMatch(
    string RuleVersion,
    int MatchScore,
    IReadOnlyList<DoctorMatchFactorResult> Factors);
