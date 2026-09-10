using System.Globalization;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class PdfPigPdfExportRenderer : IPdfExportRenderer
{
    private const int LinesPerPage = 45;
    private const int WrapWidth = 88;

    public byte[] Render(
        CanonicalSharedHealthSnapshot snapshot,
        EntityId snapshotId,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            var lines = BuildLines(snapshot, snapshotId, generatedAt);
            var pages = lines.Chunk(LinesPerPage).ToArray();
            var builder = new PdfDocumentBuilder();
            var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
            var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

            for (var index = 0; index < pages.Length; index++)
            {
                var page = builder.AddPage(PageSize.A4);
                page.SetTextAndFillColor(20, 54, 78);
                page.AddText(
                    "Beeexy Health Snapshot",
                    16,
                    new PdfPoint(48, 806),
                    bold);
                page.SetTextAndFillColor(30, 30, 30);

                double y = 780;
                foreach (var line in pages[index])
                {
                    page.AddText(
                        line.Text,
                        line.IsHeading ? 12 : 9,
                        new PdfPoint(line.IsHeading ? 48 : 58, y),
                        line.IsHeading ? bold : regular);
                    y -= line.IsHeading ? 18 : 14;
                }

                page.SetTextAndFillColor(80, 80, 80);
                page.DrawLine(new PdfPoint(48, 46), new PdfPoint(547, 46), 0.5);
                page.AddText(
                    $"Page {index + 1} of {pages.Length} | " +
                    $"Snapshot {snapshotId.Value:D} | " +
                    $"Generated {Utc(generatedAt)}",
                    7,
                    new PdfPoint(48, 30),
                    regular);
            }

            return builder.Build();
        }
        catch (Exception exception) when (exception is not PdfExportRenderException)
        {
            throw new PdfExportRenderException(exception);
        }
    }

    private static IReadOnlyList<PdfLine> BuildLines(
        CanonicalSharedHealthSnapshot snapshot,
        EntityId snapshotId,
        DateTimeOffset generatedAt)
    {
        var lines = new List<PdfLine>();
        AddHeading(lines, "Export details");
        AddText(lines, $"Generated: {Utc(generatedAt)}");
        AddText(lines, $"Canonical snapshot version: {BeeexyJsonExportRenderer.SnapshotVersion}");
        AddText(lines, $"PDF format version: {PdfExportContract.FormatVersion}");
        AddText(lines, $"Snapshot ID: {snapshotId.Value:D}");

        if (snapshot.Demographics is { } demographics)
        {
            AddHeading(lines, "Patient");
            var name = string.Join(
                ' ',
                new[] { demographics.FirstName, demographics.LastName }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            AddText(lines, $"Name: {(name.Length == 0 ? "Not provided" : name)}");
            AddText(lines, $"Beeexy ID: {demographics.BeeexyId}");
            AddText(lines,
                $"Date of birth: {demographics.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not provided"}");
            AddText(lines,
                $"Sex assigned at birth: {demographics.SexAssignedAtBirth ?? "Not provided"}");
            AddText(lines, $"State: {demographics.State ?? "Not provided"}");
            AddText(lines, $"Patient profile version: {demographics.Version}");
        }

        AddHeading(lines, "Clinical History");
        if (snapshot.ClinicalHistory.Count == 0)
        {
            AddText(lines, "No approved Clinical History records were present.");
        }

        foreach (var item in snapshot.ClinicalHistory)
        {
            AddText(lines, $"- {item.EventType} on {Utc(item.OccurredAt)}");
            AddText(lines,
                $"  Recorded {Utc(item.RecordedAt)}; source {item.Provenance.SourceType} " +
                $"({item.Provenance.SourceId.Value:D}); questionnaire " +
                $"{item.Provenance.QuestionnaireVersionId.Value:D}; clinical rule set " +
                $"{item.Provenance.ClinicalRuleSetVersionId.Value:D}.");
        }

        AddHeading(lines, "Completed Pre-Triage");
        if (snapshot.PreTriage.Count == 0)
        {
            AddText(lines, "No completed Pre-Triage records were present.");
        }

        foreach (var item in snapshot.PreTriage)
        {
            AddText(lines,
                $"- {item.PrimarySymptom.Display} ({item.PrimarySymptom.Code}), completed " +
                $"{Utc(item.CompletedAt)}");
            AddText(lines,
                $"  Duration: {item.Duration.Value.ToString(CultureInfo.InvariantCulture)} " +
                $"{item.Duration.Unit}; intensity: {item.Intensity}.");
            AddText(lines,
                $"  Additional symptoms: " +
                (item.AdditionalSymptoms.Count == 0
                    ? "None reported."
                    : string.Join(", ", item.AdditionalSymptoms)));
        }

        AddHeading(lines, "Symptom Diary");
        if (snapshot.SymptomDiaryEntries.Count == 0)
        {
            AddText(lines, "No approved Symptom Diary entries were present.");
        }

        foreach (var entry in snapshot.SymptomDiaryEntries)
        {
            AddText(lines, $"- {entry.Pathway}, recorded {Utc(entry.RecordedAt)}");
            foreach (var answer in entry.Answers)
            {
                AddText(lines,
                    $"  {answer.PromptText} ({answer.QuestionCode}): " +
                    Describe(answer.Value));
            }
        }

        AddHeading(lines, "Symptom Diary Information");
        if (snapshot.SymptomDiaryContent.Count == 0)
        {
            AddText(lines, "No separately approved informational content was present.");
        }

        foreach (var content in snapshot.SymptomDiaryContent)
        {
            AddText(lines,
                $"- {content.Pathway}: {content.InformationalHeading ?? "Approved information"}");
            if (!string.IsNullOrWhiteSpace(content.InformationalBody))
            {
                AddText(lines, $"  {content.InformationalBody}");
            }

            foreach (var warning in content.WarningSigns.OrderBy(value => value.SourceOrder))
            {
                AddText(lines, $"  Warning sign: {warning.DisplayText} ({warning.Code})");
            }
        }

        AddHeading(lines, "Patient-Visible Second Opinion Results");
        if (snapshot.SecondOpinions.Count == 0)
        {
            AddText(lines, "No patient-visible Second Opinion results were present.");
        }

        foreach (var opinion in snapshot.SecondOpinions)
        {
            AddText(lines,
                $"- Result {opinion.ResultId.Value:D}, generated {Utc(opinion.GeneratedAt)}, " +
                $"version {opinion.ResultVersion}");
            AddText(lines, $"  Summary: {opinion.Summary}");
            AddList(lines, "Important point", opinion.ImportantPoints);
            AddList(lines, "Question for doctor", opinion.PossibleQuestionsForDoctor);
            AddList(lines, "Missing information", opinion.MissingInformation);
            AddText(lines, $"  Result disclaimer: {opinion.Disclaimer}");
        }

        AddHeading(lines, "About this export");
        AddText(lines,
            "This document is an immutable snapshot of approved Beeexy information at " +
            "the generation time. Later changes to the patient record do not change it.");
        AddText(lines,
            "This export is for informational sharing and does not replace professional " +
            "medical advice, diagnosis, or treatment. Access to copies downloaded outside " +
            "Beeexy cannot be revoked by Beeexy.");
        return lines;
    }

    private static void AddList(
        ICollection<PdfLine> lines,
        string label,
        IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            AddText(lines, $"  {label}: {value}");
        }
    }

    private static void AddHeading(ICollection<PdfLine> lines, string text)
    {
        lines.Add(new PdfLine(ToPdfText(text), IsHeading: true));
    }

    private static void AddText(ICollection<PdfLine> lines, string text)
    {
        var normalized = ToPdfText(text);
        while (normalized.Length > WrapWidth)
        {
            var split = normalized.LastIndexOf(' ', WrapWidth);
            if (split < 1)
            {
                split = WrapWidth;
            }

            lines.Add(new PdfLine(normalized[..split].TrimEnd(), IsHeading: false));
            normalized = "  " + normalized[split..].TrimStart();
        }

        lines.Add(new PdfLine(normalized, IsHeading: false));
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Null => "Not provided",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(Describe)),
        JsonValueKind.Object => string.Join(
            "; ",
            value.EnumerateObject().Select(property => $"{property.Name}: {Describe(property.Value)}")),
        _ => "Structured response"
    };

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string ToPdfText(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            result.Append(character switch
            {
                >= ' ' and <= '~' => character,
                '\r' or '\n' or '\t' => ' ',
                '\u2013' or '\u2014' => '-',
                '\u2018' or '\u2019' => '\'',
                '\u201c' or '\u201d' => '"',
                _ => '?'
            });
        }

        return result.ToString();
    }

    private sealed record PdfLine(string Text, bool IsHeading);
}
