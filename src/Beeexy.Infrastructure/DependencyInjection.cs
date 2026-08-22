using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Time;
using Beeexy.Infrastructure.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        EmailChallengePolicy emailChallengePolicy,
        AuthenticationTokenPolicy authenticationTokenPolicy,
        GoogleExternalIdentityOptions googleOptions,
        string otpHashingKey,
        AuthenticationEmailSenderOptions authenticationEmailSenderOptions,
        PreTriageCleanupOptions preTriageCleanupOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(emailChallengePolicy);
        ArgumentNullException.ThrowIfNull(authenticationTokenPolicy);
        ArgumentNullException.ThrowIfNull(googleOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(otpHashingKey);
        ArgumentNullException.ThrowIfNull(authenticationEmailSenderOptions);
        ArgumentNullException.ThrowIfNull(preTriageCleanupOptions);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(emailChallengePolicy);
        services.AddSingleton(authenticationTokenPolicy);
        services.AddSingleton<IOneTimePasswordGenerator, CryptographicOneTimePasswordGenerator>();
        services.AddSingleton<IOneTimePasswordHasher>(
            _ => new HmacOneTimePasswordHasher(otpHashingKey));
        services.AddSingleton<IEmailChallengeRateLimiter, InMemoryEmailChallengeRateLimiter>();
        services.AddScoped<
            IEmailAuthenticationChallengeRepository,
            EmailAuthenticationChallengeRepository>();
        services.AddScoped<IAccountProvisioningRepository, AccountProvisioningRepository>();
        services.AddScoped<IIdentityVerificationTransaction, IdentityVerificationTransaction>();
        services.AddSingleton<IRefreshTokenService, CryptographicRefreshTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IAuthenticationSecurityLogger, AuthenticationSecurityLogger>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<
            IExternalIdentityAuthenticationRepository,
            ExternalIdentityAuthenticationRepository>();
        services.AddScoped<ICurrentAccountProfileRepository, CurrentAccountProfileRepository>();
        services.AddSingleton<IAccountProfileAuditLogger, AccountProfileAuditLogger>();
        services.AddScoped<IManagedPatientCreationRepository, ManagedPatientCreationRepository>();
        services.AddScoped<
            ICareRelationshipRevocationRepository,
            CareRelationshipRevocationRepository>();
        services.AddSingleton<ICareRelationshipAuditLogger, CareRelationshipAuditLogger>();
        services.AddScoped<IMyCircleReadRepository, MyCircleReadRepository>();
        services.AddSingleton<IMyCircleAuditLogger, MyCircleAuditLogger>();
        services.AddScoped<
            IPatientAccessAuthorizationRepository,
            PatientAccessAuthorizationRepository>();
        services.AddScoped<IPatientProfileReadRepository, PatientProfileReadRepository>();
        services.AddScoped<IPatientProfileUpdateRepository, PatientProfileUpdateRepository>();
        services.AddSingleton<IPatientProfileAuditLogger, PatientProfileAuditLogger>();
        services.AddSingleton<ClinicalDefinitionPackageValidator>();
        services.AddScoped<IClinicalDefinitionImporter, ClinicalDefinitionImporter>();
        services.AddScoped<IClinicalDefinitionProvider, ClinicalDefinitionProvider>();
        services.AddScoped<IClinicalPathwayRegistry, ClinicalPathwayRegistry>();
        services.AddSingleton<IClinicalAiProvider, UnavailableClinicalAiProvider>();
        services.AddSingleton<IClinicalSafetyPolicy, ClinicalSafetyPolicy>();
        services.AddScoped<IClinicalAiOutputValidator, ClinicalAiOutputValidator>();
        services.AddScoped<InterpretClinicalInput>();
        services.AddSingleton<
            IAnonymousPreTriageCapabilityService,
            CryptographicAnonymousPreTriageCapabilityService>();
        services.AddScoped<IPreTriageSessionRepository, PreTriageSessionRepository>();
        services.AddSingleton<IPreTriageSessionAuditLogger, PreTriageSessionAuditLogger>();
        services.AddScoped<IPreTriageAnswerRepository, PreTriageAnswerRepository>();
        services.AddSingleton<IPreTriageIntakeAuditLogger, PreTriageIntakeAuditLogger>();
        services.AddScoped<IPreTriageCompletionRepository, PreTriageCompletionRepository>();
        services.AddSingleton<IPreTriageCompletionAuditLogger, PreTriageCompletionAuditLogger>();
        services.AddScoped<IPreTriageClaimRepository, PreTriageClaimRepository>();
        services.AddSingleton<IPreTriageClaimAuditLogger, PreTriageClaimAuditLogger>();
        services.AddScoped<
            IPreTriageHistoryProjectionRepository,
            PreTriageHistoryProjectionRepository>();
        services.AddSingleton(preTriageCleanupOptions);
        services.AddSingleton(preTriageCleanupOptions.Policy);
        services.AddScoped<IPreTriageCleanupRepository, PreTriageCleanupRepository>();
        services.AddSingleton<IPreTriageCleanupTelemetry, PreTriageCleanupTelemetry>();
        services.AddHostedService<PreTriageCleanupWorker>();

        services.AddSingleton(googleOptions);
        if (googleOptions.Enabled)
        {
            services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
            services.AddSingleton<IExternalIdentityProvider, GoogleExternalIdentityProvider>();
        }
        else
        {
            services.AddSingleton<IExternalIdentityProvider, DisabledExternalIdentityProvider>();
        }

        if (authenticationEmailSenderOptions.Provider ==
            AuthenticationEmailSenderProvider.InMemory)
        {
            services.AddSingleton<InMemoryAuthenticationEmailSender>();
            services.AddSingleton<IAuthenticationEmailSender>(provider =>
                provider.GetRequiredService<InMemoryAuthenticationEmailSender>());
        }
        else
        {
            var resendOptions = authenticationEmailSenderOptions.Resend ??
                throw new InvalidOperationException(
                    "Resend authentication email options are required.");
            services.AddSingleton<IAuthenticationEmailSender>(_ =>
                new ResendAuthenticationEmailSender(new HttpClient(), resendOptions));
        }

        services.AddDbContext<BeeexyDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(BeeexyDbContext).Assembly.FullName)));

        return services;
    }
}
