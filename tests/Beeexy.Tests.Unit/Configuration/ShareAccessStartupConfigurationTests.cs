using Beeexy.Api.Configuration;
using Beeexy.Application.Identity;
using Microsoft.Extensions.Configuration;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class ShareAccessStartupConfigurationTests
{
    [Fact]
    [Trait("Category", "Phase113")]
    public void ValidSettings_CreateDistinctTokenAndRateLimitPolicies()
    {
        var settings = StartupConfiguration.GetRequiredShareAccessSettings(
            BuildConfiguration(),
            AccountPolicy());

        Assert.Equal("unit-test-issuer", settings.TokenPolicy.Issuer);
        Assert.Equal("unit-test-share-audience", settings.TokenPolicy.Audience);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.TokenPolicy.MaximumLifetime);
        Assert.Equal(7, settings.RateLimitPolicy.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(90), settings.RateLimitPolicy.Window);
    }

    [Theory]
    [Trait("Category", "Phase113")]
    [InlineData("Sharing:AccessToken:Audience", "")]
    [InlineData("Sharing:AccessToken:Audience", "unit-test-account-audience")]
    [InlineData("Sharing:AccessToken:MaximumLifetimeMinutes", "0")]
    [InlineData("Sharing:AccessToken:MaximumLifetimeMinutes", "16")]
    [InlineData("Sharing:ExchangeRateLimit:PermitLimit", "0")]
    [InlineData("Sharing:ExchangeRateLimit:WindowSeconds", "0")]
    public void InvalidOrNonIsolatedSettings_AreRejected(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredShareAccessSettings(
                BuildConfiguration((key, value)),
                AccountPolicy()));
    }

    private static AuthenticationTokenPolicy AccountPolicy() => new(
        "unit-test-issuer",
        "unit-test-account-audience",
        "unit-test-share-signing-key-with-at-least-32-bytes",
        TimeSpan.FromMinutes(15),
        TimeSpan.FromDays(30));

    private static IConfiguration BuildConfiguration(
        params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Sharing:AccessToken:Audience"] = "unit-test-share-audience",
            ["Sharing:AccessToken:MaximumLifetimeMinutes"] = "15",
            ["Sharing:ExchangeRateLimit:PermitLimit"] = "7",
            ["Sharing:ExchangeRateLimit:WindowSeconds"] = "90"
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
