using System.Text.Json;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Sharing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class PdfExportRendererTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 9, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase117")]
    public void Render_ProducesReadablePaginatedCanonicalSnapshotWithoutInternalData()
    {
        using var answer = JsonDocument.Parse("{\"severity\":7,\"improving\":false}");
        var package = Package();
        var snapshot = new CanonicalSharedHealthSnapshot(
            new SharedPatientDemographics(
                "BXY-PDF-001",
                "José",
                "Patient",
                new DateOnly(1991, 3, 4),
                "Male",
                "Lima",
                5),
            Enumerable.Range(0, 24).Select(index => new SharedClinicalHistoryEvent(
                EntityId.New(),
                "COMPLETED_PRE_TRIAGE",
                GeneratedAt.AddDays(-index - 1),
                GeneratedAt.AddDays(-index - 1),
                new SharedClinicalProvenance(
                    "PRE_TRIAGE_EPISODE",
                    EntityId.New(),
                    EntityId.New(),
                    EntityId.New()))).ToArray(),
            [
                new SharedPreTriageRecord(
                    EntityId.New(),
                    GeneratedAt.AddHours(-4),
                    new SharedPreTriagePrimarySymptom("HEADACHE", "Headache"),
                    new SharedPreTriageDuration(2, "DAYS"),
                    7,
                    ["FEVER"],
                    EntityId.New(),
                    EntityId.New())
            ],
            [
                new SharedSymptomDiaryEntry(
                    EntityId.New(),
                    EntityId.New(),
                    GeneratedAt.AddHours(-2),
                    "HEADACHE",
                    package,
                    [new SharedSymptomDiaryAnswer(
                        "status",
                        "How are the symptoms?",
                        answer.RootElement.Clone())])
            ],
            [
                new SharedSymptomDiaryContent(
                    package,
                    "HEADACHE",
                    "Approved headache information",
                    "Use the approved care information provided by the medical team.",
                    [new SharedSymptomWarningSign("URGENT", "Seek urgent care", 1)])
            ],
            [
                new SharedSecondOpinionResult(
                    EntityId.New(),
                    GeneratedAt.AddHours(-1),
                    "1.0",
                    "Patient-visible summary",
                    ["Important point"],
                    ["Question for doctor"],
                    ["Missing information"],
                    "Informational only")
            ]);

        var bytes = new PdfPigPdfExportRenderer().Render(
            snapshot,
            EntityId.New(),
            GeneratedAt);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
        using var document = PdfDocument.Open(bytes);
        Assert.True(document.NumberOfPages > 1);
        var text = string.Join(
            '\n',
            document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        Assert.Contains("Beeexy Health Snapshot", text, StringComparison.Ordinal);
        Assert.Contains("Jose Patient", text, StringComparison.Ordinal);
        Assert.Contains("Clinical History", text, StringComparison.Ordinal);
        Assert.Contains("Completed Pre-Triage", text, StringComparison.Ordinal);
        Assert.Contains("Symptom Diary Information", text, StringComparison.Ordinal);
        Assert.Contains("Patient-Visible Second Opinion Results", text, StringComparison.Ordinal);
        Assert.Contains("immutable snapshot", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Page 1 of {document.NumberOfPages}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("QR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("provider", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conversation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    private static SharedSymptomDiaryPackageVersion Package() => new(
        EntityId.New(),
        "HEADACHE",
        "1.0",
        new string('a', 64),
        "questions",
        "1.0",
        "information",
        "1.0",
        "MEDICAL_TEAM_PROVIDED",
        "REVIEWED",
        "APPROVED",
        GeneratedAt.AddDays(-30));
}
