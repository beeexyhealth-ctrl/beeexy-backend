using Beeexy.Api.Configuration;
using Beeexy.Application.Directory;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Api.Operations;

internal static class Phase7DemoDirectoryCli
{
    public const string Command = "import-phase7-demo-directory";

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
        services.AddSingleton<DirectoryImportPackageValidator>();
        services.AddScoped<IDirectoryImporter, DirectoryImporter>();
        services.AddSingleton<DoctorMatchRulePackageValidator>();
        services.AddScoped<IDoctorMatchRuleImporter, DoctorMatchRuleImporter>();
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

        var directoryPackage = ProductApprovedSyntheticDirectory.Create();
        var directoryResult = await scope.ServiceProvider
            .GetRequiredService<IDirectoryImporter>()
            .ImportAsync(directoryPackage, cancellationToken);
        await WriteResultAsync(
            output,
            directoryResult.PackageCode.Value,
            directoryResult.Version.Value,
            directoryResult.ContentHash,
            directoryResult.Outcome.ToString());

        var matchingPackage = ProductApprovedDemoDoctorMatchRule.Create();
        var matchingResult = await scope.ServiceProvider
            .GetRequiredService<IDoctorMatchRuleImporter>()
            .ImportAsync(matchingPackage, cancellationToken);
        await WriteResultAsync(
            output,
            matchingResult.PackageCode.Value,
            matchingResult.Version.Value,
            matchingResult.ContentHash,
            matchingResult.Outcome.ToString());
    }

    private static Task WriteResultAsync(
        TextWriter output,
        string packageCode,
        string version,
        string contentHash,
        string status) =>
        output.WriteLineAsync(
            $"package={packageCode}@{version} hash={contentHash} status={status}");
}
