using Beeexy.Api.Operations;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Infrastructure.Care;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase93")]
public sealed class Phase9SymptomContentCliTests
{
    [Fact]
    public void CommandRecognition_IsExactAndDoesNotInterceptApiStartup()
    {
        Assert.True(Phase9SymptomContentCli.IsCommand(
            [Phase9SymptomContentCli.Command]));
        Assert.False(Phase9SymptomContentCli.IsCommand([]));
        Assert.False(Phase9SymptomContentCli.IsCommand(["run-api"]));
        Assert.False(Phase9SymptomContentCli.IsCommand(
            [Phase9SymptomContentCli.Command, "unexpected"]));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData(null)]
    public async Task Command_RejectsNonProductionEnvironmentBeforeDatabaseAccess(
        string? environmentName)
    {
        var configuration = new ConfigurationBuilder().Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Phase9SymptomContentCli.ExecuteAsync(
                configuration,
                environmentName,
                new StringWriter()));
    }

    [Fact]
    public async Task CommandOutput_ContainsOnlyTechnicalReleaseMetadata()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISymptomDiaryContentImporter, ProbeImporter>();
        await using var provider = services.BuildServiceProvider();
        var output = new StringWriter();

        await Phase9SymptomContentCli.RunAsync(
            provider,
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(4, text.Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("review=Reviewed approval=Approved active=true", text);
        Assert.DoesNotContain("When did", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Red flags", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Pressure/tightness", text, StringComparison.Ordinal);
        Assert.DoesNotContain("neurological", text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProbeImporter : ISymptomDiaryContentImporter
    {
        public Task<SymptomDiaryPackageImportResult> ImportAsync(
            SymptomDiaryPackageDefinition package,
            CancellationToken cancellationToken = default)
        {
            var serializer = new SymptomDiaryPackageCanonicalSerializer();
            var hash = new SymptomDiaryPackageHashCalculator(serializer).Calculate(package);
            return Task.FromResult(new SymptomDiaryPackageImportResult(
                SymptomDiaryPackageImportOutcome.Imported,
                Beeexy.Domain.Common.EntityId.New(),
                package.PackageCode,
                package.PackageVersion,
                hash));
        }
    }
}
