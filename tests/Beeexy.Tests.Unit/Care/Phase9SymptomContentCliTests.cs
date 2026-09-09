using Beeexy.Api.Operations;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Infrastructure.Care;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase93")]
public sealed class Phase9SymptomContentCliTests
{
    [Fact]
    public void CommandRecognition_IsExactAndDoesNotInterceptApiStartup()
    {
        Assert.True(Phase9SymptomContentCli.IsCommand(
            [Phase9SymptomContentCli.Command]));
        Assert.False(Phase9SymptomContentCli.IsCommand(
            [Phase9SymptomContentCli.DevelopmentCommand]));
        Assert.True(Phase9SymptomContentCli.IsDevelopmentCommand(
            [Phase9SymptomContentCli.DevelopmentCommand]));
        Assert.False(Phase9SymptomContentCli.IsCommand([]));
        Assert.False(Phase9SymptomContentCli.IsDevelopmentCommand([]));
        Assert.False(Phase9SymptomContentCli.IsCommand(["run-api"]));
        Assert.False(Phase9SymptomContentCli.IsDevelopmentCommand(["run-api"]));
        Assert.False(Phase9SymptomContentCli.IsCommand(
            [Phase9SymptomContentCli.Command, "unexpected"]));
        Assert.False(Phase9SymptomContentCli.IsDevelopmentCommand(
            [Phase9SymptomContentCli.DevelopmentCommand, "unexpected"]));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public async Task DevelopmentCommand_RejectsEveryNonDevelopmentEnvironmentBeforeDatabaseAccess(
        string? environmentName)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Phase9SymptomContentCli.ExecuteDevelopmentAsync(
                new ConfigurationBuilder().Build(),
                environmentName,
                new StringWriter()));

        Assert.Contains("requires ASPNETCORE_ENVIRONMENT=Development", exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void DevelopmentCommand_AcceptsExplicitLoopbackDatabaseTarget(string host)
    {
        var connectionString =
            $"Host={host};Database=beeexy;Username=beeexy;Password=local-only";

        var target = Phase9SymptomContentCli.GetRequiredDevelopmentDatabaseTarget(
            Configuration(connectionString));

        Assert.Equal(connectionString, target.ConnectionString);
        Assert.Equal(host, target.Host);
        Assert.Equal("beeexy", target.Database);
    }

    [Theory]
    [InlineData("production.example.com", "beeexy")]
    [InlineData("localhost,production.example.com", "beeexy")]
    [InlineData("localhost", "postgres")]
    [InlineData("localhost", "template0")]
    [InlineData("localhost", "template1")]
    public void DevelopmentCommand_RejectsUnsafeDatabaseTarget(string host, string database)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Phase9SymptomContentCli.GetRequiredDevelopmentDatabaseTarget(
                Configuration(
                    $"Host={host};Database={database};Username=user;Password=secret")));

        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.Ordinal);
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
        var importer = new ProbeImporter();
        services.AddSingleton<ISymptomDiaryContentImporter>(importer);
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

        var expected = AndreaSymptomDiaryPackages.CreateAll();
        var serializer = new SymptomDiaryPackageCanonicalSerializer();
        Assert.Equal(4, importer.Packages.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(
                serializer.Serialize(expected[index]),
                serializer.Serialize(importer.Packages[index]));
            Assert.Equal(
                expected[index].ExpectedContentHash,
                importer.Packages[index].ExpectedContentHash);
        }
    }

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeeexyDatabase"] = connectionString
            })
            .Build();

    private sealed class ProbeImporter : ISymptomDiaryContentImporter
    {
        public List<SymptomDiaryPackageDefinition> Packages { get; } = [];

        public Task<SymptomDiaryPackageImportResult> ImportAsync(
            SymptomDiaryPackageDefinition package,
            CancellationToken cancellationToken = default)
        {
            Packages.Add(package);
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
