namespace Beeexy.Infrastructure.Triage;

public sealed record ClinicalAiProviderOptions(
    string? Provider,
    string? ApiKey,
    string? Model,
    string? BaseUrl,
    int? TimeoutSeconds)
{
    public const string NvidiaProviderName = "NVIDIA";
    public const string DefaultNvidiaModel = "nvidia/nemotron-3.5-lightning-30b-a3b";
    public const string DefaultNvidiaBaseUrl = "https://integrate.api.nvidia.com/v1";
    public const int DefaultTimeoutSeconds = 20;
    public const int MaximumTimeoutSeconds = 60;

    public bool TryCreateNvidia(out NvidiaClinicalAiOptions? options)
    {
        options = null;
        if (!string.Equals(Provider?.Trim(), NvidiaProviderName,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ApiKey))
        {
            return false;
        }

        var model = string.IsNullOrWhiteSpace(Model) ? DefaultNvidiaModel : Model.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultNvidiaBaseUrl : BaseUrl.Trim();
        var timeoutSeconds = TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (timeoutSeconds is <= 0 or > MaximumTimeoutSeconds ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        options = new NvidiaClinicalAiOptions(
            ApiKey.Trim(),
            model,
            EnsureTrailingSlash(uri),
            TimeSpan.FromSeconds(timeoutSeconds));
        return true;
    }

    private static Uri EnsureTrailingSlash(Uri value) =>
        value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? value
            : new Uri($"{value.AbsoluteUri}/", UriKind.Absolute);
}

public sealed record NvidiaClinicalAiOptions(
    string ApiKey,
    string Model,
    Uri BaseUri,
    TimeSpan Timeout);
