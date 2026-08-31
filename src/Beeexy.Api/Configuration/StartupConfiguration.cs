using System.Globalization;
using Beeexy.Api.PrivateAccess;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
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
    private const string ClinicalAiSectionKey = "ClinicalAi";
    private const string PreTriageEducationalVideosSectionKey =
        "PreTriageEducationalVideos";
    private const string PrivateAccessSectionKey = "PrivateAccess";
    private const string SchedulerAssignmentsSectionKey =
        "Scheduling:AppointmentSchedulers:Assignments";

    public static AppointmentSchedulerAssignments GetAppointmentSchedulerAssignments(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = new List<AppointmentSchedulerAssignment>();
        foreach (var entry in configuration
            .GetSection(SchedulerAssignmentsSectionKey)
            .GetChildren())
        {
            var keys = entry.GetChildren().Select(child => child.Key).ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            if (!keys.SetEquals(["AccountId", "ClinicIds"]) ||
                !Guid.TryParse(entry["AccountId"], out var accountId) ||
                accountId == Guid.Empty)
            {
                throw InvalidSchedulerConfiguration();
            }

            var clinicValues = entry.GetSection("ClinicIds")
                .GetChildren()
                .Select(child => child.Value)
                .ToArray();
            var clinicIds = new List<EntityId>(clinicValues.Length);
            foreach (var value in clinicValues)
            {
                if (!Guid.TryParse(value, out var clinicId) || clinicId == Guid.Empty)
                {
                    throw InvalidSchedulerConfiguration();
                }

                clinicIds.Add(EntityId.From(clinicId));
            }

            if (clinicIds.Count == 0 || clinicIds.Distinct().Count() != clinicIds.Count)
            {
                throw InvalidSchedulerConfiguration();
            }

            configured.Add(new AppointmentSchedulerAssignment(
                EntityId.From(accountId),
                clinicIds));
        }

        try
        {
            return AppointmentSchedulerAssignments.Create(configured);
        }
        catch (ArgumentException exception)
        {
            throw InvalidSchedulerConfiguration(exception);
        }
    }

    private static InvalidOperationException InvalidSchedulerConfiguration(
        Exception? innerException = null) => new(
        $"Configuration section '{SchedulerAssignmentsSectionKey}' is invalid.",
        innerException);

    public static PrivateAccessSettings GetPrivateAccessSettings(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(PrivateAccessSectionKey);
        var demoGuestSection = section.GetSection("DemoGuest");
        bool enabled;
        bool demoGuestEnabled;
        try
        {
            enabled = section.GetValue("Enabled", false);
            demoGuestEnabled = demoGuestSection.GetValue("Enabled", false);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{PrivateAccessSectionKey}' is invalid.",
                exception);
        }

        if (!enabled)
        {
            if (demoGuestEnabled)
            {
                throw new InvalidOperationException(
                    $"Configuration section '{PrivateAccessSectionKey}:DemoGuest' requires " +
                    $"'{PrivateAccessSectionKey}:Enabled'.");
            }

            return PrivateAccessSettings.Disabled;
        }

        var modeValue = section["AuthenticationMode"] ?? "Legacy";
        if (!Enum.TryParse<PrivateAccessAuthenticationMode>(modeValue, true, out var mode) ||
            !Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                $"Configuration section '{PrivateAccessSectionKey}:AuthenticationMode' is invalid.");
        }

        var username = section["Username"]?.Trim();
        var passwordHash = section["PasswordHash"];
        var keywordHash = section["KeywordHash"];
        var signingKeyValue = section["SessionSigningKey"];
        if (mode == PrivateAccessAuthenticationMode.Legacy &&
            (string.IsNullOrWhiteSpace(username) || username.Length > 128 ||
            !PrivateAccessPasswordHasher.IsValidEncodedHash(passwordHash) ||
            !PrivateAccessPasswordHasher.IsValidEncodedHash(keywordHash) ||
            string.IsNullOrWhiteSpace(signingKeyValue)))
        {
            throw new InvalidOperationException(
                $"Configuration section '{PrivateAccessSectionKey}' is incomplete or invalid.");
        }

        byte[]? signingKey = null;
        try
        {
            if (mode == PrivateAccessAuthenticationMode.Legacy &&
                !string.IsNullOrWhiteSpace(signingKeyValue))
            {
                signingKey = Convert.FromBase64String(signingKeyValue);
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{PrivateAccessSectionKey}' is incomplete or invalid.",
                exception);
        }

        if (mode == PrivateAccessAuthenticationMode.Legacy && signingKey?.Length < 32)
        {
            throw new InvalidOperationException(
                $"Configuration section '{PrivateAccessSectionKey}' is incomplete or invalid.");
        }

        var sessionLifetimeMinutes = GetRequiredPositiveInt(section, "SessionLifetimeMinutes");
        var loginPermitLimit = GetRequiredPositiveInt(section, "LoginPermitLimit");
        var loginRateLimitWindowMinutes = GetRequiredPositiveInt(
            section,
            "LoginRateLimitWindowMinutes");
        if (sessionLifetimeMinutes > 1_440 ||
            loginPermitLimit > 1_000 ||
            loginRateLimitWindowMinutes > 1_440)
        {
            throw new InvalidOperationException(
                $"Configuration section '{PrivateAccessSectionKey}' is invalid.");
        }

        var settings = new PrivateAccessSettings(
            true,
            mode,
            username,
            passwordHash,
            keywordHash,
            signingKey,
            TimeSpan.FromMinutes(sessionLifetimeMinutes),
            loginPermitLimit,
            TimeSpan.FromMinutes(loginRateLimitWindowMinutes),
            environment.IsProduction());
        return settings with
        {
            DemoGuest = mode == PrivateAccessAuthenticationMode.Legacy && demoGuestEnabled
                ? new DemoGuestSettings(true, GetRequiredDemoGuestDefinition(demoGuestSection))
                : DemoGuestSettings.Disabled
        };
    }

    private static DemoGuestDefinition GetRequiredDemoGuestDefinition(
        IConfigurationSection section)
    {
        const string sectionName = $"{PrivateAccessSectionKey}:DemoGuest";
        try
        {
            var dateOfBirthValue = section["DateOfBirth"];
            if (!DateOnly.TryParseExact(
                    dateOfBirthValue,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dateOfBirth) ||
                dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                throw new ArgumentException("Invalid date of birth.");
            }

            if (!Enum.TryParse<SexAssignedAtBirth>(
                    section["SexAssignedAtBirth"],
                    ignoreCase: false,
                    out var sexAssignedAtBirth) ||
                !Enum.IsDefined(sexAssignedAtBirth))
            {
                throw new ArgumentException("Invalid sex assigned at birth.");
            }

            return new DemoGuestDefinition(
                NormalizedEmail.Create(section["Email"] ?? string.Empty),
                PatientName.Create(section["FirstName"] ?? string.Empty),
                PatientName.Create(section["LastName"] ?? string.Empty),
                dateOfBirth,
                sexAssignedAtBirth,
                UsState.Create(section["State"] ?? string.Empty),
                UserTimeZone.Create(section["Timezone"] ?? string.Empty));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{sectionName}' is incomplete or invalid.",
                exception);
        }
    }

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

    public static ClinicalAiProviderOptions GetClinicalAiProviderOptions(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(ClinicalAiSectionKey);
        return new ClinicalAiProviderOptions(
            section["Provider"],
            section["ApiKey"],
            section["Model"],
            section["BaseUrl"],
            section.GetValue<int?>("TimeoutSeconds"),
            section.GetValue<bool?>("UseJsonObjectResponseFormat"));
    }

    public static PreTriageEducationalVideoOptions GetRequiredPreTriageEducationalVideoOptions(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(PreTriageEducationalVideosSectionKey);
        var configured = section.GetChildren().ToDictionary(
            child => child.Key,
            child => new PreTriageEducationalVideoConfiguration(
                child["Id"],
                child["Title"],
                child["Url"]),
            StringComparer.Ordinal);
        try
        {
            return PreTriageEducationalVideoOptions.Create(configured);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Configuration section '{PreTriageEducationalVideosSectionKey}' is invalid.",
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
