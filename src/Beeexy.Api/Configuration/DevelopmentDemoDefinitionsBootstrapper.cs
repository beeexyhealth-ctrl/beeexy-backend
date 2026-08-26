using Beeexy.Application.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Api.Configuration;

/// <summary>
/// Creates the local schema and imports the explicitly non-clinical Phase 4 demo
/// definitions. This service is registered only in the Development environment.
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

        logger.LogInformation(
            "Local demo definitions are available and active for {Packages}.",
            string.Join(", ", packages.Select(package =>
                $"{package.Pathway.Value}@{package.Version.Value}")));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
