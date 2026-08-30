using Beeexy.Api.Operations;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class Phase7DemoDirectoryCliTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private readonly string _migratedDatabaseName =
        $"phase7_cli_{Guid.NewGuid():N}";
    private readonly string _unmigratedDatabaseName =
        $"phase7_cli_empty_{Guid.NewGuid():N}";

    private string MigratedConnectionString => ConnectionString(_migratedDatabaseName);

    private string UnmigratedConnectionString => ConnectionString(_unmigratedDatabaseName);

    [Fact]
    public async Task ProductionCommand_ImportsBothPackagesAndIdenticalRerunIsIdempotent()
    {
        var firstOutput = new StringWriter();
        await Phase7DemoDirectoryCli.ExecuteAsync(
            Configuration(MigratedConnectionString),
            Environments.Production,
            firstOutput);

        Assert.Equal(
            [
                ExpectedLine(
                    ProductApprovedSyntheticDirectory.PackageCode,
                    ProductApprovedSyntheticDirectory.Version,
                    ProductApprovedSyntheticDirectory.ExpectedContentHash,
                    "Imported"),
                ExpectedLine(
                    ProductApprovedDemoDoctorMatchRule.PackageCode,
                    ProductApprovedDemoDoctorMatchRule.Version,
                    ProductApprovedDemoDoctorMatchRule.ExpectedContentHash,
                    "Imported")
            ],
            OutputLines(firstOutput));

        var directoryPackage = ProductApprovedSyntheticDirectory.Create();
        await using (var verify = CreateDbContext(MigratedConnectionString))
        {
            Assert.Equal(directoryPackage.Clinics.Count, await verify.Clinics.CountAsync());
            Assert.Equal(
                directoryPackage.ClinicLocations.Count,
                await verify.ClinicLocations.CountAsync());
            Assert.Equal(directoryPackage.Doctors.Count, await verify.Doctors.CountAsync());
            Assert.Equal(
                directoryPackage.DoctorAffiliations.Count,
                await verify.DoctorAffiliations.CountAsync());
            Assert.Equal(
                directoryPackage.DoctorCredentials.Count,
                await verify.DoctorCredentials.CountAsync());
            Assert.Equal(
                directoryPackage.Specialties.Count,
                await verify.Specialties.CountAsync());
            Assert.Equal(
                directoryPackage.DoctorSpecialties.Count,
                await verify.DoctorSpecialties.CountAsync());
            Assert.Equal(directoryPackage.Languages.Count, await verify.Languages.CountAsync());
            Assert.Equal(
                directoryPackage.DoctorLanguages.Count,
                await verify.DoctorLanguages.CountAsync());
            Assert.Equal(
                directoryPackage.InsurancePlans.Count,
                await verify.InsurancePlans.CountAsync());
            Assert.Equal(
                directoryPackage.DoctorInsuranceParticipations.Count,
                await verify.DoctorInsuranceParticipations.CountAsync());
            Assert.Single(await verify.DoctorMatchRuleVersions.ToListAsync());
            Assert.Single(await verify.DoctorMatchRuleConfigurations.ToListAsync());

            Assert.Empty(await verify.QuestionnaireVersions.ToListAsync());
            Assert.Empty(await verify.TriageQuestions.ToListAsync());
            Assert.Empty(await verify.ClinicalRuleSetVersions.ToListAsync());
        }

        var countsAfterFirstRun = await DirectoryCountsAsync(MigratedConnectionString);
        var secondOutput = new StringWriter();
        await Phase7DemoDirectoryCli.ExecuteAsync(
            Configuration(MigratedConnectionString),
            Environments.Production,
            secondOutput);

        Assert.Equal(
            [
                ExpectedLine(
                    ProductApprovedSyntheticDirectory.PackageCode,
                    ProductApprovedSyntheticDirectory.Version,
                    ProductApprovedSyntheticDirectory.ExpectedContentHash,
                    "AlreadyImported"),
                ExpectedLine(
                    ProductApprovedDemoDoctorMatchRule.PackageCode,
                    ProductApprovedDemoDoctorMatchRule.Version,
                    ProductApprovedDemoDoctorMatchRule.ExpectedContentHash,
                    "AlreadyImported")
            ],
            OutputLines(secondOutput));
        Assert.Equal(
            countsAfterFirstRun,
            await DirectoryCountsAsync(MigratedConnectionString));
    }

    [Fact]
    public async Task ProductionCommand_DoesNotApplyMigrationsToAnEmptyDatabase()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            Phase7DemoDirectoryCli.ExecuteAsync(
                Configuration(UnmigratedConnectionString),
                Environments.Production,
                new StringWriter()));
        Assert.Equal(PostgresErrorCodes.UndefinedTable, exception.SqlState);

        await using var connection = new NpgsqlConnection(UnmigratedConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory';";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync(_migratedDatabaseName);
        await CreateDatabaseAsync(_unmigratedDatabaseName);
        await using var dbContext = CreateDbContext(MigratedConnectionString);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await DropDatabaseAsync(_migratedDatabaseName);
        await DropDatabaseAsync(_unmigratedDatabaseName);
    }

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeeexyDatabase"] = connectionString
            })
            .Build();

    private BeeexyDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private string ConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    private async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\";";
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long[]> DirectoryCountsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT " +
            "(SELECT count(*) FROM directory.demo_directory_imports), " +
            "(SELECT count(*) FROM directory.clinics), " +
            "(SELECT count(*) FROM directory.clinic_locations), " +
            "(SELECT count(*) FROM directory.doctors), " +
            "(SELECT count(*) FROM directory.doctor_affiliations), " +
            "(SELECT count(*) FROM directory.doctor_credentials), " +
            "(SELECT count(*) FROM directory.specialties), " +
            "(SELECT count(*) FROM directory.doctor_specialties), " +
            "(SELECT count(*) FROM directory.languages), " +
            "(SELECT count(*) FROM directory.doctor_languages), " +
            "(SELECT count(*) FROM directory.insurance_plans), " +
            "(SELECT count(*) FROM directory.doctor_insurance_participations), " +
            "(SELECT count(*) FROM directory.doctor_match_rule_versions), " +
            "(SELECT count(*) FROM directory.doctor_match_rule_configurations);";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetInt64)
            .ToArray();
    }

    private static string ExpectedLine(
        string packageCode,
        string version,
        string contentHash,
        string status) =>
        $"package={packageCode}@{version} hash={contentHash} status={status}";

    private static string[] OutputLines(StringWriter output) =>
        output.ToString().Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries);
}
