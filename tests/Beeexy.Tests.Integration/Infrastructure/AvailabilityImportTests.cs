using Beeexy.Application.Directory;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Scheduling;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase8Acceptance")]
public sealed class AvailabilityImportTests(PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly ReferenceDate = new(2026, 8, 31);
    private static readonly DateTimeOffset ImportedAt =
        new(2026, 8, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProductPackage_ImportsExistingDirectoryRelationshipsAndRerunsIdempotently()
    {
        await ImportDirectoryAsync();
        var package = ProductApprovedSyntheticAvailability.Create(ReferenceDate);

        await using (var first = CreateDbContext())
        {
            var result = await CreateImporter(first).ImportAsync(package);
            Assert.Equal(AvailabilityImportOutcome.Imported, result.Outcome);
        }

        await using (var second = CreateDbContext())
        {
            var result = await CreateImporter(second).ImportAsync(
                ProductApprovedSyntheticAvailability.Create(ReferenceDate));
            Assert.Equal(AvailabilityImportOutcome.AlreadyImported, result.Outcome);
        }

        await using var verify = CreateDbContext();
        var slots = await verify.AvailabilitySlots.AsNoTracking()
            .OrderBy(value => value.StartsAt)
            .ThenBy(value => value.Id)
            .ToArrayAsync();
        Assert.Equal(ProductApprovedSyntheticAvailability.SlotCount, slots.Length);
        foreach (var slot in slots)
        {
            Assert.True(await verify.Doctors.AnyAsync(value => value.Id == slot.DoctorId));
            Assert.True(await verify.Clinics.AnyAsync(value => value.Id == slot.ClinicId));
            Assert.True(await verify.ClinicLocations.AnyAsync(value =>
                value.Id == slot.ClinicLocationId && value.ClinicId == slot.ClinicId));
            Assert.Equal(TimeSpan.FromMinutes(30), slot.Duration);
        }
        Assert.Equal(1L, await CountImportRecordsAsync());
    }

    [Fact]
    public async Task MissingDirectoryReferences_FailBeforeAnySchedulingMutation()
    {
        var package = ProductApprovedSyntheticAvailability.Create(ReferenceDate);
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<AvailabilityImportValidationException>(() =>
            CreateImporter(dbContext).ImportAsync(package));

        Assert.Empty(await dbContext.AvailabilitySlots.AsNoTracking().ToArrayAsync());
        Assert.Equal(0L, await CountImportRecordsAsync());
    }

    [Fact]
    public async Task SameIdentityWithChangedContent_IsRejectedWithoutDuplicates()
    {
        await ImportDirectoryAsync();
        var original = ProductApprovedSyntheticAvailability.Create(ReferenceDate);
        await using (var first = CreateDbContext())
        {
            await CreateImporter(first).ImportAsync(original);
        }

        var source = original.Slots[0];
        var changedSlot = AvailabilitySlot.Create(
            source.DoctorId,
            source.ClinicId,
            source.ClinicLocationId,
            source.StartsAt,
            source.EndsAt,
            source.ClinicTimeZone,
            source.Modality == AppointmentModality.InPerson
                ? AppointmentModality.Virtual
                : AppointmentModality.InPerson,
            source.IsPublished,
            source.CreatedAt,
            source.Id);
        var changed = AvailabilityImportPackage.Create(
            original.PackageCode,
            original.Version,
            original.ReferenceDate,
            [changedSlot, .. original.Slots.Skip(1)]);

        await using (var second = CreateDbContext())
        {
            await Assert.ThrowsAsync<AvailabilityImportConflictException>(() =>
                CreateImporter(second).ImportAsync(changed));
        }

        await using var verify = CreateDbContext();
        Assert.Equal(original.Slots.Count, await verify.AvailabilitySlots.CountAsync());
        Assert.Equal(1L, await CountImportRecordsAsync());
    }

    [Fact]
    public async Task ConcurrentIdenticalImports_AreSerializedAndConvergeIdempotently()
    {
        await ImportDirectoryAsync();
        var package = ProductApprovedSyntheticAvailability.Create(ReferenceDate);
        await using var first = CreateDbContext();
        await using var second = CreateDbContext();

        var results = await Task.WhenAll(
            CreateImporter(first).ImportAsync(package),
            CreateImporter(second).ImportAsync(package));

        Assert.Single(results, value => value.Outcome == AvailabilityImportOutcome.Imported);
        Assert.Single(
            results,
            value => value.Outcome == AvailabilityImportOutcome.AlreadyImported);
        await using var verify = CreateDbContext();
        Assert.Equal(
            ProductApprovedSyntheticAvailability.SlotCount,
            await verify.AvailabilitySlots.CountAsync());
        Assert.Equal(1L, await CountImportRecordsAsync());
    }

    private async Task ImportDirectoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var importer = new DirectoryImporter(
            dbContext,
            new DirectoryImportPackageValidator(),
            NullLogger<DirectoryImporter>.Instance);
        await importer.ImportAsync(ProductApprovedSyntheticDirectory.Create());
    }

    private AvailabilityImporter CreateImporter(BeeexyDbContext dbContext) =>
        new(
            dbContext,
            new AvailabilityImportPackageValidator(),
            new StubClock(ImportedAt),
            NullLogger<AvailabilityImporter>.Instance);

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<long> CountImportRecordsAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM scheduling.demo_availability_imports;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE scheduling.demo_availability_imports, " +
            "scheduling.appointment_reschedule_history, scheduling.appointment_status_history, " +
            "scheduling.appointments, scheduling.availability_slots, " +
            "directory.doctor_match_rule_configurations, directory.demo_directory_imports, " +
            "directory.doctor_affiliations, directory.doctor_credentials, " +
            "directory.doctor_insurance_participations, directory.doctor_languages, " +
            "directory.doctor_specialties, directory.clinic_locations, directory.clinics, " +
            "directory.doctors, directory.insurance_plans, directory.languages, " +
            "directory.specialties, directory.doctor_match_rule_versions;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
