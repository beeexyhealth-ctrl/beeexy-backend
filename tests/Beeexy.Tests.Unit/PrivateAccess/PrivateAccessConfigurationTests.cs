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
        Assert.False(settings.DemoGuest.Enabled);
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

    [Fact]
    public void EnabledDemoGuest_WithCompleteApprovedProfile_IsAcceptedAndNormalized()
    {
        var settings = StartupConfiguration.GetPrivateAccessSettings(
            BuildConfiguration(ValidDemoGuestValues()
                .Select(value => (value.Key, value.Value))
                .ToArray()),
            new StubEnvironment(Environments.Production));

        Assert.True(settings.DemoGuest.Enabled);
        var definition = Assert.IsType<Beeexy.Application.Identity.DemoGuestDefinition>(
            settings.DemoGuest.Definition);
        Assert.Equal("demo.guest@example.com", definition.Email.Value);
        Assert.Equal("Bee", definition.FirstName.Value);
        Assert.Equal("Exy", definition.LastName.Value);
        Assert.Equal(new DateOnly(1990, 5, 20), definition.DateOfBirth);
        Assert.Equal(Beeexy.Domain.Patients.SexAssignedAtBirth.Female,
            definition.SexAssignedAtBirth);
        Assert.Equal("CA", definition.State.Code);
        Assert.Equal("America/Lima", definition.TimeZone.Value);
    }

    [Theory]
    [InlineData("PrivateAccess:DemoGuest:Email")]
    [InlineData("PrivateAccess:DemoGuest:FirstName")]
    [InlineData("PrivateAccess:DemoGuest:LastName")]
    [InlineData("PrivateAccess:DemoGuest:DateOfBirth")]
    [InlineData("PrivateAccess:DemoGuest:SexAssignedAtBirth")]
    [InlineData("PrivateAccess:DemoGuest:State")]
    [InlineData("PrivateAccess:DemoGuest:Timezone")]
    public void EnabledDemoGuest_MissingRequiredProfileConfiguration_FailsSafely(string key)
    {
        var values = ValidDemoGuestValues();
        values[key] = string.Empty;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetPrivateAccessSettings(
                BuildConfiguration(values.Select(value => (value.Key, value.Value)).ToArray()),
                new StubEnvironment(Environments.Production)));

        Assert.Contains("PrivateAccess:DemoGuest", exception.Message);
        Assert.DoesNotContain("demo.guest@example.com", exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DemoGuestCannotBeEnabledWhenPrivateAccessIsDisabled()
    {
        var values = ValidDemoGuestValues();
        values["PrivateAccess:Enabled"] = "false";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetPrivateAccessSettings(
                BuildConfiguration(values.Select(value => (value.Key, value.Value)).ToArray()),
                new StubEnvironment(Environments.Production)));

        Assert.Contains("requires 'PrivateAccess:Enabled'", exception.Message);
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

    private static Dictionary<string, string?> ValidDemoGuestValues()
    {
        var values = ValidValues();
        values["PrivateAccess:DemoGuest:Enabled"] = "true";
        values["PrivateAccess:DemoGuest:Email"] = " Demo.Guest@example.com ";
        values["PrivateAccess:DemoGuest:FirstName"] = " Bee ";
        values["PrivateAccess:DemoGuest:LastName"] = " Exy ";
        values["PrivateAccess:DemoGuest:DateOfBirth"] = "1990-05-20";
        values["PrivateAccess:DemoGuest:SexAssignedAtBirth"] = "Female";
        values["PrivateAccess:DemoGuest:State"] = "ca";
        values["PrivateAccess:DemoGuest:Timezone"] = "America/Lima";
        return values;
    }

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
