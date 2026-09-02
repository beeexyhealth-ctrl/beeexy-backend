using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase8Acceptance")]
public sealed class AvailabilitySlotEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 3, 8, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = Now.AddDays(-2);
    private readonly string _suffix = Guid.NewGuid().ToString("N");
    private FixtureGraph _graph = null!;

    [Fact]
    public async Task AnonymousDefaultRange_ReturnsOnlyPublicFutureUnreservedSlotsInStableOrder()
    {
        using var context = CreateApiContext();

        using var response = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(
            _graph.DefaultAvailableSlots
                .OrderBy(value => value.StartsAt)
                .ThenBy(value => value.Id.Value)
                .Select(value => value.Id.Value),
            items.Select(value => value.GetProperty("slotId").GetGuid()));
        Assert.All(items, item => Assert.Equal(
            ["clinicId", "clinicTimeZone", "doctorId", "endsAt", "locationId", "modality", "slotId", "startsAt"],
            item.EnumerateObject().Select(value => value.Name).Order().ToArray()));
        var serialized = document.RootElement.ToString();
        foreach (var forbidden in new[]
        {
            "appointment", "patient", "reason", "requestingAccount", "idempotency",
            "isPublished", "status", "version"
        })
        {
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExplicitHalfOpenRange_AppliesStartBoundaryAndNinetyDayMaximum()
    {
        using var context = CreateApiContext();
        var from = Now.AddDays(39);
        var to = Now.AddDays(40).AddMinutes(30);
        var endpoint = $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots" +
            $"?from={Uri.EscapeDataString(from.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(to.ToString("O"))}";

        using var response = await context.Client.GetAsync(endpoint);
        var items = await response.Content.ReadFromJsonAsync<SlotResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(items);
        Assert.Equal([_graph.DayFortySlot.Id.Value], items.Select(value => value.SlotId));

        using var maximumResponse = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots" +
            $"?from={Uri.EscapeDataString(Now.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(Now.AddDays(90).ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, maximumResponse.StatusCode);
    }

    [Theory]
    [InlineData("2026-03-09T00:00:00Z", "2026-03-09T00:00:00Z")]
    [InlineData("2026-03-10T00:00:00Z", "2026-03-09T00:00:00Z")]
    [InlineData("2026-03-08T05:00:00Z", "2026-06-06T05:00:00.0000001Z")]
    public async Task InvalidRange_ReturnsStableUnprocessableProblem(
        string from,
        string to)
    {
        using var context = CreateApiContext();

        using var response = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots?from={from}&to={to}");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "availability.range_invalid",
            problem.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UnknownAndUnpublishedDoctors_AreConcealedWithIdenticalNotFoundSemantics()
    {
        using var context = CreateApiContext();

        using var hiddenResponse = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.HiddenDoctor.Id.Value:D}/slots");
        using var missingResponse = await context.Client.GetAsync(
            $"/api/v1/doctors/{Guid.NewGuid():D}/slots");
        using var hidden = JsonDocument.Parse(await hiddenResponse.Content.ReadAsStringAsync());
        using var missing = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(
            hidden.RootElement.GetProperty("title").GetString(),
            missing.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            hidden.RootElement.GetProperty("detail").GetString(),
            missing.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PublishedDoctorWithoutAvailability_ReturnsEmptyCollection()
    {
        using var context = CreateApiContext();

        using var response = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.EmptyDoctor.Id.Value:D}/slots");
        var items = await response.Content.ReadFromJsonAsync<SlotResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ReservingStatusesHideSlotsButCancelledAndRejectedHistoryReleaseThem()
    {
        using var context = CreateApiContext();

        using var response = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots");
        var items = await response.Content.ReadFromJsonAsync<SlotResponse[]>();

        Assert.NotNull(items);
        Assert.DoesNotContain(items, value => value.SlotId == _graph.RequestedSlot.Id.Value);
        Assert.DoesNotContain(items, value => value.SlotId == _graph.ConfirmedSlot.Id.Value);
        Assert.Contains(items, value => value.SlotId == _graph.CancelledSlot.Id.Value);
        Assert.Contains(items, value => value.SlotId == _graph.RejectedSlot.Id.Value);
    }

    [Fact]
    public async Task NewYorkDstProjection_PreservesUtcInstantsAndClinicLocalInterpretation()
    {
        using var context = CreateApiContext();

        using var response = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots");
        var items = await response.Content.ReadFromJsonAsync<SlotResponse[]>();
        var before = Assert.Single(items!, value => value.SlotId == _graph.BeforeDstSlot.Id.Value);
        var after = Assert.Single(items!, value => value.SlotId == _graph.AfterDstSlot.Id.Value);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero), before.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), after.StartsAt);
        Assert.Equal(new DateTime(2026, 3, 8, 1, 30, 0),
            TimeZoneInfo.ConvertTime(before.StartsAt, zone).DateTime);
        Assert.Equal(new DateTime(2026, 3, 8, 3, 30, 0),
            TimeZoneInfo.ConvertTime(after.StartsAt, zone).DateTime);
        Assert.Equal("America/New_York", before.ClinicTimeZone);
        Assert.Equal("America/New_York", after.ClinicTimeZone);
    }

    [Fact]
    public async Task NewYorkFallBack_PreservesBothAmbiguousLocalInstants()
    {
        using var context = CreateApiContext();
        var from = new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

        using var response = await context.Client.GetAsync(
            $"/api/v1/doctors/{_graph.Doctor.Id.Value:D}/slots" +
            $"?from={Uri.EscapeDataString(from.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(to.ToString("O"))}");
        var items = await response.Content.ReadFromJsonAsync<SlotResponse[]>();
        var first = Assert.Single(
            items!, value => value.SlotId == _graph.FallBackFirstSlot.Id.Value);
        var second = Assert.Single(
            items!, value => value.SlotId == _graph.FallBackSecondSlot.Id.Value);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var firstLocal = TimeZoneInfo.ConvertTime(first.StartsAt, zone);
        var secondLocal = TimeZoneInfo.ConvertTime(second.StartsAt, zone);

        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), firstLocal.DateTime);
        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), secondLocal.DateTime);
        Assert.Equal(TimeSpan.FromHours(-4), firstLocal.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), secondLocal.Offset);
        Assert.Equal("America/New_York", first.ClinicTimeZone);
        Assert.Equal("America/New_York", second.ClinicTimeZone);
    }

    [Fact]
    public async Task OpenApi_DocumentsSingleAnonymousAvailabilityPathAndStableContract()
    {
        using var context = CreateApiContext();

        using var response = await context.Client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths
            .GetProperty("/api/v1/doctors/{doctorId}/slots")
            .GetProperty("get");

        Assert.Equal(51, paths.EnumerateObject().Count());
        Assert.False(operation.TryGetProperty("security", out _));
        Assert.Equal(
            ["doctorId", "from", "to"],
            operation.GetProperty("parameters").EnumerateArray()
                .Select(value => value.GetProperty("name").GetString()));
        Assert.Equal(
            ["200", "404", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order());
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("AvailabilitySlotResponse").GetProperty("properties");
        Assert.Equal(
            ["clinicId", "clinicTimeZone", "doctorId", "endsAt", "locationId", "modality", "slotId", "startsAt"],
            schema.EnumerateObject().Select(value => value.Name).Order());
    }

    private ApiContext CreateApiContext()
    {
        var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new StubClock(Now));
            });
        return new ApiContext(factory, factory.CreateApiClient());
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await CleanupAsync();
        _graph = CreateGraph();
        dbContext.AddRange(
            _graph.Account,
            _graph.Patient,
            _graph.Clinic,
            _graph.Location,
            _graph.HiddenLocation,
            _graph.Doctor,
            _graph.HiddenDoctor,
            _graph.EmptyDoctor,
            _graph.Affiliation,
            _graph.HiddenLocationAffiliation,
            _graph.HiddenDoctorAffiliation,
            _graph.EmptyDoctorAffiliation);
        dbContext.AvailabilitySlots.AddRange(_graph.AllSlots);
        dbContext.Appointments.AddRange(_graph.Appointments);
        await dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => CleanupAsync();

    private FixtureGraph CreateGraph()
    {
        var account = Account.Create(
            NormalizedEmail.Create($"availability-{_suffix}@example.test"),
            CreatedAt);
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-AVAIL-{_suffix}"),
            CreatedAt,
            account.Id);
        var clinic = Clinic.Create(
            DirectoryCode.Create($"availability-clinic-{_suffix}"),
            DirectoryName.Create("Synthetic availability clinic"),
            true,
            CreatedAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Synthetic New York availability location"),
            "New York",
            "NY",
            "US",
            IanaTimeZone.Create("America/New_York"),
            true,
            CreatedAt);
        var hiddenLocation = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Hidden synthetic availability location"),
            "New York",
            "NY",
            "US",
            IanaTimeZone.Create("America/New_York"),
            false,
            CreatedAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"availability-doctor-{_suffix}"),
            DirectoryName.Create("Synthetic availability doctor"),
            true,
            CreatedAt);
        var hiddenDoctor = Doctor.Create(
            DirectoryCode.Create($"availability-hidden-doctor-{_suffix}"),
            DirectoryName.Create("Hidden synthetic availability doctor"),
            false,
            CreatedAt);
        var emptyDoctor = Doctor.Create(
            DirectoryCode.Create($"availability-empty-doctor-{_suffix}"),
            DirectoryName.Create("Synthetic doctor without availability"),
            true,
            CreatedAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id, clinic.Id, location.Id, true, CreatedAt);
        var hiddenLocationAffiliation = DoctorAffiliation.Create(
            doctor.Id, clinic.Id, hiddenLocation.Id, true, CreatedAt);
        var hiddenDoctorAffiliation = DoctorAffiliation.Create(
            hiddenDoctor.Id, clinic.Id, location.Id, true, CreatedAt);
        var emptyDoctorAffiliation = DoctorAffiliation.Create(
            emptyDoctor.Id, clinic.Id, location.Id, true, CreatedAt);

        AvailabilitySlot Slot(DateTimeOffset startsAt, bool published = true,
            EntityId? doctorId = null, EntityId? locationId = null, EntityId? id = null) =>
            AvailabilitySlot.Create(
                doctorId ?? doctor.Id,
                clinic.Id,
                locationId ?? location.Id,
                startsAt,
                startsAt.AddMinutes(30),
                IanaTimeZone.Create("America/New_York"),
                AppointmentModality.InPerson,
                published,
                CreatedAt,
                id);

        var past = Slot(Now);
        var beforeDst = Slot(new(2026, 3, 8, 6, 30, 0, TimeSpan.Zero));
        var afterDst = Slot(new(2026, 3, 8, 7, 30, 0, TimeSpan.Zero));
        var fallBackFirst = Slot(new(2026, 11, 1, 5, 30, 0, TimeSpan.Zero));
        var fallBackSecond = Slot(new(2026, 11, 1, 6, 30, 0, TimeSpan.Zero));
        var tieSecond = Slot(Now.AddDays(1), id: EntityId.From(
            Guid.Parse("82000000-0000-4000-8000-000000000002")));
        var tieFirst = Slot(Now.AddDays(1), id: EntityId.From(
            Guid.Parse("82000000-0000-4000-8000-000000000001")));
        var unpublished = Slot(Now.AddDays(2), published: false);
        var hiddenRelation = Slot(Now.AddDays(2), locationId: hiddenLocation.Id);
        var requested = Slot(Now.AddDays(3));
        var confirmed = Slot(Now.AddDays(4));
        var cancelled = Slot(Now.AddDays(5));
        var rejected = Slot(Now.AddDays(6));
        var dayForty = Slot(Now.AddDays(40));
        var hiddenDoctorSlot = Slot(Now.AddDays(1), doctorId: hiddenDoctor.Id);

        Appointment AppointmentFor(AvailabilitySlot slot) => Appointment.Create(
            patient.Id,
            slot,
            account.Id,
            slot.Modality,
            null,
            EntityId.New(),
            AppointmentRequestFingerprint.Create(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            CreatedAt);

        var requestedAppointment = AppointmentFor(requested);
        var confirmedAppointment = AppointmentFor(confirmed);
        confirmedAppointment.Confirm(account.Id, CreatedAt.AddMinutes(1));
        var cancelledAppointment = AppointmentFor(cancelled);
        cancelledAppointment.Cancel(account.Id, CreatedAt.AddMinutes(1));
        var rejectedAppointment = AppointmentFor(rejected);
        rejectedAppointment.Reject(account.Id, CreatedAt.AddMinutes(1));

        return new FixtureGraph(
            account,
            patient,
            clinic,
            location,
            hiddenLocation,
            doctor,
            hiddenDoctor,
            emptyDoctor,
            affiliation,
            hiddenLocationAffiliation,
            hiddenDoctorAffiliation,
            emptyDoctorAffiliation,
            [past, beforeDst, afterDst, fallBackFirst, fallBackSecond, tieSecond, tieFirst,
                unpublished, hiddenRelation, requested, confirmed, cancelled, rejected,
                dayForty, hiddenDoctorSlot],
            [beforeDst, afterDst, tieFirst, tieSecond, cancelled, rejected],
            requested,
            confirmed,
            cancelled,
            rejected,
            beforeDst,
            afterDst,
            fallBackFirst,
            fallBackSecond,
            dayForty,
            [requestedAppointment, confirmedAppointment, cancelledAppointment,
                rejectedAppointment]);
    }

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM scheduling.appointment_status_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'availability-%@example.test')); " +
            "DELETE FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'availability-%@example.test'); " +
            "DELETE FROM scheduling.availability_slots WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'availability-%'); " +
            "DELETE FROM directory.doctor_affiliations WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'availability-%'); " +
            "DELETE FROM directory.doctors WHERE code LIKE 'availability-%'; " +
            "DELETE FROM directory.clinic_locations WHERE clinic_id IN " +
            "(SELECT id FROM directory.clinics WHERE code LIKE 'availability-clinic-%'); " +
            "DELETE FROM directory.clinics WHERE code LIKE 'availability-clinic-%'; " +
            "DELETE FROM patients.patient_profiles WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'availability-%@example.test'); " +
            "DELETE FROM identity.accounts WHERE normalized_email LIKE 'availability-%@example.test';";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed record SlotResponse(
        Guid SlotId,
        Guid DoctorId,
        Guid ClinicId,
        Guid LocationId,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        string ClinicTimeZone,
        string Modality);

    private sealed record FixtureGraph(
        Account Account,
        PatientProfile Patient,
        Clinic Clinic,
        ClinicLocation Location,
        ClinicLocation HiddenLocation,
        Doctor Doctor,
        Doctor HiddenDoctor,
        Doctor EmptyDoctor,
        DoctorAffiliation Affiliation,
        DoctorAffiliation HiddenLocationAffiliation,
        DoctorAffiliation HiddenDoctorAffiliation,
        DoctorAffiliation EmptyDoctorAffiliation,
        AvailabilitySlot[] AllSlots,
        AvailabilitySlot[] DefaultAvailableSlots,
        AvailabilitySlot RequestedSlot,
        AvailabilitySlot ConfirmedSlot,
        AvailabilitySlot CancelledSlot,
        AvailabilitySlot RejectedSlot,
        AvailabilitySlot BeforeDstSlot,
        AvailabilitySlot AfterDstSlot,
        AvailabilitySlot FallBackFirstSlot,
        AvailabilitySlot FallBackSecondSlot,
        AvailabilitySlot DayFortySlot,
        Appointment[] Appointments);

    private sealed record ApiContext(
        BeeexyApiFactory Factory,
        HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
