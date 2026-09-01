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

    [Fact]
    public async Task Command_IsProductionOnly()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Phase8DemoAvailabilityCli.ExecuteAsync(
                [Phase8DemoAvailabilityCli.Command, "2026-08-31"],
                new ConfigurationManager(),
                Environments.Development));

        Assert.Contains("requires ASPNETCORE_ENVIRONMENT=Production", exception.Message,
            StringComparison.Ordinal);
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
}
