using Beeexy.Api.Configuration;
using Beeexy.Infrastructure.Sharing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Unit.Configuration;

public sealed class ExportStartupConfigurationTests
{
    [Fact]
    [Trait("Category", "Phase116")]
    public void DevelopmentLocalStorage_UsesConfiguredRetentionAndPrivateRoot()
    {
        var configuration = Configuration(
            "LocalFileSystem",
            retentionDays: "45",
            localRoot: Path.Combine(Path.GetTempPath(), "beeexy-private-exports"));

        var settings = StartupConfiguration.GetRequiredExportSettings(
            configuration,
            new Environment(Environments.Development));

        Assert.Equal(TimeSpan.FromDays(45), settings.Generation.Retention);
        Assert.Equal(PrivateArtifactStorageProvider.LocalFileSystem,
            settings.Storage.Provider);
        Assert.NotNull(settings.Storage.LocalRoot);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public void Production_RequiresConfiguredObjectStorageBoundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredExportSettings(
                Configuration("LocalFileSystem"),
                new Environment(Environments.Production)));

        var settings = StartupConfiguration.GetRequiredExportSettings(
            Configuration("ObjectStorage"),
            new Environment(Environments.Production));
        Assert.Equal(PrivateArtifactStorageProvider.ObjectStorage,
            settings.Storage.Provider);
        Assert.Equal("exports", settings.Storage.ObjectKeyPrefix);
    }

    [Theory]
    [InlineData("Unknown", "30", "26214400")]
    [InlineData("LocalFileSystem", "0", "26214400")]
    [InlineData("LocalFileSystem", "30", "0")]
    [Trait("Category", "Phase116")]
    public void InvalidExportConfiguration_FailsAtStartupBoundary(
        string provider,
        string retentionDays,
        string maximumBytes)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Exports:PrivateStorage:Provider"] = provider,
                ["Exports:RetentionDays"] = retentionDays,
                ["Exports:MaximumBeeexyJsonBytes"] = maximumBytes
            }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            StartupConfiguration.GetRequiredExportSettings(
                configuration,
                new Environment(Environments.Development)));
    }

    private static IConfiguration Configuration(
        string provider,
        string retentionDays = "30",
        string? localRoot = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Exports:PrivateStorage:Provider"] = provider,
                ["Exports:PrivateStorage:LocalRoot"] = localRoot,
                ["Exports:PrivateStorage:ObjectKeyPrefix"] = "exports",
                ["Exports:RetentionDays"] = retentionDays,
                ["Exports:MaximumBeeexyJsonBytes"] = "26214400"
            }).Build();

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Beeexy.Tests.Unit";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
