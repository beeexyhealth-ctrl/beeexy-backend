using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class PreTriageEducationalVideoCatalogTests
{
    [Fact]
    public void ValidConfiguration_MapsOnlyTheFourEducationalPathways()
    {
        var options = PreTriageEducationalVideoOptions.Create(ValidConfiguration());
        var catalog = new PreTriageEducationalVideoCatalog(options);

        Assert.Equal("headache", catalog.Find(ClinicalPathways.Headache)!.Id);
        Assert.Equal("abdominal-pain", catalog.Find(ClinicalPathways.AbdominalPain)!.Id);
        Assert.Equal("chest-pain", catalog.Find(ClinicalPathways.ChestPain)!.Id);
        Assert.Equal("fever", catalog.Find(ClinicalPathways.Fever)!.Id);
        Assert.Null(catalog.Find(ClinicalPathways.OtherSymptoms));
    }

    [Fact]
    public void MissingUnsupportedOrInsecureConfiguration_IsRejected()
    {
        var missing = ValidConfiguration();
        missing.Remove("FEVER");
        Assert.Throws<InvalidOperationException>(() =>
            PreTriageEducationalVideoOptions.Create(missing));

        var unsupported = ValidConfiguration();
        unsupported.Add("OTHER_SYMPTOMS", Video("other", "Other"));
        Assert.Throws<InvalidOperationException>(() =>
            PreTriageEducationalVideoOptions.Create(unsupported));

        var insecure = ValidConfiguration();
        insecure["HEADACHE"] = new PreTriageEducationalVideoConfiguration(
            "headache", "Understanding Headaches", "http://example.com/headache.mp4");
        Assert.Throws<InvalidOperationException>(() =>
            PreTriageEducationalVideoOptions.Create(insecure));
    }

    private static Dictionary<string, PreTriageEducationalVideoConfiguration>
        ValidConfiguration() => new(StringComparer.Ordinal)
        {
            ["HEADACHE"] = Video("headache", "Understanding Headaches"),
            ["ABDOMINAL_PAIN"] = Video("abdominal-pain", "Understanding Stomach Pain"),
            ["CHEST_PAIN"] = Video("chest-pain", "Understanding Chest Pain"),
            ["FEVER"] = Video("fever", "Understanding Fever")
        };

    private static PreTriageEducationalVideoConfiguration Video(
        string id,
        string title) => new(id, title, $"https://example.com/{id}.mp4");
}
