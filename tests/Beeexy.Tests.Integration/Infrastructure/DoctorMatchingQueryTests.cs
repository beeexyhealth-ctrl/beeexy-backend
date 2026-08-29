using System.Text.Json;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class DoctorMatchingQueryTests(PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    [Fact]
    public async Task Calculate_UsesOnlyEligibleStoredRelationshipsWithExactExpectedTotals()
    {
        await ImportPackagesAsync();
        await using var dbContext = CreateDbContext();
        var useCase = UseCase(dbContext);

        var result = await useCase.ExecuteAsync(new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            "demo-specialty-general",
            "demo-language-es",
            "Demo Central",
            "Synthetic Demo Region",
            "Synthetic Demo Country",
            "demo-plan-blue"));

        Assert.Equal(ProductApprovedDemoDoctorMatchRule.Version, result.Rule.Version);
        Assert.Equal(
            [
                (DoctorId("21"), 100),
                (DoctorId("22"), 75),
                (DoctorId("25"), 25),
                (DoctorId("23"), 0)
            ],
            result.Candidates.Select(candidate =>
                (candidate.DoctorId, candidate.TotalDemoMatchScorePoints)));
        Assert.DoesNotContain(result.Candidates, candidate => candidate.DoctorId == DoctorId("24"));
        Assert.All(result.Candidates, candidate => Assert.Equal(4, candidate.Factors.Count));
    }

    [Fact]
    public async Task HiddenClinicLocationAndAffiliation_CannotContributeOrChangeTieOrder()
    {
        await ImportPackagesAsync();
        await using var dbContext = CreateDbContext();
        var useCase = UseCase(dbContext);
        var query = new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            Locality: "Demo North",
            AdministrativeArea: "Synthetic Demo Region",
            Country: "Synthetic Demo Country");

        var first = await useCase.ExecuteAsync(query);
        var second = await useCase.ExecuteAsync(query);

        Assert.All(first.Candidates, candidate =>
        {
            Assert.Equal(0, candidate.TotalDemoMatchScorePoints);
            Assert.Equal(DoctorMatchFactorState.NotMatched, candidate.Factors[2].State);
        });
        Assert.Equal(
            first.Candidates
                .Select(candidate => candidate.DoctorId.Value.ToString("D"))
                .Order(StringComparer.Ordinal),
            first.Candidates.Select(candidate => candidate.DoctorId.Value.ToString("D")));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task StoredInsuranceFactor_IsOnlyExactParticipationAndOutputHasNoLiveClaims()
    {
        await ImportPackagesAsync();
        await using var dbContext = CreateDbContext();
        var result = await UseCase(dbContext).ExecuteAsync(new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            InsurancePlanCode: "demo-plan-coral"));

        Assert.Equal(
            [DoctorId("23"), DoctorId("25")],
            result.Candidates
                .Where(candidate => candidate.TotalDemoMatchScorePoints == 25)
                .Select(candidate => candidate.DoctorId));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("eligibility", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coverage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("network", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patient", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clinical", json, StringComparison.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await ResetDirectoryAsync();
    }

    public Task DisposeAsync() => ResetDirectoryAsync();

    private async Task ImportPackagesAsync()
    {
        await using var dbContext = CreateDbContext();
        await new DirectoryImporter(
            dbContext,
            new DirectoryImportPackageValidator(),
            NullLogger<DirectoryImporter>.Instance)
            .ImportAsync(ProductApprovedSyntheticDirectory.Create());
        await new DoctorMatchRuleImporter(
            dbContext,
            new DoctorMatchRulePackageValidator(),
            NullLogger<DoctorMatchRuleImporter>.Instance)
            .ImportAsync(ProductApprovedDemoDoctorMatchRule.Create());
    }

    private static CalculateDoctorMatch UseCase(BeeexyDbContext dbContext) => new(
        new DoctorMatchingRepository(dbContext, new PublicDirectoryQueryBoundary(dbContext)),
        new DeterministicDoctorMatchEngine());

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task ResetDirectoryAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE directory.doctor_match_rule_configurations, " +
            "directory.demo_directory_imports, directory.doctor_affiliations, " +
            "directory.doctor_credentials, directory.doctor_insurance_participations, " +
            "directory.doctor_languages, directory.doctor_specialties, " +
            "directory.clinic_locations, directory.clinics, directory.doctors, " +
            "directory.insurance_plans, directory.languages, directory.specialties, " +
            "directory.doctor_match_rule_versions;";
        await command.ExecuteNonQueryAsync();
    }

    private static EntityId DoctorId(string suffix) => EntityId.From(Guid.Parse(
        $"71020000-0000-4200-8000-0000000000{suffix}"));
}
