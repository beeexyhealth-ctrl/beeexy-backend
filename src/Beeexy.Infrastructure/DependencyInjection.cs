using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Time;
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
        bool useInMemoryAuthenticationEmailSender)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(emailChallengePolicy);
        ArgumentNullException.ThrowIfNull(authenticationTokenPolicy);
        ArgumentNullException.ThrowIfNull(googleOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(otpHashingKey);

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

        if (useInMemoryAuthenticationEmailSender)
        {
            services.AddSingleton<InMemoryAuthenticationEmailSender>();
            services.AddSingleton<IAuthenticationEmailSender>(provider =>
                provider.GetRequiredService<InMemoryAuthenticationEmailSender>());
        }
        else
        {
            services.AddSingleton<
                IAuthenticationEmailSender,
                UnavailableAuthenticationEmailSender>();
        }

        services.AddDbContext<BeeexyDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(BeeexyDbContext).Assembly.FullName)));

        return services;
    }
}
