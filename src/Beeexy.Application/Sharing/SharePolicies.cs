using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public static class SupportedShareResourceTypes
{
    public const string ClinicalHistoryEvent = "clinical_history_event";
    public const string PreTriageEpisode = "pre_triage_episode";
    public const string SymptomCheckIn = "symptom_check_in";
    public const string SecondOpinionResult = "second_opinion_result";

    private static readonly HashSet<string> Values =
    [
        ClinicalHistoryEvent,
        PreTriageEpisode,
        SymptomCheckIn,
        SecondOpinionResult
    ];

    public static bool Contains(ShareResourceType value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Values.Contains(value.Value);
    }
}

public sealed class ShareLifetimePolicy
{
    public const int DefaultLifetimeMinutes = 24 * 60;
    public const int MaximumLifetimeMinutes = 7 * 24 * 60;

    public TimeSpan Resolve(int? requestedMinutes)
    {
        var minutes = requestedMinutes ?? DefaultLifetimeMinutes;
        if (minutes <= 0 || minutes > MaximumLifetimeMinutes)
        {
            throw new RequestValidationException(
                "sharing.lifetime_invalid",
                $"Share lifetime must be between 1 and {MaximumLifetimeMinutes} minutes.");
        }

        return TimeSpan.FromMinutes(minutes);
    }
}

public sealed record ShareUrlOptions
{
    private ShareUrlOptions(string publicBaseUrl)
    {
        PublicBaseUrl = publicBaseUrl;
    }

    public string PublicBaseUrl { get; }

    public static ShareUrlOptions Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            normalized.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The public share base URL must be an absolute HTTP(S) URL without " +
                "credentials, query, fragment, or trailing slash.",
                nameof(value));
        }

        return new ShareUrlOptions(normalized);
    }

    public string Build(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        return $"{PublicBaseUrl}#{capability}";
    }
}

public static class ShareRequestFingerprintCalculator
{
    public static ShareRequestFingerprint Calculate(
        EntityId patientProfileId,
        ShareScope scope,
        TimeSpan lifetime,
        IReadOnlyList<ShareItemReference> items)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("patientId", patientProfileId.Value.ToString("D"));
            writer.WriteString("scope", ToCanonicalScope(scope));
            writer.WriteNumber("lifetimeMinutes", checked((int)lifetime.TotalMinutes));
            writer.WriteStartArray("items");
            foreach (var item in items
                .OrderBy(value => value.ResourceType.Value, StringComparer.Ordinal)
                .ThenBy(value => value.ResourceId.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("resourceType", item.ResourceType.Value);
                writer.WriteString("resourceId", item.ResourceId.Value.ToString("D"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ShareRequestFingerprint.Create(
            Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant());
    }

    private static string ToCanonicalScope(ShareScope scope) => scope switch
    {
        ShareScope.FullProfile => "full_profile",
        ShareScope.PreTriage => "pre_triage",
        ShareScope.SpecificRecords => "specific_records",
        ShareScope.Case => "case",
        ShareScope.Visit => "visit",
        _ => throw new RequestValidationException(
            "sharing.scope_invalid",
            "The share scope is invalid.")
    };
}
