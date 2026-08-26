using Beeexy.Api.Configuration;
using Beeexy.Api.PrivateAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Unit.PrivateAccess;

public sealed class PrivateAccessConfigurationTests
{
    [Fact]
    public void Disabled_DoesNotRequireSecrets()
    {
        var settings = StartupConfiguration.GetPrivateAccessSettings(
            BuildConfiguration(("PrivateAccess:Enabled", "false")),
            new StubEnvironment(Environments.Production));

        Assert.False(settings.Enabled);
        Assert.Null(settings.SessionSigningKey);
    }

    [Fact]
    public void EnabledValidProductionConfiguration_IsAccepted()
    {
        var settings = StartupConfiguration.GetPrivateAccessSettings(
            BuildValidConfiguration(),
            new StubEnvironment(Environments.Production));

        Assert.True(settings.Enabled);
        Assert.Equal("BeeexyHealth", settings.Username);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.SessionLifetime);
        Assert.Equal(5, settings.LoginPermitLimit);
        Assert.True(settings.SecureCookie);
    }

    [Theory]
    [InlineData("PrivateAccess:Username")]
    [InlineData("PrivateAccess:PasswordHash")]
    [InlineData("PrivateAccess:KeywordHash")]
    [InlineData("PrivateAccess:SessionSigningKey")]
    public void EnabledMissingRequiredConfiguration_FailsWithoutLeakingSecrets(string key)
    {
        const string password = "UnitTestPassword!123";
        var values = ValidValues();
        values[key] = "";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetPrivateAccessSettings(
                BuildConfiguration(values.Select(value => (value.Key, value.Value)).ToArray()),
                new StubEnvironment(Environments.Production)));

        Assert.Contains("PrivateAccess", exception.Message);
        Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
    }

    private static IConfiguration BuildValidConfiguration() =>
        BuildConfiguration(ValidValues().Select(value => (value.Key, value.Value)).ToArray());

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["PrivateAccess:Enabled"] = "true",
        ["PrivateAccess:Username"] = " BeeexyHealth ",
        ["PrivateAccess:PasswordHash"] = PrivateAccessPasswordHasher.Hash("UnitTestPassword!123"),
        ["PrivateAccess:KeywordHash"] = PrivateAccessPasswordHasher.Hash("HealthTech"),
        ["PrivateAccess:SessionSigningKey"] = Convert.ToBase64String(new byte[32]),
        ["PrivateAccess:SessionLifetimeMinutes"] = "30",
        ["PrivateAccess:LoginPermitLimit"] = "5",
        ["PrivateAccess:LoginRateLimitWindowMinutes"] = "15"
    };

    private static IConfiguration BuildConfiguration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Beeexy.Tests.Unit";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
