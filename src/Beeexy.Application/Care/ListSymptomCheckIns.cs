using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Care;

public sealed class ListSymptomCheckIns(
    AuthorizePatientAccess authorizePatientAccess,
    ISymptomDiaryEpisodeReadRepository episodeRepository,
    ISymptomCheckInReadRepository repository,
    ISymptomDiaryExactContentBatchProvider contentProvider,
    ISymptomDiaryHistoryCursorCodec cursorCodec,
    SymptomDiaryPackageValidator packageValidator,
    ISymptomDiaryHistoryAuditLogger auditLogger)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public async Task<ListSymptomCheckInsResult> ExecuteAsync(
        ListSymptomCheckInsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.EpisodeId.Value == Guid.Empty)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var episode = await episodeRepository.GetEligibleAsync(
            query.EpisodeId,
            cancellationToken);
        if (episode is null)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            episode.PatientProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        ValidateRequest(query);

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "symptom_diary.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var cursor = query.Cursor is null
            ? null
            : cursorCodec.Decode(query.Cursor, query.EpisodeId, pageSize);
        if (cursor is not null &&
            !await repository.CursorExistsAsync(cursor, cancellationToken))
        {
            throw SymptomDiaryHistoryCursorErrors.Invalid();
        }

        var page = await repository.ListAsync(
            query.EpisodeId,
            cursor,
            pageSize + 1,
            cancellationToken);
        if (page.Any(item => item.EpisodeId != query.EpisodeId))
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        var hasMore = page.Count > pageSize;
        var records = page.Take(pageSize).ToArray();
        var packageIds = records
            .Select(record => record.PackageVersionId)
            .Distinct()
            .ToArray();

        IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent> packages;
        try
        {
            packages = await contentProvider.GetExactPackagesAsync(
                packageIds,
                cancellationToken);
        }
        catch (SymptomDiaryPackageIntegrityException)
        {
            auditLogger.HistoricalContentUnavailable(query.EpisodeId, packageIds.Length);
            throw new SymptomDiaryContentUnavailableException();
        }
        catch (SymptomDiaryPackageValidationException)
        {
            auditLogger.HistoricalContentUnavailable(query.EpisodeId, packageIds.Length);
            throw new SymptomDiaryContentUnavailableException();
        }

        SymptomCheckInHistoryItem[] items;
        try
        {
            items = records.Select(record => Reconstruct(record, episode, packages)).ToArray();
        }
        catch (SymptomDiaryPackageIntegrityException)
        {
            auditLogger.HistoricalContentUnavailable(query.EpisodeId, packageIds.Length);
            throw new SymptomDiaryContentUnavailableException();
        }
        catch (SymptomDiaryPackageValidationException)
        {
            auditLogger.HistoricalContentUnavailable(query.EpisodeId, packageIds.Length);
            throw new SymptomDiaryContentUnavailableException();
        }
        catch (SymptomDiaryContentUnavailableException)
        {
            auditLogger.HistoricalContentUnavailable(query.EpisodeId, packageIds.Length);
            throw;
        }
        var nextCursor = hasMore
            ? cursorCodec.Encode(new SymptomCheckInPageCursor(
                query.EpisodeId,
                pageSize,
                records[^1].CreatedAt,
                records[^1].CheckInId))
            : null;

        return new ListSymptomCheckInsResult(items, nextCursor);
    }

    private static void ValidateRequest(ListSymptomCheckInsQuery query)
    {
        if (query.HasUnsupportedQuery)
        {
            throw new RequestValidationException(
                "symptom_diary.unsupported_query",
                "Symptom-diary history accepts one optional cursor and pageSize value only.");
        }

        if (query.HasUnsupportedBody)
        {
            throw new RequestValidationException(
                "symptom_diary.unsupported_body",
                "Symptom-diary history retrieval does not accept a request body.");
        }

        if (query.HasInvalidPageSize)
        {
            throw new RequestValidationException(
                "symptom_diary.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }
    }

    private SymptomCheckInHistoryItem Reconstruct(
        SymptomCheckInHistoryRecord record,
        EligibleSymptomDiaryEpisode episode,
        IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent> packages)
    {
        if (!packages.TryGetValue(record.PackageVersionId, out var package) ||
            package.PackageVersionId != record.PackageVersionId ||
            package.Definition.Pathway != episode.Pathway ||
            !packageValidator.IsDisplayEligible(package.Definition))
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        var questions = package.Definition.Questions.ToDictionary(
            question => question.Code,
            question => question);
        var seen = new HashSet<SymptomDiaryCode>();
        var answers = new List<SymptomCheckInHistoryAnswer>(record.Answers.Count);
        foreach (var stored in record.Answers.OrderBy(answer => answer.SourceOrder))
        {
            if (!seen.Add(stored.QuestionCode) ||
                !questions.TryGetValue(stored.QuestionCode, out var question) ||
                question.SourceOrder != stored.SourceOrder)
            {
                throw new SymptomDiaryContentUnavailableException();
            }

            try
            {
                using var document = JsonDocument.Parse(stored.SubmittedValueJson);
                answers.Add(new SymptomCheckInHistoryAnswer(
                    question,
                    document.RootElement.Clone()));
            }
            catch (JsonException)
            {
                throw new SymptomDiaryContentUnavailableException();
            }
        }

        return new SymptomCheckInHistoryItem(
            record.CheckInId,
            record.EpisodeId,
            record.CreatedAt,
            episode.Pathway,
            package,
            answers);
    }
}

public sealed record ListSymptomCheckInsQuery(
    EntityId EpisodeId,
    string? Cursor = null,
    int? PageSize = null,
    bool HasUnsupportedQuery = false,
    bool HasUnsupportedBody = false,
    bool HasInvalidPageSize = false);

public sealed record ListSymptomCheckInsResult(
    IReadOnlyList<SymptomCheckInHistoryItem> Items,
    string? NextCursor);

public sealed record SymptomCheckInHistoryItem(
    EntityId CheckInId,
    EntityId EpisodeId,
    DateTimeOffset CreatedAt,
    ClinicalPathwayCode Pathway,
    SymptomDiaryPackageContent Package,
    IReadOnlyList<SymptomCheckInHistoryAnswer> Answers);

public sealed record SymptomCheckInHistoryAnswer(
    SymptomDiaryQuestionDefinition Question,
    JsonElement Value);

public interface ISymptomCheckInReadRepository
{
    Task<bool> CursorExistsAsync(
        SymptomCheckInPageCursor cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SymptomCheckInHistoryRecord>> ListAsync(
        EntityId episodeId,
        SymptomCheckInPageCursor? after,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record SymptomCheckInHistoryRecord(
    EntityId CheckInId,
    EntityId EpisodeId,
    EntityId PackageVersionId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymptomCheckInHistoryAnswerRecord> Answers);

public sealed record SymptomCheckInHistoryAnswerRecord(
    SymptomDiaryCode QuestionCode,
    int SourceOrder,
    string SubmittedValueJson);

public sealed record SymptomCheckInPageCursor(
    EntityId EpisodeId,
    int PageSize,
    DateTimeOffset CreatedAt,
    EntityId CheckInId);

public interface ISymptomDiaryHistoryCursorCodec
{
    string Encode(SymptomCheckInPageCursor cursor);

    SymptomCheckInPageCursor Decode(
        string encoded,
        EntityId expectedEpisodeId,
        int expectedPageSize);
}

public interface ISymptomDiaryExactContentBatchProvider
{
    Task<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>> GetExactPackagesAsync(
        IReadOnlyCollection<EntityId> packageVersionIds,
        CancellationToken cancellationToken = default);
}

public interface ISymptomDiaryHistoryAuditLogger
{
    void HistoricalContentUnavailable(EntityId episodeId, int packageVersionCount);
}

public static class SymptomDiaryHistoryCursorErrors
{
    public static RequestValidationException Invalid() => new(
        "symptom_diary.cursor_invalid",
        "The symptom-diary history cursor is invalid for this request.");
}
