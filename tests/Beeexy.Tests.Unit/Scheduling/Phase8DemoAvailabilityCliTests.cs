using Beeexy.Api.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Unit.Scheduling;

[Trait("Category", "Phase8Acceptance")]
public sealed class Phase8DemoAvailabilityCliTests
{
    [Fact]
    public void CommandDetection_IsExplicitAndDoesNotMatchApiStartup()
    {
        Assert.True(Phase8DemoAvailabilityCli.IsCommand(
            [Phase8DemoAvailabilityCli.Command, "2026-08-31"]));
        Assert.False(Phase8DemoAvailabilityCli.IsCommand([]));
        Assert.False(Phase8DemoAvailabilityCli.IsCommand(["run-api"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task Command_RejectsEveryEnvironmentOtherThanDevelopmentAndProduction(
        string? environmentName)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Phase8DemoAvailabilityCli.ExecuteAsync(
                [Phase8DemoAvailabilityCli.Command, "2026-08-31"],
                new ConfigurationManager(),
                environmentName));

        Assert.Contains("explicitly set to Development or Production", exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Development_UsesExistingLocalDatabaseConnectionConfiguration(string host)
    {
        var connectionString =
            $"Host={host};Database=beeexy;Username=beeexy;Password=local-only";

        var resolved = Phase8DemoAvailabilityCli.GetRequiredCommandConnectionString(
            Configuration(connectionString),
            isDevelopment: true);

        Assert.Equal(connectionString, resolved);
    }

    [Fact]
    public void Development_RejectsRemoteDatabaseConnectionConfiguration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Phase8DemoAvailabilityCli.GetRequiredCommandConnectionString(
                Configuration(
                    "Host=production.example.com;Database=beeexy;Username=user;Password=secret"),
                isDevelopment: true));

        Assert.Contains("only use a local database in Development", exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_PreservesExistingConfiguredDatabaseConnectionBehavior()
    {
        const string connectionString =
            "Host=production.example.com;Database=beeexy;Username=user;Password=secret";

        var resolved = Phase8DemoAvailabilityCli.GetRequiredCommandConnectionString(
            Configuration(connectionString),
            isDevelopment: false);

        Assert.Equal(connectionString, resolved);
    }

    [Fact]
    public async Task Command_RequiresExplicitIsoReferenceDateBeforeReadingDatabaseConfiguration()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Phase8DemoAvailabilityCli.ExecuteAsync(
                [Phase8DemoAvailabilityCli.Command, "08/31/2026"],
                new ConfigurationManager(),
                Environments.Production));

        Assert.Contains("<reference-date:yyyy-MM-dd>", exception.Message,
            StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeeexyDatabase"] = connectionString
            })
            .Build();
}
