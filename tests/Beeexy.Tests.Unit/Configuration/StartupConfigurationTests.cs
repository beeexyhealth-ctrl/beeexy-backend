using Beeexy.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class StartupConfigurationTests
{
    [Fact]
    public void ValidConfiguration_ReturnsDatabaseAndCorsSettings()
    {
        var configuration = BuildConfiguration(
            "Host=localhost;Database=beeexy;Username=beeexy;Password=local-only",
            "https://app.example");

        var connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(configuration);
        var origins = StartupConfiguration.GetRequiredCorsAllowedOrigins(configuration);

        Assert.Contains("Database=beeexy", connectionString);
        Assert.Equal(["https://app.example"], origins);
    }

    [Fact]
    public void MissingDatabaseConnectionString_IsRejected()
    {
        var configuration = BuildConfiguration(string.Empty, "https://app.example");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredDatabaseConnectionString(configuration));

        Assert.Contains("BeeexyDatabase", exception.Message);
        Assert.DoesNotContain("Password", exception.Message);
    }

    [Fact]
    public void MissingCorsAllowedOrigins_IsRejected()
    {
        var configuration = BuildConfiguration(
            "Host=localhost;Database=beeexy;Username=beeexy;Password=local-only",
            null);

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredCorsAllowedOrigins(configuration));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://app.example/")]
    [InlineData("https://user:secret@app.example")]
    [InlineData("https://app.example/path")]
    public void UnsafeCorsOrigin_IsRejected(string origin)
    {
        var configuration = BuildConfiguration(
            "Host=localhost;Database=beeexy;Username=beeexy;Password=local-only",
            origin);

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredCorsAllowedOrigins(configuration));
    }

    private static IConfiguration BuildConfiguration(string connectionString, string? origin)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:BeeexyDatabase"] = connectionString
        };

        if (origin is not null)
        {
            values["Cors:AllowedOrigins:0"] = origin;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
