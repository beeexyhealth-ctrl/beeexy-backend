using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Application.Care;
using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Sharing;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Sharing;

public sealed class CanonicalSharedHealthSnapshotBuilder(
    IPatientProfileReadRepository patientRepository,
    IClinicalHistoryReadRepository clinicalHistoryRepository,
    IClinicalHistoryEventReadRepository clinicalHistoryEventRepository,
    ISymptomDiaryEpisodeReadRepository symptomEpisodeRepository,
    ISymptomCheckInReadRepository symptomCheckInRepository,
    ISymptomDiaryExactContentBatchProvider symptomContentProvider,
    SymptomDiaryPackageValidator symptomPackageValidator,
    ISharedSecondOpinionReadRepository secondOpinionRepository)
    : ICanonicalSharedHealthSnapshotBuilder
{
    private const int PageSize = 100;

    public async Task<CanonicalSharedHealthSnapshot> BuildAsync(
        EntityId patientProfileId,
        SharedProfileSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.IncludeFullProfile)
        {
            return await BuildFullProfileAsync(patientProfileId, cancellationToken);
        }

        return await BuildSelectedAsync(patientProfileId, selection, cancellationToken);
    }

    private async Task<CanonicalSharedHealthSnapshot> BuildFullProfileAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken)
    {
        var patient = await patientRepository.FindAsync(patientProfileId, cancellationToken) ??
            throw new SharedProfileSourceUnavailableException();
        var clinicalDetails = await ListClinicalHistoryDetailsAsync(
            patientProfileId,
            cancellationToken);
        var symptomRecords = await ListSymptomRecordsAsync(
            patientProfileId,
            cancellationToken);
        var symptomProjection = await ProjectSymptomRecordsAsync(
            symptomRecords,
            cancellationToken);
        var secondOpinions = await secondOpinionRepository.ListDisplayableAsync(
            patientProfileId,
            cancellationToken);

        return new CanonicalSharedHealthSnapshot(
            new SharedPatientDemographics(
                patient.BeeexyId,
                patient.FirstName,
                patient.LastName,
                patient.DateOfBirth,
                patient.SexAssignedAtBirth?.ToString(),
                patient.State,
                patient.Version),
            clinicalDetails.Select(detail => MapClinicalHistory(detail.Event)).ToArray(),
            clinicalDetails.Select(MapPreTriage).ToArray(),
            symptomProjection.Entries,
            symptomProjection.Content,
            secondOpinions.Select(MapSecondOpinion).ToArray());
    }

    private async Task<CanonicalSharedHealthSnapshot> BuildSelectedAsync(
        EntityId patientProfileId,
        SharedProfileSelection selection,
        CancellationToken cancellationToken)
    {
        var clinicalHistory = new List<SharedClinicalHistoryEvent>();
        var preTriage = new List<SharedPreTriageRecord>();
        var symptomRecords = new List<SymptomCheckInHistoryRecord>();
        var secondOpinions = new List<SharedSecondOpinionResult>();

        foreach (var item in selection.Items)
        {
            switch (item.ResourceType.Value)
            {
                case SupportedShareResourceTypes.ClinicalHistoryEvent:
                    {
                        var detail = await clinicalHistoryEventRepository.GetAsync(
                            patientProfileId,
                            item.ResourceId,
                            cancellationToken) ?? throw new SharedProfileSourceUnavailableException();
                        clinicalHistory.Add(MapClinicalHistory(detail.Event));
                        break;
                    }
                case SupportedShareResourceTypes.PreTriageEpisode:
                    {
                        var detail = await clinicalHistoryEventRepository
                            .GetByPreTriageEpisodeAsync(
                                patientProfileId,
                                item.ResourceId,
                                cancellationToken) ??
                            throw new SharedProfileSourceUnavailableException();
                        preTriage.Add(MapPreTriage(detail));
                        break;
                    }
                case SupportedShareResourceTypes.SymptomCheckIn:
                    {
                        var record = await symptomCheckInRepository.GetAsync(
                            item.ResourceId,
                            cancellationToken) ?? throw new SharedProfileSourceUnavailableException();
                        var episode = await symptomEpisodeRepository.GetEligibleAsync(
                            record.EpisodeId,
                            cancellationToken);
                        if (episode is null || episode.PatientProfileId != patientProfileId)
                        {
                            throw new SharedProfileSourceUnavailableException();
                        }

                        symptomRecords.Add(record);
                        break;
                    }
                case SupportedShareResourceTypes.SecondOpinionResult:
                    {
                        var result = await secondOpinionRepository.GetDisplayableAsync(
                            patientProfileId,
                            item.ResourceId,
                            cancellationToken) ?? throw new SharedProfileSourceUnavailableException();
                        secondOpinions.Add(MapSecondOpinion(result));
                        break;
                    }
                default:
                    throw new SharedProfileSourceUnavailableException();
            }
        }

        var symptomProjection = await ProjectSymptomRecordsAsync(
            symptomRecords,
            cancellationToken);
        return new CanonicalSharedHealthSnapshot(
            null,
            clinicalHistory.OrderBy(value => value.OccurredAt)
                .ThenBy(value => value.EventId.Value).ToArray(),
            preTriage.OrderBy(value => value.CompletedAt)
                .ThenBy(value => value.EpisodeId.Value).ToArray(),
            symptomProjection.Entries,
            symptomProjection.Content,
            secondOpinions.OrderBy(value => value.GeneratedAt)
                .ThenBy(value => value.ResultId.Value).ToArray());
    }

    private async Task<IReadOnlyList<ClinicalHistoryEventDetail>>
        ListClinicalHistoryDetailsAsync(
            EntityId patientProfileId,
            CancellationToken cancellationToken)
    {
        var details = new List<ClinicalHistoryEventDetail>();
        ClinicalHistoryPageCursor? cursor = null;
        while (true)
        {
            var page = await clinicalHistoryRepository.ListAsync(
                patientProfileId,
                null,
                cursor,
                PageSize,
                cancellationToken);
            foreach (var item in page)
            {
                var detail = await clinicalHistoryEventRepository.GetAsync(
                    patientProfileId,
                    item.EventId,
                    cancellationToken) ?? throw new SharedProfileSourceUnavailableException();
                details.Add(detail);
            }

            if (page.Count < PageSize)
            {
                break;
            }

            var last = page[^1];
            cursor = new ClinicalHistoryPageCursor(
                patientProfileId,
                null,
                last.OccurredAt,
                last.EventId);
        }

        return details
            .OrderBy(value => value.Event.OccurredAt)
            .ThenBy(value => value.Event.EventId.Value)
            .ToArray();
    }

    private async Task<IReadOnlyList<SymptomCheckInHistoryRecord>> ListSymptomRecordsAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken)
    {
        var episodes = await symptomEpisodeRepository.ListEligibleAsync(
            patientProfileId,
            cancellationToken);
        var records = new List<SymptomCheckInHistoryRecord>();
        foreach (var episode in episodes)
        {
            SymptomCheckInPageCursor? cursor = null;
            while (true)
            {
                var page = await symptomCheckInRepository.ListAsync(
                    episode.EpisodeId,
                    cursor,
                    PageSize,
                    cancellationToken);
                records.AddRange(page);
                if (page.Count < PageSize)
                {
                    break;
                }

                var last = page[^1];
                cursor = new SymptomCheckInPageCursor(
                    episode.EpisodeId,
                    PageSize,
                    last.CreatedAt,
                    last.CheckInId);
            }
        }

        return records;
    }

    private async Task<SymptomProjection> ProjectSymptomRecordsAsync(
        IReadOnlyList<SymptomCheckInHistoryRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return new SymptomProjection([], []);
        }

        var episodeIds = records.Select(value => value.EpisodeId).Distinct().ToArray();
        var episodes = new Dictionary<EntityId, EligibleSymptomDiaryEpisode>();
        foreach (var episodeId in episodeIds)
        {
            var episode = await symptomEpisodeRepository.GetEligibleAsync(
                episodeId,
                cancellationToken) ?? throw new SharedProfileSourceUnavailableException();
            episodes.Add(episodeId, episode);
        }

        var packages = new Dictionary<EntityId, SymptomDiaryPackageContent>();
        foreach (var batch in records.Select(value => value.PackageVersionId)
                     .Distinct()
                     .Chunk(PageSize))
        {
            IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent> loaded;
            try
            {
                loaded = await symptomContentProvider.GetExactPackagesAsync(
                    batch,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is
                SymptomDiaryPackageIntegrityException or
                SymptomDiaryPackageValidationException)
            {
                throw new SharedProfileSourceUnavailableException();
            }

            foreach (var package in loaded)
            {
                packages.Add(package.Key, package.Value);
            }
        }

        SharedSymptomDiaryEntry[] entries;
        try
        {
            entries = records.Select(record =>
            {
                if (!episodes.TryGetValue(record.EpisodeId, out var episode) ||
                    !packages.TryGetValue(record.PackageVersionId, out var package) ||
                    package.Definition.Pathway != episode.Pathway ||
                    !symptomPackageValidator.IsDisplayEligible(package.Definition))
                {
                    throw new SharedProfileSourceUnavailableException();
                }

                var questions = package.Definition.Questions.ToDictionary(
                    value => value.Code,
                    value => value);
                var seen = new HashSet<SymptomDiaryCode>();
                var answers = new List<SharedSymptomDiaryAnswer>();
                foreach (var answer in record.Answers.OrderBy(value => value.SourceOrder))
                {
                    if (!seen.Add(answer.QuestionCode) ||
                        !questions.TryGetValue(answer.QuestionCode, out var question) ||
                        question.SourceOrder != answer.SourceOrder)
                    {
                        throw new SharedProfileSourceUnavailableException();
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(answer.SubmittedValueJson);
                        answers.Add(new SharedSymptomDiaryAnswer(
                            question.Code.Value,
                            question.PromptText,
                            document.RootElement.Clone()));
                    }
                    catch (JsonException)
                    {
                        throw new SharedProfileSourceUnavailableException();
                    }
                }

                return new SharedSymptomDiaryEntry(
                    record.CheckInId,
                    record.EpisodeId,
                    record.CreatedAt,
                    episode.Pathway.Value,
                    MapPackage(package),
                    answers);
            }).OrderBy(value => value.RecordedAt)
                .ThenBy(value => value.CheckInId.Value)
                .ToArray();
        }
        catch (SymptomDiaryPackageValidationException)
        {
            throw new SharedProfileSourceUnavailableException();
        }

        var content = packages.Values
            .OrderBy(value => value.PackageVersionId.Value)
            .Select(package => new SharedSymptomDiaryContent(
                MapPackage(package),
                package.Definition.Pathway.Value,
                package.Definition.InformationalHeading,
                package.Definition.InformationalBody,
                package.Definition.WarningSigns
                    .OrderBy(value => value.SourceOrder)
                    .Select(value => new SharedSymptomWarningSign(
                        value.Code.Value,
                        value.DisplayText,
                        value.SourceOrder))
                    .ToArray()))
            .ToArray();
        return new SymptomProjection(entries, content);
    }

    private static SharedClinicalHistoryEvent MapClinicalHistory(
        ClinicalHistoryListItem item) => new(
        item.EventId,
        ClinicalHistoryEventTypes.ToApiValue(item.EventType),
        item.OccurredAt,
        item.RecordedAt,
        new SharedClinicalProvenance(
            item.SourceType == AuthoritativeClinicalSourceType.PreTriageEpisode
                ? "PRE_TRIAGE_EPISODE"
                : throw new SharedProfileSourceUnavailableException(),
            item.SourceId,
            item.QuestionnaireVersionId,
            item.ClinicalRuleSetVersionId));

    private static SharedPreTriageRecord MapPreTriage(ClinicalHistoryEventDetail detail)
    {
        var summary = detail.PreTriageSummary ??
            throw new SharedProfileSourceUnavailableException();
        var source = detail.AuthoritativeSource;
        if (source.SourceType != AuthoritativeClinicalSourceType.PreTriageEpisode)
        {
            throw new SharedProfileSourceUnavailableException();
        }

        return new SharedPreTriageRecord(
            source.Id,
            source.CompletedAt,
            new SharedPreTriagePrimarySymptom(
                summary.PrimarySymptom.Code,
                summary.PrimarySymptom.Display),
            new SharedPreTriageDuration(summary.Duration.Value, summary.Duration.Unit),
            summary.Intensity,
            summary.AdditionalSymptoms,
            source.QuestionnaireVersionId,
            source.ClinicalRuleSetVersionId);
    }

    private static SharedSymptomDiaryPackageVersion MapPackage(
        SymptomDiaryPackageContent package)
    {
        var definition = package.Definition;
        if (definition.ApprovedAt is not { } approvedAt)
        {
            throw new SharedProfileSourceUnavailableException();
        }

        return new SharedSymptomDiaryPackageVersion(
            package.PackageVersionId,
            definition.PackageCode.Value,
            definition.PackageVersion.Value,
            package.CanonicalContentHash.Value,
            definition.QuestionSetCode.Value,
            definition.QuestionSetVersion.Value,
            definition.SymptomInformationCode.Value,
            definition.SymptomInformationVersion.Value,
            definition.ContentStatus.Source == ClinicalContentSource.MedicalTeamProvided
                ? "MEDICAL_TEAM_PROVIDED"
                : throw new SharedProfileSourceUnavailableException(),
            definition.ContentStatus.ReviewStatus == ClinicalReviewStatus.Reviewed
                ? "REVIEWED"
                : throw new SharedProfileSourceUnavailableException(),
            definition.ContentStatus.ApprovalStatus == ClinicalApprovalStatus.Approved
                ? "APPROVED"
                : throw new SharedProfileSourceUnavailableException(),
            approvedAt);
    }

    private static SharedSecondOpinionResult MapSecondOpinion(
        SharedSecondOpinionStoredResult stored)
    {
        try
        {
            using var document = JsonDocument.Parse(stored.ContentJson);
            var root = document.RootElement;
            return new SharedSecondOpinionResult(
                stored.ResultId,
                stored.GeneratedAt,
                stored.ResultSchemaVersion,
                ReadRequiredString(root, "summary"),
                ReadStringArray(root, "importantPoints"),
                ReadStringArray(root, "possibleQuestionsForDoctor"),
                ReadStringArray(root, "missingInformation"),
                SecondOpinionProductContent.Disclaimer);
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new SharedProfileSourceUnavailableException();
        }
    }

    private static string ReadRequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new SharedProfileSourceUnavailableException();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new SharedProfileSourceUnavailableException();
        }

        var items = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null)
            .ToArray();
        return items.Any(string.IsNullOrWhiteSpace)
            ? throw new SharedProfileSourceUnavailableException()
            : items.Select(item => item!).ToArray();
    }

    private sealed record SymptomProjection(
        IReadOnlyList<SharedSymptomDiaryEntry> Entries,
        IReadOnlyList<SharedSymptomDiaryContent> Content);
}
