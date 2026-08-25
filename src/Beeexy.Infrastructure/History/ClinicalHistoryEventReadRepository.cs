using Beeexy.Application.History;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.History;

internal sealed class ClinicalHistoryEventReadRepository(BeeexyDbContext dbContext)
    : IClinicalHistoryEventReadRepository
{
    public async Task<ClinicalHistoryEventDetail?> GetAsync(
        EntityId patientProfileId,
        EntityId eventId,
        CancellationToken cancellationToken = default)
    {
        var historyEvent = await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.PatientProfileId == patientProfileId &&
                    candidate.Id == eventId,
                cancellationToken);
        if (historyEvent is null)
        {
            return null;
        }

        if (historyEvent.EventType != ClinicalHistoryEventType.CompletedPreTriage ||
            historyEvent.SourceType != AuthoritativeClinicalSourceType.PreTriageEpisode)
        {
            return null;
        }

        var source = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                episode =>
                    episode.Id == historyEvent.SourceId &&
                    episode.PatientProfileId == patientProfileId &&
                    episode.QuestionnaireVersionId ==
                        historyEvent.SourceQuestionnaireVersionId &&
                    episode.ClinicalRuleSetVersionId ==
                        historyEvent.SourceClinicalRuleSetVersionId &&
                    episode.CompletedAt == historyEvent.OccurredAt,
                cancellationToken);
        if (source is null)
        {
            return null;
        }

        var storedAnswers = await (
                from answer in dbContext.TriageAnswers.AsNoTracking()
                join question in dbContext.TriageQuestions.AsNoTracking()
                    on answer.QuestionId equals question.Id
                where answer.EpisodeId == source.Id &&
                    answer.QuestionnaireVersionId == source.QuestionnaireVersionId &&
                    question.QuestionnaireVersionId == source.QuestionnaireVersionId
                orderby answer.Sequence
                select new StoredPreTriageAnswer(question.Code, answer.AnswerJson))
            .ToArrayAsync(cancellationToken);
        var storedSymptoms = await dbContext.ReportedSymptoms
            .AsNoTracking()
            .Where(symptom => symptom.EpisodeId == source.Id)
            .OrderBy(symptom => symptom.Sequence)
            .Select(symptom => new StoredPreTriageSymptom(
                symptom.Sequence,
                symptom.TerminologyCode,
                symptom.TerminologyDisplay))
            .ToArrayAsync(cancellationToken);
        var preTriageSummary = CompletedPreTriageSummaryProjection.TryProject(
            storedAnswers,
            storedSymptoms);

        var amendments = await (
                from amendment in dbContext.ClinicalAmendments.AsNoTracking()
                where amendment.ClinicalHistoryEventId == historyEvent.Id &&
                    amendment.SourceType == historyEvent.SourceType &&
                    amendment.SourceId == historyEvent.SourceId &&
                    amendment.SourceQuestionnaireVersionId ==
                        historyEvent.SourceQuestionnaireVersionId &&
                    amendment.SourceClinicalRuleSetVersionId ==
                        historyEvent.SourceClinicalRuleSetVersionId
                join authorProfile in dbContext.PatientProfiles.AsNoTracking()
                    on (EntityId?)amendment.AuthorAccountId equals
                        authorProfile.AccountId into authorProfiles
                from authorProfile in authorProfiles.DefaultIfEmpty()
                orderby amendment.CreatedAt, amendment.Id
                select new
                {
                    Amendment = amendment,
                    AuthorProfile = authorProfile
                })
            .ToArrayAsync(cancellationToken);

        var listItem = new ClinicalHistoryListItem(
            historyEvent.Id,
            historyEvent.EventType,
            historyEvent.OccurredAt,
            historyEvent.RecordedAt,
            historyEvent.SourceType,
            historyEvent.SourceId,
            historyEvent.SourceQuestionnaireVersionId,
            historyEvent.SourceClinicalRuleSetVersionId);
        var sourceDetail = new ClinicalHistorySourceDetail(
            historyEvent.SourceType,
            source.Id,
            source.CompletedAt,
            source.QuestionnaireVersionId,
            source.ClinicalRuleSetVersionId);
        var amendmentDetails = amendments.Select(row =>
            new ClinicalHistoryAmendmentDetail(
                row.Amendment.Id,
                row.Amendment.Reason.Value,
                new ClinicalHistoryAmendmentAuthor(
                    row.AuthorProfile?.BeeexyId.Value),
                row.Amendment.CreatedAt,
                new ClinicalHistoryProvenance(
                    row.Amendment.SourceType,
                    row.Amendment.SourceId,
                    row.Amendment.SourceQuestionnaireVersionId,
                    row.Amendment.SourceClinicalRuleSetVersionId)))
            .ToArray();

        return new ClinicalHistoryEventDetail(
            listItem,
            sourceDetail,
            amendmentDetails,
            preTriageSummary);
    }

    private sealed record StoredPreTriageAnswer(QuestionCode Code, string AnswerJson);

    private sealed record StoredPreTriageSymptom(
        int Sequence,
        string? Code,
        string? Display);

    private static class CompletedPreTriageSummaryProjection
    {
        public static CompletedPreTriageSummary? TryProject(
            IReadOnlyList<StoredPreTriageAnswer> answers,
            IReadOnlyList<StoredPreTriageSymptom> symptoms)
        {
            var primary = symptoms.FirstOrDefault();
            if (primary is null || primary.Sequence != 1 ||
                string.IsNullOrWhiteSpace(primary.Code) ||
                string.IsNullOrWhiteSpace(primary.Display) ||
                !TrySingleAnswer(answers, SimplifiedDemoDefinitionPackages.DurationQuestion,
                    out var durationJson) ||
                !TryReadDuration(durationJson, out var duration) ||
                !TrySingleAnswer(answers, SimplifiedDemoDefinitionPackages.IntensityQuestion,
                    out var intensityJson) ||
                !TryReadIntensity(intensityJson, out var intensity))
            {
                return null;
            }

            var additionalSymptoms = symptoms.Skip(1)
                .Select(symptom => symptom.Code)
                .ToArray();
            if (additionalSymptoms.Any(string.IsNullOrWhiteSpace))
            {
                return null;
            }

            return new CompletedPreTriageSummary(
                new CompletedPreTriagePrimarySymptom(primary.Code, primary.Display),
                duration,
                intensity,
                additionalSymptoms.Select(value => value!).ToArray());
        }

        private static bool TrySingleAnswer(
            IReadOnlyList<StoredPreTriageAnswer> answers,
            string code,
            out string answerJson)
        {
            var matching = answers
                .Where(answer => string.Equals(
                    answer.Code.Value,
                    code,
                    StringComparison.Ordinal))
                .Select(answer => answer.AnswerJson)
                .Take(2)
                .ToArray();
            answerJson = matching.Length == 1 ? matching[0] : string.Empty;
            return matching.Length == 1;
        }

        private static bool TryReadDuration(
            string json,
            out CompletedPreTriageDuration duration)
        {
            duration = null!;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!HasExactProperties(root, "value", "unit") ||
                    !root.GetProperty("value").TryGetDecimal(out var value) ||
                    root.GetProperty("unit").ValueKind !=
                        System.Text.Json.JsonValueKind.String)
                {
                    return false;
                }

                var unit = root.GetProperty("unit").GetString();
                if (string.IsNullOrWhiteSpace(unit))
                {
                    return false;
                }

                duration = new CompletedPreTriageDuration(value, unit);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        private static bool TryReadIntensity(string json, out int intensity)
        {
            intensity = default;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var root = document.RootElement;
                return HasExactProperties(root, "value") &&
                    root.GetProperty("value").TryGetInt32(out intensity);
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        private static bool HasExactProperties(
            System.Text.Json.JsonElement element,
            params string[] expected)
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }

            var actual = element.EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
            return actual.Length == expected.Length &&
                actual.Distinct(StringComparer.Ordinal).Count() == actual.Length &&
                expected.All(property => actual.Contains(property, StringComparer.Ordinal));
        }
    }
}
