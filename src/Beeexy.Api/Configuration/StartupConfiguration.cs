using Beeexy.Application.Identity;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Api.Configuration;

internal static class StartupConfiguration
{
    public const string CorsPolicyName = "ConfiguredFrontendOrigins";

    private const string CorsAllowedOriginsKey = "Cors:AllowedOrigins";
    private const string EmailChallengeSectionKey = "Authentication:EmailChallenge";
    private const string EmailSenderProviderKey = "Authentication:EmailSender:Provider";
    private const string ResendEmailSenderSectionKey = "Authentication:EmailSender:Resend";
    private const string TokenSectionKey = "Authentication:Tokens";
    private const string GoogleSectionKey = "Authentication:Google";
    private const string PreTriageCleanupSectionKey = "PreTriageCleanup";

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

    public static EmailChallengeStartupSettings GetRequiredEmailChallengeSettings(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(EmailChallengeSectionKey);
        var codeLength = GetRequiredPositiveInt(section, "CodeLength");
        var lifetimeMinutes = GetRequiredPositiveInt(section, "LifetimeMinutes");
        var emailPermitLimit = GetRequiredPositiveInt(section, "EmailPermitLimit");
        var ipPermitLimit = GetRequiredPositiveInt(section, "IpPermitLimit");
        var maximumVerificationAttempts = GetRequiredPositiveInt(
            section,
            "MaximumVerificationAttempts");
        var rateLimitWindowMinutes = GetRequiredPositiveInt(
            section,
            "RateLimitWindowMinutes");
        var otpHashingKey = section["OtpHashingKey"];

        if (string.IsNullOrWhiteSpace(otpHashingKey) || otpHashingKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"A secret of at least 32 characters must be configured in " +
                $"'{EmailChallengeSectionKey}:OtpHashingKey'.");
        }

        EmailChallengePolicy policy;
        try
        {
            policy = new EmailChallengePolicy(
                codeLength,
                TimeSpan.FromMinutes(lifetimeMinutes),
                emailPermitLimit,
                ipPermitLimit,
                TimeSpan.FromMinutes(rateLimitWindowMinutes),
                maximumVerificationAttempts);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{EmailChallengeSectionKey}' is invalid.",
                exception);
        }

        var emailSenderProvider = configuration[EmailSenderProviderKey];
        var useInMemoryEmailSender = string.Equals(
            emailSenderProvider,
            "InMemory",
            StringComparison.OrdinalIgnoreCase);
        var useResendEmailSender = string.Equals(
            emailSenderProvider,
            "Resend",
            StringComparison.OrdinalIgnoreCase);

        if (!useInMemoryEmailSender && !useResendEmailSender)
        {
            throw new InvalidOperationException(
                $"A supported provider must be configured in '{EmailSenderProviderKey}'.");
        }

        if (environment.IsProduction() && useInMemoryEmailSender)
        {
            throw new InvalidOperationException(
                "The in-memory authentication email sender cannot be used in Production.");
        }

        AuthenticationEmailSenderOptions emailSenderOptions;
        if (useInMemoryEmailSender)
        {
            emailSenderOptions = AuthenticationEmailSenderOptions.InMemory;
        }
        else
        {
            var resendSection = configuration.GetSection(ResendEmailSenderSectionKey);
            try
            {
                emailSenderOptions = AuthenticationEmailSenderOptions.CreateResend(
                    new ResendAuthenticationEmailOptions(
                        resendSection["ApiKey"] ?? string.Empty,
                        resendSection["SenderEmail"] ?? string.Empty,
                        resendSection["SenderDisplayName"] ?? string.Empty));
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    $"Configuration section '{ResendEmailSenderSectionKey}' is invalid. " +
                    "A valid API key, sender email, and sender display name are required.");
            }
        }

        return new EmailChallengeStartupSettings(
            policy,
            otpHashingKey,
            emailSenderOptions);
    }

    public static AuthenticationTokenPolicy GetRequiredAuthenticationTokenPolicy(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(TokenSectionKey);
        var issuer = section["Issuer"];
        var audience = section["Audience"];
        var signingKey = section["SigningKey"];
        var accessTokenLifetimeMinutes = GetRequiredPositiveInt(
            section,
            "AccessTokenLifetimeMinutes");
        var refreshTokenLifetimeDays = GetRequiredPositiveInt(
            section,
            "RefreshTokenLifetimeDays");

        try
        {
            return new AuthenticationTokenPolicy(
                issuer ?? string.Empty,
                audience ?? string.Empty,
                signingKey ?? string.Empty,
                TimeSpan.FromMinutes(accessTokenLifetimeMinutes),
                TimeSpan.FromDays(refreshTokenLifetimeDays));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{TokenSectionKey}' is invalid.",
                exception);
        }
    }

    public static GoogleAuthenticationStartupSettings GetGoogleAuthenticationSettings(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(GoogleSectionKey);
        bool enabled;
        try
        {
            enabled = section.GetValue("Enabled", false);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{GoogleSectionKey}' is invalid.",
                exception);
        }

        var clientId = section["ClientId"]?.Trim();
        if (enabled && string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                $"A Google client ID must be configured in '{GoogleSectionKey}:ClientId' " +
                "when Google authentication is enabled.");
        }

        return new GoogleAuthenticationStartupSettings(enabled, enabled ? clientId : null);
    }

    public static PreTriageCleanupOptions GetRequiredPreTriageCleanupOptions(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(PreTriageCleanupSectionKey);
        var cadenceMinutes = GetRequiredPositiveInt(section, "CadenceMinutes");
        var batchSize = GetRequiredPositiveInt(section, "BatchSize");
        var maximumBatches = GetRequiredPositiveInt(section, "MaximumBatchesPerRun");

        try
        {
            return new PreTriageCleanupOptions(
                TimeSpan.FromMinutes(cadenceMinutes),
                batchSize,
                maximumBatches);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{PreTriageCleanupSectionKey}' is invalid.",
                exception);
        }
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

    private static int GetRequiredPositiveInt(
        IConfigurationSection section,
        string settingName)
    {
        var value = section.GetValue<int?>(settingName);
        if (value is null or <= 0)
        {
            throw new InvalidOperationException(
                $"A positive integer must be configured in " +
                $"'{section.Path}:{settingName}'.");
        }

        return value.Value;
    }
}
