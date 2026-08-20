using Beeexy.Api.Configuration;
using Beeexy.Infrastructure.Identity;
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
        Assert.Equal(
            AuthenticationEmailSenderProvider.InMemory,
            settings.EmailSender.Provider);
        Assert.Null(settings.EmailSender.Resend);
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
    public void ValidResendEmailSender_IsAcceptedInProduction()
    {
        var settings = StartupConfiguration.GetRequiredEmailChallengeSettings(
            BuildConfiguration(
                ("Authentication:EmailSender:Provider", "Resend"),
                ("Authentication:EmailSender:Resend:ApiKey",
                    "re_unit_test_key_that_is_not_a_real_secret"),
                ("Authentication:EmailSender:Resend:SenderEmail", "auth@beeexy.test"),
                ("Authentication:EmailSender:Resend:SenderDisplayName", "Beeexy")),
            new StubEnvironment("Production"));

        Assert.Equal(
            AuthenticationEmailSenderProvider.Resend,
            settings.EmailSender.Provider);
        Assert.Equal("auth@beeexy.test", settings.EmailSender.Resend!.SenderEmail);
        Assert.Equal("Beeexy", settings.EmailSender.Resend.SenderDisplayName);
    }

    [Theory]
    [InlineData("Authentication:EmailSender:Resend:ApiKey", "")]
    [InlineData("Authentication:EmailSender:Resend:ApiKey", "not-a-resend-key")]
    [InlineData("Authentication:EmailSender:Resend:SenderEmail", "invalid")]
    [InlineData("Authentication:EmailSender:Resend:SenderDisplayName", "")]
    [InlineData("Authentication:EmailSender:Resend:SenderDisplayName", "Beeexy\nInjected")]
    public void InvalidResendSettings_AreRejected(string key, string value)
    {
        var configuration = BuildConfiguration(
            ("Authentication:EmailSender:Provider", "Resend"),
            ("Authentication:EmailSender:Resend:ApiKey",
                "re_unit_test_key_that_is_not_a_real_secret"),
            ("Authentication:EmailSender:Resend:SenderEmail", "auth@beeexy.test"),
            ("Authentication:EmailSender:Resend:SenderDisplayName", "Beeexy"),
            (key, value));

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredEmailChallengeSettings(
                configuration,
                new StubEnvironment("Production")));
    }

    [Fact]
    public void InvalidResendApiKey_IsRejectedWithoutEchoingSecret()
    {
        const string invalidSecret = "unexpected-production-provider-secret";
        var configuration = BuildConfiguration(
            ("Authentication:EmailSender:Provider", "Resend"),
            ("Authentication:EmailSender:Resend:ApiKey", invalidSecret),
            ("Authentication:EmailSender:Resend:SenderEmail", "auth@beeexy.test"),
            ("Authentication:EmailSender:Resend:SenderDisplayName", "Beeexy"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredEmailChallengeSettings(
                configuration,
                new StubEnvironment("Production")));

        Assert.DoesNotContain(invalidSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionalUnavailableProvider_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredEmailChallengeSettings(
                BuildConfiguration(("Authentication:EmailSender:Provider", "Unavailable")),
                new StubEnvironment("Production")));
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
