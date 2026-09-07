using Beeexy.Api.Configuration;
using Beeexy.Application.Care;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Api.Operations;

internal static class Phase9SymptomContentCli
{
    public const string Command = "import-phase9-andrea-symptom-content";

    public static bool IsCommand(string[] args) =>
        args.Length == 1 && string.Equals(args[0], Command, StringComparison.Ordinal);

    public static async Task ExecuteAsync(
        IConfiguration configuration,
        string? environmentName,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.Equals(
                environmentName,
                Environments.Production,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The '{Command}' command requires ASPNETCORE_ENVIRONMENT=Production.");
        }

        var connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
            configuration);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SymptomDiaryPackageValidator>();
        services.AddSingleton<SymptomDiaryPackageCanonicalSerializer>();
        services.AddSingleton<SymptomDiaryPackageHashCalculator>();
        services.AddScoped<ISymptomDiaryContentImporter, SymptomDiaryContentImporter>();
        services.AddDbContext<BeeexyDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BeeexyDbContext).Assembly.FullName)));

        await using var serviceProvider = services.BuildServiceProvider();
        await RunAsync(
            serviceProvider,
            output ?? Console.Out,
            cancellationToken);
    }

    internal static async Task RunAsync(
        IServiceProvider services,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);
        await using var scope = services.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<ISymptomDiaryContentImporter>();
        foreach (var package in AndreaSymptomDiaryPackages.CreateAll())
        {
            var result = await importer.ImportAsync(package, cancellationToken);
            await output.WriteLineAsync(
                $"package={result.PackageCode.Value}@{result.PackageVersion.Value} " +
                $"pathway={package.Pathway.Value} hash={result.CanonicalContentHash.Value} " +
                $"status={result.Outcome} review={package.ContentStatus.ReviewStatus} " +
                $"approval={package.ContentStatus.ApprovalStatus} " +
                $"active={package.ActivatedAt.HasValue.ToString().ToLowerInvariant()}");
        }
    }
}
