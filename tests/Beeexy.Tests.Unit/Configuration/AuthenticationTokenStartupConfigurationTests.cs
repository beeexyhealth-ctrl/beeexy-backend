using Beeexy.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class AuthenticationTokenStartupConfigurationTests
{
    [Fact]
    public void ValidSettings_CreateConcreteTokenPolicy()
    {
        var policy = StartupConfiguration.GetRequiredAuthenticationTokenPolicy(
            BuildConfiguration());

        Assert.Equal("unit-test-issuer", policy.Issuer);
        Assert.Equal("unit-test-audience", policy.Audience);
        Assert.Equal(TimeSpan.FromMinutes(15), policy.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(30), policy.RefreshTokenLifetime);
    }

    [Theory]
    [InlineData("Authentication:Tokens:Issuer")]
    [InlineData("Authentication:Tokens:Audience")]
    public void MissingIssuerOrAudience_IsRejected(string key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredAuthenticationTokenPolicy(
                BuildConfiguration((key, string.Empty))));
    }

    [Fact]
    public void ShortSigningKey_IsRejectedWithoutEchoingSecret()
    {
        const string secret = "short-signing-secret";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredAuthenticationTokenPolicy(
                BuildConfiguration(("Authentication:Tokens:SigningKey", secret))));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authentication:Tokens:AccessTokenLifetimeMinutes", "0")]
    [InlineData("Authentication:Tokens:AccessTokenLifetimeMinutes", "61")]
    [InlineData("Authentication:Tokens:RefreshTokenLifetimeDays", "0")]
    [InlineData("Authentication:Tokens:RefreshTokenLifetimeDays", "366")]
    public void InvalidLifetime_IsRejected(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredAuthenticationTokenPolicy(
                BuildConfiguration((key, value))));
    }

    private static IConfiguration BuildConfiguration(
        params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Tokens:Issuer"] = "unit-test-issuer",
            ["Authentication:Tokens:Audience"] = "unit-test-audience",
            ["Authentication:Tokens:SigningKey"] =
                "unit-test-signing-key-with-at-least-32-bytes",
            ["Authentication:Tokens:AccessTokenLifetimeMinutes"] = "15",
            ["Authentication:Tokens:RefreshTokenLifetimeDays"] = "30"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
