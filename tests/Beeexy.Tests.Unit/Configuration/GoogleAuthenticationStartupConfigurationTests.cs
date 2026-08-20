using Beeexy.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class GoogleAuthenticationStartupConfigurationTests
{
    [Fact]
    public void DisabledGoogle_DoesNotRequireClientId()
    {
        var settings = StartupConfiguration.GetGoogleAuthenticationSettings(
            BuildConfiguration(("Authentication:Google:Enabled", "false")));

        Assert.False(settings.Enabled);
        Assert.Null(settings.ClientId);
    }

    [Fact]
    public void EnabledGoogle_RequiresAndNormalizesClientId()
    {
        var settings = StartupConfiguration.GetGoogleAuthenticationSettings(
            BuildConfiguration(
                ("Authentication:Google:Enabled", "true"),
                ("Authentication:Google:ClientId", "  client.apps.googleusercontent.com  ")));

        Assert.True(settings.Enabled);
        Assert.Equal("client.apps.googleusercontent.com", settings.ClientId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnabledGoogle_WithoutClientIdFailsFast(string? clientId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetGoogleAuthenticationSettings(
                BuildConfiguration(
                    ("Authentication:Google:Enabled", "true"),
                    ("Authentication:Google:ClientId", clientId))));

        Assert.Contains("ClientId", exception.Message);
    }

    [Fact]
    public void InvalidEnabledValueFailsSafely()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetGoogleAuthenticationSettings(
                BuildConfiguration(("Authentication:Google:Enabled", "not-a-boolean"))));

        Assert.Contains("Authentication:Google", exception.ToString());
        Assert.DoesNotContain("stack", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();
    }
}
