using Beeexy.Api.Operations;
using Beeexy.Application.Directory;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Scheduling;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase8Acceptance")]
public sealed class Phase8DemoAvailabilityCliTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly ReferenceDate = new(2026, 8, 31);
    private readonly string _databaseName = $"phase8_cli_{Guid.NewGuid():N}";

    private string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
            {
                Database = _databaseName
            };
            return builder.ConnectionString;
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task SupportedEnvironment_ImportsConfiguredDatabaseAndRerunsIdempotently(
        string environmentName)
    {
        var firstOutput = new StringWriter();
        await Phase8DemoAvailabilityCli.ExecuteAsync(
            [Phase8DemoAvailabilityCli.Command, "2026-08-31"],
            Configuration(ConnectionString),
            environmentName,
            firstOutput);

        Assert.Equal(
            ExpectedLine("Imported"),
            Assert.Single(OutputLines(firstOutput)));

        var secondOutput = new StringWriter();
        await Phase8DemoAvailabilityCli.ExecuteAsync(
            [Phase8DemoAvailabilityCli.Command, "2026-08-31"],
            Configuration(ConnectionString),
            environmentName,
            secondOutput);

        Assert.Equal(
            ExpectedLine("AlreadyImported"),
            Assert.Single(OutputLines(secondOutput)));

        await using var verify = CreateDbContext();
        Assert.Equal(
            ProductApprovedSyntheticAvailability.SlotCount,
            await verify.AvailabilitySlots.CountAsync());
        Assert.Single(await verify.Set<AvailabilityImportRecord>().ToListAsync());
    }

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var directoryImporter = new DirectoryImporter(
            dbContext,
            new DirectoryImportPackageValidator(),
            NullLogger<DirectoryImporter>.Instance);
        await directoryImporter.ImportAsync(ProductApprovedSyntheticDirectory.Create());
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeeexyDatabase"] = connectionString
            })
            .Build();

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static string ExpectedLine(string status) =>
        $"package={ProductApprovedSyntheticAvailability.PackageCode}@" +
        $"{ProductApprovedSyntheticAvailability.Version} referenceDate={ReferenceDate:yyyy-MM-dd} " +
        $"hash={ProductApprovedSyntheticAvailability.ExpectedContentHash} status={status}";

    private static string[] OutputLines(StringWriter output) =>
        output.ToString().Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries);
}
