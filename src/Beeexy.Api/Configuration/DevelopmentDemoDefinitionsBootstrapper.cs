using Beeexy.Application.Directory;
using Beeexy.Application.Triage;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Api.Configuration;

/// <summary>
/// Creates the local schema and imports the explicitly synthetic demo packages.
/// This service is registered only in the Development environment.
/// </summary>
public sealed class DevelopmentDemoDefinitionsBootstrapper(
    IServiceScopeFactory scopeFactory,
    ILogger<DevelopmentDemoDefinitionsBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeeexyDbContext>();

        // Local development is the sole environment allowed to apply migrations at startup.
        await dbContext.Database.MigrateAsync(cancellationToken);

        var importer = scope.ServiceProvider.GetRequiredService<IClinicalDefinitionImporter>();
        var packages = SimplifiedDemoDefinitionPackages.CreateAll();
        foreach (var package in packages)
        {
            await importer.ImportAsync(package, cancellationToken);
        }

        var directoryPackage = ProductApprovedSyntheticDirectory.Create();
        var directoryImporter = scope.ServiceProvider.GetRequiredService<IDirectoryImporter>();
        await directoryImporter.ImportAsync(directoryPackage, cancellationToken);

        var matchingPackage = ProductApprovedDemoDoctorMatchRule.Create();
        var matchingImporter = scope.ServiceProvider.GetRequiredService<IDoctorMatchRuleImporter>();
        await matchingImporter.ImportAsync(matchingPackage, cancellationToken);

        logger.LogInformation(
            "Local demo definitions are available and active for {Packages}.",
            string.Join(", ", packages.Select(package =>
                $"{package.Pathway.Value}@{package.Version.Value}")));
        logger.LogInformation(
            "Synthetic demo directory package {PackageCode}@{Version} is available with content " +
            "hash {ContentHash}. Published and Verified values are demo-dataset states only.",
            directoryPackage.PackageCode.Value,
            directoryPackage.Version.Value,
            directoryPackage.ContentHash);
        logger.LogInformation(
            "Demo-only doctor matching package {PackageCode}@{Version} is available with content " +
            "hash {ContentHash}. Its deterministic weights are not clinically validated or " +
            "production recommendation logic.",
            matchingPackage.PackageCode.Value,
            matchingPackage.Version.Value,
            matchingPackage.ContentHash);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
