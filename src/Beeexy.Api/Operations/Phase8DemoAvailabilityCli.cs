using System.Globalization;
using Beeexy.Api.Configuration;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;

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
        if (!string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The '{Command}' command requires ASPNETCORE_ENVIRONMENT=Production.");
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

        var connectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
            configuration);
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
}
