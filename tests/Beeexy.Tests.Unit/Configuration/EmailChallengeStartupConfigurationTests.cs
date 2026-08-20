using Beeexy.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class EmailChallengeStartupConfigurationTests
{
    [Fact]
    public void ValidNonProductionSettings_CreateConcretePolicy()
    {
        var settings = StartupConfiguration.GetRequiredEmailChallengeSettings(
            BuildConfiguration(),
            new StubEnvironment("Development"));

        Assert.Equal(6, settings.Policy.CodeLength);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.Policy.Lifetime);
        Assert.Equal(3, settings.Policy.EmailPermitLimit);
        Assert.Equal(20, settings.Policy.IpPermitLimit);
        Assert.Equal(5, settings.Policy.MaximumVerificationAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.Policy.RateLimitWindow);
        Assert.True(settings.UseInMemoryEmailSender);
    }

    [Fact]
    public void ShortHashingKey_IsRejectedWithoutEchoingSecret()
    {
        const string shortSecret = "short-secret";
        var configuration = BuildConfiguration(
            ("Authentication:EmailChallenge:OtpHashingKey", shortSecret));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredEmailChallengeSettings(
                configuration,
                new StubEnvironment("Development")));

        Assert.DoesNotContain(shortSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InMemoryEmailSender_IsRejectedInProduction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredEmailChallengeSettings(
                BuildConfiguration(),
                new StubEnvironment("Production")));
    }

    [Fact]
    public void UnavailableEmailSender_IsAcceptedInProductionWithoutEnablingInMemoryDelivery()
    {
        var settings = StartupConfiguration.GetRequiredEmailChallengeSettings(
            BuildConfiguration(("Authentication:EmailSender:Provider", "Unavailable")),
            new StubEnvironment("Production"));

        Assert.False(settings.UseInMemoryEmailSender);
    }

    private static IConfiguration BuildConfiguration(
        params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:EmailChallenge:CodeLength"] = "6",
            ["Authentication:EmailChallenge:LifetimeMinutes"] = "10",
            ["Authentication:EmailChallenge:EmailPermitLimit"] = "3",
            ["Authentication:EmailChallenge:IpPermitLimit"] = "20",
            ["Authentication:EmailChallenge:MaximumVerificationAttempts"] = "5",
            ["Authentication:EmailChallenge:RateLimitWindowMinutes"] = "15",
            ["Authentication:EmailChallenge:OtpHashingKey"] =
                "unit-test-only-hmac-key-with-at-least-32-bytes",
            ["Authentication:EmailSender:Provider"] = "InMemory"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Beeexy.Tests.Unit";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
