using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed record PreTriageEducationalVideoConfiguration(
    string? Id,
    string? Title,
    string? Url);

public sealed class PreTriageEducationalVideoOptions
{
    private static readonly IReadOnlySet<string> RequiredPathways =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "HEADACHE",
            "ABDOMINAL_PAIN",
            "CHEST_PAIN",
            "FEVER"
        };

    private PreTriageEducationalVideoOptions(
        IReadOnlyDictionary<string, PreTriageEducationalVideo> videos)
    {
        Videos = videos;
    }

    public IReadOnlyDictionary<string, PreTriageEducationalVideo> Videos { get; }

    public static PreTriageEducationalVideoOptions Create(
        IReadOnlyDictionary<string, PreTriageEducationalVideoConfiguration> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        if (configured.Count != RequiredPathways.Count ||
            configured.Keys.Any(key => !RequiredPathways.Contains(key)) ||
            RequiredPathways.Any(pathway => !configured.ContainsKey(pathway)))
        {
            throw new InvalidOperationException(
                "Pre-triage educational videos must configure exactly HEADACHE, " +
                "ABDOMINAL_PAIN, CHEST_PAIN, and FEVER.");
        }

        var videos = new Dictionary<string, PreTriageEducationalVideo>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pathway, value) in configured)
        {
            var id = value.Id?.Trim();
            var title = value.Title?.Trim();
            var url = value.Url?.Trim();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100 ||
                string.IsNullOrWhiteSpace(title) || title.Length > 200 ||
                !ids.Add(id) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException(
                    $"Educational video configuration for '{pathway}' is invalid.");
            }

            videos.Add(pathway, new PreTriageEducationalVideo(id, title, uri.AbsoluteUri));
        }

        return new PreTriageEducationalVideoOptions(videos);
    }
}

public sealed class PreTriageEducationalVideoCatalog(
    PreTriageEducationalVideoOptions options) : IPreTriageEducationalVideoCatalog
{
    public PreTriageEducationalVideo? Find(ClinicalPathwayCode pathway)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        return options.Videos.GetValueOrDefault(pathway.Value);
    }
}
