using System.Net;
using System.Text.Json;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class DirectoryImportTests(PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    [Fact]
    public async Task ProductPackage_ImportsEveryCategoryWithDeterministicIdentifiersAndNoRules()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        await using (var dbContext = CreateDbContext())
        {
            var result = await CreateImporter(dbContext).ImportAsync(package);
            Assert.Equal(DirectoryImportOutcome.Imported, result.Outcome);
            Assert.Equal(package.ContentHash, result.ContentHash);
        }

        await using var verify = CreateDbContext();
        Assert.Equal(package.Clinics.Count, await verify.Clinics.CountAsync());
        Assert.Equal(package.ClinicLocations.Count, await verify.ClinicLocations.CountAsync());
        Assert.Equal(package.Doctors.Count, await verify.Doctors.CountAsync());
        Assert.Equal(package.DoctorAffiliations.Count, await verify.DoctorAffiliations.CountAsync());
        Assert.Equal(package.DoctorCredentials.Count, await verify.DoctorCredentials.CountAsync());
        Assert.Equal(package.Specialties.Count, await verify.Specialties.CountAsync());
        Assert.Equal(package.DoctorSpecialties.Count, await verify.DoctorSpecialties.CountAsync());
        Assert.Equal(package.Languages.Count, await verify.Languages.CountAsync());
        Assert.Equal(package.DoctorLanguages.Count, await verify.DoctorLanguages.CountAsync());
        Assert.Equal(package.InsurancePlans.Count, await verify.InsurancePlans.CountAsync());
        Assert.Equal(
            package.DoctorInsuranceParticipations.Count,
            await verify.DoctorInsuranceParticipations.CountAsync());
        Assert.Empty(await verify.DoctorMatchRuleVersions.ToListAsync());
        Assert.Empty(await verify.DoctorMatchRuleConfigurations.ToListAsync());
        Assert.Equal(1L, await CountImportRecordsAsync());
        Assert.Equal(
            package.Doctors.Select(value => value.Id).OrderBy(value => value.Value),
            (await verify.Doctors.AsNoTracking().ToListAsync())
                .Select(value => value.Id)
                .OrderBy(value => value.Value));
    }

    [Fact]
    public async Task IdenticalAndConcurrentReruns_AreIdempotentWithoutDuplicateRows()
    {
        var firstPackage = ProductApprovedSyntheticDirectory.Create();
        await using (var dbContext = CreateDbContext())
        {
            Assert.Equal(
                DirectoryImportOutcome.Imported,
                (await CreateImporter(dbContext).ImportAsync(firstPackage)).Outcome);
            Assert.Equal(
                DirectoryImportOutcome.AlreadyImported,
                (await CreateImporter(dbContext).ImportAsync(
                    ProductApprovedSyntheticDirectory.Create())).Outcome);
        }

        await ResetDirectoryAsync();
        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();
        var outcomes = await Task.WhenAll(
            CreateImporter(firstContext).ImportAsync(ProductApprovedSyntheticDirectory.Create()),
            CreateImporter(secondContext).ImportAsync(ProductApprovedSyntheticDirectory.Create()));

        Assert.Contains(outcomes, value => value.Outcome == DirectoryImportOutcome.Imported);
        Assert.Contains(outcomes, value => value.Outcome == DirectoryImportOutcome.AlreadyImported);
        Assert.Equal(1L, await CountImportRecordsAsync());
        await using var verify = CreateDbContext();
        Assert.Equal(firstPackage.Doctors.Count, await verify.Doctors.CountAsync());
        Assert.Equal(firstPackage.DoctorAffiliations.Count, await verify.DoctorAffiliations.CountAsync());
    }

    [Fact]
    public async Task SameVersionWithChangedContent_IsRejectedWithoutMutation()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        await using (var dbContext = CreateDbContext())
        {
            await CreateImporter(dbContext).ImportAsync(package);
        }

        var changedClinic = Clinic.Create(
            package.Clinics[0].Code,
            DirectoryName.Create("Synthetic Demo Clinic Mutated Content"),
            package.Clinics[0].IsPublished,
            package.Clinics[0].CreatedAt,
            package.Clinics[0].Id);
        var changed = Copy(package, clinics: [changedClinic, .. package.Clinics.Skip(1)]);
        await using (var dbContext = CreateDbContext())
        {
            await Assert.ThrowsAsync<DirectoryImportConflictException>(() =>
                CreateImporter(dbContext).ImportAsync(changed));
        }

        await using var verify = CreateDbContext();
        Assert.Equal(
            package.Clinics[0].Name,
            (await verify.Clinics.AsNoTracking().SingleAsync(value =>
                value.Id == package.Clinics[0].Id)).Name);
        Assert.Equal(1L, await CountImportRecordsAsync());
    }

    [Fact]
    public async Task InvalidReferenceAndPersistenceConflict_LeaveNoPartialPackage()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        var affiliation = package.DoctorAffiliations[0];
        var invalidAffiliation = DoctorAffiliation.Create(
            EntityId.New(),
            affiliation.ClinicId,
            affiliation.ClinicLocationId,
            affiliation.IsPublished,
            affiliation.CreatedAt,
            affiliation.Id);
        var invalid = Copy(
            package,
            affiliations: [invalidAffiliation, .. package.DoctorAffiliations.Skip(1)]);
        await using (var dbContext = CreateDbContext())
        {
            await Assert.ThrowsAsync<DirectoryImportValidationException>(() =>
                CreateImporter(dbContext).ImportAsync(invalid));
        }

        Assert.Equal(0L, await CountDirectoryRowsAsync());

        await using (var dbContext = CreateDbContext())
        {
            dbContext.Clinics.Add(Clinic.Create(
                package.Clinics[0].Code,
                DirectoryName.Create("Unrelated conflicting test row"),
                false,
                package.Clinics[0].CreatedAt));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            await Assert.ThrowsAsync<DirectoryImportConflictException>(() =>
                CreateImporter(dbContext).ImportAsync(package));
        }

        await using var verify = CreateDbContext();
        Assert.Single(await verify.Clinics.AsNoTracking().ToListAsync());
        Assert.Empty(await verify.Doctors.AsNoTracking().ToListAsync());
        Assert.Equal(0L, await CountImportRecordsAsync());
        Assert.Equal(0L, await CountMatchRuleConfigurationsAsync());
    }

    [Fact]
    public async Task PublicQueryBoundary_ExcludesUnpublishedParentsRelationshipsAndClaims()
    {
        await using var dbContext = CreateDbContext();
        await CreateImporter(dbContext).ImportAsync(ProductApprovedSyntheticDirectory.Create());
        var boundary = new PublicDirectoryQueryBoundary(dbContext);

        Assert.Equal(
            ["demo-clinic-aurora", "demo-clinic-mosaic"],
            await boundary.Clinics().OrderBy(value => value.Code).Select(value => value.Code.Value)
                .ToArrayAsync());
        Assert.Equal(4, await boundary.Doctors().CountAsync());
        Assert.Equal(2, await boundary.ClinicLocations().CountAsync());
        Assert.Equal(3, await boundary.DoctorAffiliations().CountAsync());
        var credentials = await boundary.DoctorCredentials().ToArrayAsync();
        Assert.Equal(2, credentials.Length);
        Assert.All(credentials, value =>
            Assert.Equal(DoctorCredentialStatus.Verified, value.Status));
        Assert.DoesNotContain(credentials, value =>
            value.DoctorId.Value == Guid.Parse("71020000-0000-4200-8000-000000000024"));
    }

    [Fact]
    public async Task InsuranceParticipation_IsStoredDirectoryDataWithoutRealtimeClaims()
    {
        var package = ProductApprovedSyntheticDirectory.Create();
        await using var dbContext = CreateDbContext();
        await CreateImporter(dbContext).ImportAsync(package);

        Assert.Equal(
            package.DoctorInsuranceParticipations.Count,
            await dbContext.DoctorInsuranceParticipations.AsNoTracking().CountAsync());
        Assert.Equal(
            ["CreatedAt", "DoctorId", "Id", "InsurancePlanId"],
            typeof(DoctorInsuranceParticipation).GetProperties()
                .Select(value => value.Name)
                .Order()
                .ToArray());
    }

    [Fact]
    public async Task Bootstrap_IsDevelopmentOnly_AndClinicRoutesDoNotExposeImportOperations()
    {
        using (var productionFactory = new BeeexyApiFactory(
            postgres.ConnectionString,
            environment: "Production"))
        using (var productionClient = productionFactory.CreateApiClient())
        using (var response = await productionClient.GetAsync("/health/live"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(0L, await CountImportRecordsAsync());

        using var developmentFactory = new BeeexyApiFactory(postgres.ConnectionString);
        using var developmentClient = developmentFactory.CreateApiClient();
        using (var response = await developmentClient.GetAsync("/swagger/v1/swagger.json"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToArray();
            Assert.Equal(46, paths.Length);
            Assert.Equal(2, paths.Count(value =>
                value.Name.StartsWith("/api/v1/clinics", StringComparison.Ordinal)));
            Assert.Equal(3, paths.Count(value =>
                value.Name.StartsWith("/api/v1/doctors", StringComparison.Ordinal)));
            Assert.DoesNotContain(paths, value =>
                value.Name.Contains("directory-import", StringComparison.Ordinal));
        }

        Assert.Equal(1L, await CountImportRecordsAsync());
        Assert.Equal(1L, await CountMatchRuleConfigurationsAsync());
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await ResetDirectoryAsync();
    }

    public Task DisposeAsync() => ResetDirectoryAsync();

    private DirectoryImporter CreateImporter(BeeexyDbContext dbContext) =>
        new(
            dbContext,
            new DirectoryImportPackageValidator(),
            NullLogger<DirectoryImporter>.Instance);

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task ResetDirectoryAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE scheduling.appointment_reschedule_history, " +
            "scheduling.appointment_status_history, scheduling.appointments, " +
            "scheduling.availability_slots, scheduling.demo_availability_imports, " +
            "directory.doctor_match_rule_configurations, " +
            "directory.demo_directory_imports, directory.doctor_affiliations, " +
            "directory.doctor_credentials, directory.doctor_insurance_participations, " +
            "directory.doctor_languages, directory.doctor_specialties, " +
            "directory.clinic_locations, directory.clinics, directory.doctors, " +
            "directory.insurance_plans, directory.languages, directory.specialties, " +
            "directory.doctor_match_rule_versions;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountImportRecordsAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM directory.demo_directory_imports;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> CountMatchRuleConfigurationsAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM directory.doctor_match_rule_configurations;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> CountDirectoryRowsAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT count(*) FROM directory.demo_directory_imports) + " +
            "(SELECT count(*) FROM directory.clinics) + " +
            "(SELECT count(*) FROM directory.doctors) + " +
            "(SELECT count(*) FROM directory.specialties);";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static DirectoryImportPackage Copy(
        DirectoryImportPackage source,
        IEnumerable<Clinic>? clinics = null,
        IEnumerable<DoctorAffiliation>? affiliations = null) =>
        DirectoryImportPackage.Create(
            source.PackageCode,
            source.Version,
            clinics ?? source.Clinics,
            source.ClinicLocations,
            source.Doctors,
            affiliations ?? source.DoctorAffiliations,
            source.DoctorCredentials,
            source.Specialties,
            source.DoctorSpecialties,
            source.Languages,
            source.DoctorLanguages,
            source.InsurancePlans,
            source.DoctorInsuranceParticipations);
}
