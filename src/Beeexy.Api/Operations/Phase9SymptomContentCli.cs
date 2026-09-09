using System.Net;
using Beeexy.Api.Configuration;
using Beeexy.Application.Care;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Api.Operations;

internal static class Phase9SymptomContentCli
{
    public const string Command = "import-phase9-andrea-symptom-content";
    public const string DevelopmentCommand =
        "import-phase9-andrea-symptom-content-development";

    public static bool IsCommand(string[] args) =>
        args.Length == 1 && string.Equals(args[0], Command, StringComparison.Ordinal);

    public static bool IsDevelopmentCommand(string[] args) =>
        args.Length == 1 &&
        string.Equals(args[0], DevelopmentCommand, StringComparison.Ordinal);

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

    public static async Task ExecuteDevelopmentAsync(
        IConfiguration configuration,
        string? environmentName,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.Equals(
                environmentName,
                Environments.Development,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The '{DevelopmentCommand}' command requires " +
                "ASPNETCORE_ENVIRONMENT=Development.");
        }

        var target = GetRequiredDevelopmentDatabaseTarget(configuration);
        var commandOutput = output ?? Console.Out;
        await commandOutput.WriteLineAsync(
            $"targetDatabaseHost={target.Host} targetDatabase={target.Database}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SymptomDiaryPackageValidator>();
        services.AddSingleton<SymptomDiaryPackageCanonicalSerializer>();
        services.AddSingleton<SymptomDiaryPackageHashCalculator>();
        services.AddScoped<ISymptomDiaryContentImporter, SymptomDiaryContentImporter>();
        services.AddDbContext<BeeexyDbContext>(options => options.UseNpgsql(
            target.ConnectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BeeexyDbContext).Assembly.FullName)));

        await using var serviceProvider = services.BuildServiceProvider();
        await RunAsync(
            serviceProvider,
            commandOutput,
            cancellationToken);
    }

    internal static DevelopmentDatabaseTarget GetRequiredDevelopmentDatabaseTarget(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
            configuration);

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The Development database connection string is invalid.",
                exception);
        }

        var hosts = (builder.Host ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(host => !IsLocalHost(host)))
        {
            throw new InvalidOperationException(
                $"The '{DevelopmentCommand}' command may only use a local database.");
        }

        var database = builder.Database?.Trim();
        if (string.IsNullOrWhiteSpace(database) || IsPostgreSqlSystemDatabase(database))
        {
            throw new InvalidOperationException(
                $"The '{DevelopmentCommand}' command requires an explicit non-system " +
                "Development database name.");
        }

        return new DevelopmentDatabaseTarget(
            connectionString,
            string.Join(',', hosts),
            database);
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

    private static bool IsLocalHost(string host)
    {
        var normalizedHost = host.Trim('[', ']');
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(normalizedHost, out var address) && IPAddress.IsLoopback(address)) ||
            normalizedHost.StartsWith('/');
    }

    private static bool IsPostgreSqlSystemDatabase(string database) =>
        database.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
        database.Equals("template0", StringComparison.OrdinalIgnoreCase) ||
        database.Equals("template1", StringComparison.OrdinalIgnoreCase);

    internal sealed record DevelopmentDatabaseTarget(
        string ConnectionString,
        string Host,
        string Database);
}
