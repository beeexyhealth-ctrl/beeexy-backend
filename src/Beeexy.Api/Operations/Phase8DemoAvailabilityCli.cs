using System.Globalization;
using System.Net;
using Beeexy.Api.Configuration;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Api.Operations;

internal static class Phase8DemoAvailabilityCli
{
    public const string Command = "import-phase8-demo-availability";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Command, StringComparison.Ordinal);

    public static async Task ExecuteAsync(
        string[] args,
        IConfiguration configuration,
        string? environmentName,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        var isDevelopment = string.Equals(
            environmentName,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);
        var isProduction = string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase);
        if (!isDevelopment && !isProduction)
        {
            throw new InvalidOperationException(
                $"The '{Command}' command requires ASPNETCORE_ENVIRONMENT to be explicitly " +
                $"set to Development or Production.");
        }

        if (args.Length != 2 || !DateOnly.TryParseExact(
            args[1],
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var referenceDate))
        {
            throw new InvalidOperationException(
                $"Usage: {Command} <reference-date:yyyy-MM-dd>.");
        }

        var connectionString = GetRequiredCommandConnectionString(
            configuration,
            isDevelopment);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AvailabilityImportPackageValidator>();
        services.AddSingleton<IClock>(_ => new CommandClock(DateTimeOffset.UtcNow));
        services.AddScoped<IAvailabilityImporter, AvailabilityImporter>();
        services.AddDbContext<BeeexyDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BeeexyDbContext).Assembly.FullName)));

        await using var serviceProvider = services.BuildServiceProvider();
        await RunAsync(
            serviceProvider,
            referenceDate,
            output ?? Console.Out,
            cancellationToken);
    }

    internal static string GetRequiredCommandConnectionString(
        IConfiguration configuration,
        bool isDevelopment)
    {
        var connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
            configuration);
        if (!isDevelopment)
        {
            return connectionString;
        }

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
                $"The '{Command}' command may only use a local database in Development.");
        }

        return connectionString;
    }

    internal static async Task RunAsync(
        IServiceProvider services,
        DateOnly referenceDate,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<IAvailabilityImporter>()
            .ImportAsync(
                ProductApprovedSyntheticAvailability.Create(referenceDate),
                cancellationToken);
        await output.WriteLineAsync(
            $"package={result.PackageCode.Value}@{result.Version.Value} " +
            $"referenceDate={result.ReferenceDate:yyyy-MM-dd} hash={result.ContentHash} " +
            $"status={result.Outcome}");
    }

    private sealed class CommandClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow.ToUniversalTime();
    }

    private static bool IsLocalHost(string host)
    {
        var normalizedHost = host.Trim('[', ']');
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(normalizedHost, out var address) && IPAddress.IsLoopback(address)) ||
            normalizedHost.StartsWith('/');
    }
}
