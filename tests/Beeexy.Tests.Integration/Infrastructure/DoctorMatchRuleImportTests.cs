using Beeexy.Application.Directory;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class DoctorMatchRuleImportTests(PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    [Fact]
    public async Task ApprovedPackage_PersistsExactImmutableVersionHashAndIntegerWeights()
    {
        var package = ProductApprovedDemoDoctorMatchRule.Create();
        await using (var dbContext = CreateDbContext())
        {
            var result = await Importer(dbContext).ImportAsync(package);
            Assert.Equal(DoctorMatchRuleImportOutcome.Imported, result.Outcome);
            Assert.Equal(ProductApprovedDemoDoctorMatchRule.ExpectedContentHash, result.ContentHash);
        }

        await using var verify = CreateDbContext();
        var version = await verify.DoctorMatchRuleVersions.AsNoTracking().SingleAsync();
        var configuration = await verify.DoctorMatchRuleConfigurations.AsNoTracking().SingleAsync();
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.Version, version.Version.Value);
        Assert.Equal(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero), version.CreatedAt);
        Assert.Equal(version.Id, configuration.RuleVersionId);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.PackageCode, configuration.PackageCode.Value);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.ExpectedContentHash, configuration.ContentHash);
        Assert.Equal(25, configuration.SpecialtyWeightPoints);
        Assert.Equal(25, configuration.LanguageWeightPoints);
        Assert.Equal(25, configuration.LocationWeightPoints);
        Assert.Equal(25, configuration.StoredInsuranceWeightPoints);
        Assert.Empty(await verify.Doctors.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task IdenticalAndConcurrentImports_AreIdempotentWithoutDuplicateConfiguration()
    {
        await using (var dbContext = CreateDbContext())
        {
            Assert.Equal(
                DoctorMatchRuleImportOutcome.Imported,
                (await Importer(dbContext).ImportAsync(
                    ProductApprovedDemoDoctorMatchRule.Create())).Outcome);
            Assert.Equal(
                DoctorMatchRuleImportOutcome.AlreadyImported,
                (await Importer(dbContext).ImportAsync(
                    ProductApprovedDemoDoctorMatchRule.Create())).Outcome);
        }

        await ResetRulesAsync();
        await using var first = CreateDbContext();
        await using var second = CreateDbContext();
        var outcomes = await Task.WhenAll(
            Importer(first).ImportAsync(ProductApprovedDemoDoctorMatchRule.Create()),
            Importer(second).ImportAsync(ProductApprovedDemoDoctorMatchRule.Create()));

        Assert.Contains(outcomes, value => value.Outcome == DoctorMatchRuleImportOutcome.Imported);
        Assert.Contains(
            outcomes,
            value => value.Outcome == DoctorMatchRuleImportOutcome.AlreadyImported);
        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.DoctorMatchRuleVersions.CountAsync());
        Assert.Equal(1, await verify.DoctorMatchRuleConfigurations.CountAsync());
    }

    [Fact]
    public async Task SameVersionChangedOrIncompleteConfiguration_IsRejectedWithoutMutation()
    {
        var approved = ProductApprovedDemoDoctorMatchRule.Create();
        await using (var dbContext = CreateDbContext())
        {
            await Importer(dbContext).ImportAsync(approved);
        }

        var changed = DoctorMatchRulePackage.Create(
            approved.PackageCode,
            approved.Version,
            approved.CreatedAt,
            [
                Factor(DoctorMatchFactorCodes.Specialty, 40),
                Factor(DoctorMatchFactorCodes.Language, 20),
                Factor(DoctorMatchFactorCodes.Location, 20),
                Factor(DoctorMatchFactorCodes.StoredInsurance, 20)
            ]);
        await using (var dbContext = CreateDbContext())
        {
            await Assert.ThrowsAsync<DoctorMatchRuleImportConflictException>(() =>
                Importer(dbContext).ImportAsync(changed));
        }

        await using (var verify = CreateDbContext())
        {
            Assert.Equal(
                approved.ContentHash,
                (await verify.DoctorMatchRuleConfigurations.AsNoTracking().SingleAsync())
                    .ContentHash);
        }

        await ResetRulesAsync();
        await using (var dbContext = CreateDbContext())
        {
            dbContext.DoctorMatchRuleVersions.Add(DoctorMatchRuleVersion.Create(
                approved.Version,
                approved.CreatedAt));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            await Assert.ThrowsAsync<DoctorMatchRuleImportConflictException>(() =>
                Importer(dbContext).ImportAsync(approved));
        }
    }

    [Fact]
    public async Task DatabaseConstraints_RejectInvalidHashAndWeightTotals()
    {
        var package = ProductApprovedDemoDoctorMatchRule.Create();
        await using (var dbContext = CreateDbContext())
        {
            await Importer(dbContext).ImportAsync(package);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE directory.doctor_match_rule_configurations " +
            "SET specialty_weight_points = 24;";
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_doctor_match_rule_configurations_weights", exception.ConstraintName);
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await ResetRulesAsync();
    }

    public Task DisposeAsync() => ResetRulesAsync();

    private DoctorMatchRuleImporter Importer(BeeexyDbContext dbContext) => new(
        dbContext,
        new DoctorMatchRulePackageValidator(),
        NullLogger<DoctorMatchRuleImporter>.Instance);

    private static DoctorMatchRuleFactorDefinition Factor(string code, int weight) =>
        new(code, DoctorMatchFactorSemanticsCodes.For(code), weight);

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task ResetRulesAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE directory.doctor_match_rule_configurations, " +
            "directory.doctor_match_rule_versions;";
        await command.ExecuteNonQueryAsync();
    }
}
