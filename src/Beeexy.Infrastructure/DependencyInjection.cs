using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Identity;
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
        string otpHashingKey,
        bool useInMemoryAuthenticationEmailSender)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(emailChallengePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(otpHashingKey);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(emailChallengePolicy);
        services.AddSingleton<IOneTimePasswordGenerator, CryptographicOneTimePasswordGenerator>();
        services.AddSingleton<IOneTimePasswordHasher>(
            _ => new HmacOneTimePasswordHasher(otpHashingKey));
        services.AddSingleton<IEmailChallengeRateLimiter, InMemoryEmailChallengeRateLimiter>();
        services.AddScoped<
            IEmailAuthenticationChallengeRepository,
            EmailAuthenticationChallengeRepository>();

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
