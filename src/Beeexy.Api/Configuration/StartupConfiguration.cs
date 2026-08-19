using Beeexy.Infrastructure.Persistence;

namespace Beeexy.Api.Configuration;

internal static class StartupConfiguration
{
    public const string CorsPolicyName = "ConfiguredFrontendOrigins";

    private const string CorsAllowedOriginsKey = "Cors:AllowedOrigins";

    public static string GetRequiredDatabaseConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(
            DatabaseConfiguration.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{DatabaseConfiguration.ConnectionStringName}' is not configured.");
        }

        return connectionString;
    }

    public static string[] GetRequiredCorsAllowedOrigins(IConfiguration configuration)
    {
        var configuredOrigins = configuration
            .GetSection(CorsAllowedOriginsKey)
            .Get<string[]>() ?? [];

        if (configuredOrigins.Length == 0 || configuredOrigins.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"At least one origin must be configured in '{CorsAllowedOriginsKey}'.");
        }

        var normalizedOrigins = configuredOrigins
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedOrigins.Any(origin => !IsValidOrigin(origin)))
        {
            throw new InvalidOperationException(
                $"Every origin in '{CorsAllowedOriginsKey}' must be an absolute HTTP(S) origin " +
                "without wildcards, credentials, paths, queries, fragments, or trailing slashes.");
        }

        return normalizedOrigins;
    }

    private static bool IsValidOrigin(string origin)
    {
        if (origin.Contains('*') || origin.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
