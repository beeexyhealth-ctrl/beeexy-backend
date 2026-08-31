using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
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
public sealed class AppointmentRequestEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private const string Endpoint = "/api/v1/appointments";
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private DateTimeOffset now;
    private FixtureGraph graph = null!;
    private BeeexyApiFactory factory = null!;
    private string accessToken = null!;
    private string secondAccessToken = null!;

    [Fact]
    public async Task FirstRequest_PersistsRequestedAppointmentAndMinimalResponse()
    {
        using var client = CreateAuthenticatedClient();
        var key = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[0], key, "  Follow-up visit  "));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = document.RootElement;
        var appointmentId = root.GetProperty("appointmentId").GetGuid();
        Assert.Equal(graph.Patient.Id.Value, root.GetProperty("patientId").GetGuid());
        Assert.Equal(graph.Slots[0].Id.Value, root.GetProperty("slotId").GetGuid());
        Assert.Equal(graph.Doctor.Id.Value, root.GetProperty("doctorId").GetGuid());
        Assert.Equal(graph.Clinic.Id.Value, root.GetProperty("clinicId").GetGuid());
        Assert.Equal(graph.Location.Id.Value, root.GetProperty("locationId").GetGuid());
        Assert.Equal("Requested", root.GetProperty("status").GetString());
        Assert.Equal("inPerson", root.GetProperty("modality").GetString());
        Assert.Equal("Follow-up visit", root.GetProperty("reason").GetString());
        Assert.Equal(
            $"/api/v1/appointments/{appointmentId:D}",
            response.Headers.Location?.ToString());
        Assert.Equal(
            ["appointmentId", "clinicId", "clinicTimeZone", "createdAt", "doctorId",
                "endsAt", "locationId", "modality", "patientId", "reason", "slotId",
                "startsAt", "status"],
            root.EnumerateObject().Select(value => value.Name).Order().ToArray());
        var serialized = root.ToString();
        Assert.DoesNotContain("fingerprint", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestingAccount", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", serialized, StringComparison.OrdinalIgnoreCase);

        await using var dbContext = CreateDbContext();
        var appointment = await dbContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == EntityId.From(appointmentId));
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        Assert.Equal("Follow-up visit", appointment.Reason?.Value);
        Assert.Equal(EntityId.From(key), appointment.IdempotencyKey);
        Assert.Matches("^[0-9a-f]{64}$", appointment.RequestFingerprint.Value);
        var history = Assert.Single(appointment.StatusHistory);
        Assert.Equal(AppointmentStatusAction.Creation, history.Action);
        Assert.Equal(AppointmentActorType.PatientAuthority, history.ActorType);
    }

    [Fact]
    public async Task ExactReplay_ReturnsOriginalWithOkAndNoDuplicateHistory()
    {
        using var client = CreateAuthenticatedClient();
        var key = Guid.NewGuid();
        var request = Request(graph.Slots[1], key, "Control");

        using var first = await client.PostAsJsonAsync(Endpoint, request);
        using var replay = await client.PostAsJsonAsync(Endpoint, request);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody.RootElement.ToString(), replayBody.RootElement.ToString());
        await using var dbContext = CreateDbContext();
        var accountId = graph.Account.Id;
        Assert.Equal(1, await dbContext.Appointments.CountAsync(value =>
            value.RequestingAccountId == accountId && value.IdempotencyKey == EntityId.From(key)));
        Assert.Equal(1, await dbContext.AppointmentStatusHistory.CountAsync(value =>
            value.AppointmentId == EntityId.From(
                firstBody.RootElement.GetProperty("appointmentId").GetGuid())));
    }

    [Fact]
    public async Task ReusedKeyWithDifferentReason_ReturnsSafeConflict()
    {
        using var client = CreateAuthenticatedClient();
        var key = Guid.NewGuid();
        using var first = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[2], key, "Original"));

        using var conflict = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[2], key, "Changed"));
        using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(
            "scheduling.idempotency_key_reused",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("Original", problem.RootElement.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Changed", problem.RootElement.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", problem.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentDifferentKeysForSameSlot_CreateExactlyOneReservation()
    {
        using var firstClient = CreateAuthenticatedClient();
        using var secondClient = CreateAuthenticatedClient();
        var slot = graph.Slots[3];

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(Endpoint, Request(slot, Guid.NewGuid())),
            secondClient.PostAsJsonAsync(Endpoint, Request(slot, Guid.NewGuid())));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(value => value.StatusCode).Order().ToArray());
        var conflict = Assert.Single(responses, value => value.StatusCode == HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal(
            "scheduling.slot_reserved",
            problem.RootElement.GetProperty("errorCode").GetString());
        await using var dbContext = CreateDbContext();
        Assert.Equal(1, await dbContext.Appointments.CountAsync(value =>
            value.AvailabilitySlotId == slot.Id &&
            (value.Status == AppointmentStatus.Requested ||
             value.Status == AppointmentStatus.Confirmed)));
    }

    [Fact]
    public async Task ConcurrentIdenticalRetries_CreateOneAndReplayOne()
    {
        using var firstClient = CreateAuthenticatedClient();
        using var secondClient = CreateAuthenticatedClient();
        var key = Guid.NewGuid();
        var request = Request(graph.Slots[4], key, "Same request");

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(Endpoint, request),
            secondClient.PostAsJsonAsync(Endpoint, request));
        using var first = responses[0];
        using var second = responses[1];
        var bodies = await Task.WhenAll(
            first.Content.ReadAsStringAsync(),
            second.Content.ReadAsStringAsync());

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Created],
            responses.Select(value => value.StatusCode).Order().ToArray());
        Assert.Equal(bodies[0], bodies[1]);
        await using var dbContext = CreateDbContext();
        Assert.Equal(1, await dbContext.Appointments.CountAsync(value =>
            value.RequestingAccountId == graph.Account.Id &&
            value.IdempotencyKey == EntityId.From(key)));
    }

    [Fact]
    public async Task MissingBearerToken_ReturnsUnauthorizedWithoutCreating()
    {
        using var client = factory.CreateApiClient();
        var before = await CountAppointmentsAsync();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[5], Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await CountAppointmentsAsync());
    }

    [Fact]
    public async Task ActiveManagerCanBookButRevokedRelationshipIsConcealed()
    {
        using var client = CreateAuthenticatedClient();

        using var managed = await client.PostAsJsonAsync(
            Endpoint,
            Request(
                graph.Slots[9],
                Guid.NewGuid(),
                patientId: graph.ManagedPatient.Id));
        using var revoked = await client.PostAsJsonAsync(
            Endpoint,
            Request(
                graph.Slots[10],
                Guid.NewGuid(),
                patientId: graph.RevokedPatient.Id));

        Assert.Equal(HttpStatusCode.Created, managed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        using var problem = JsonDocument.Parse(await revoked.Content.ReadAsStringAsync());
        Assert.Equal(
            "scheduling.appointment_target_not_found",
            problem.RootElement.GetProperty("errorCode").GetString());
        await using var dbContext = CreateDbContext();
        Assert.Equal(1, await dbContext.Appointments.CountAsync(value =>
            value.PatientProfileId == graph.ManagedPatient.Id));
        Assert.Equal(0, await dbContext.Appointments.CountAsync(value =>
            value.PatientProfileId == graph.RevokedPatient.Id));
        Assert.Equal(CareRelationshipStatus.Active,
            (await dbContext.CareRelationships.SingleAsync(value =>
                value.Id == graph.ActiveRelationship.Id)).Status);
        Assert.Equal(CareRelationshipStatus.Revoked,
            (await dbContext.CareRelationships.SingleAsync(value =>
                value.Id == graph.RevokedRelationship.Id)).Status);
    }

    [Fact]
    public async Task SlotAndSemanticValidationUseStableSafeErrors()
    {
        using var client = CreateAuthenticatedClient();

        using var missing = await client.PostAsJsonAsync(
            Endpoint,
            new
            {
                patientId = graph.Patient.Id.Value,
                slotId = Guid.NewGuid(),
                modality = "inPerson",
                idempotencyKey = Guid.NewGuid()
            });
        using var expired = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[11], Guid.NewGuid()));
        using var mismatch = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[12], Guid.NewGuid()));
        using var longReason = await client.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[13], Guid.NewGuid(), new string('x', 501), "virtual"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await AssertProblemAsync(expired, HttpStatusCode.UnprocessableEntity,
            "scheduling.slot_expired");
        await AssertProblemAsync(mismatch, HttpStatusCode.UnprocessableEntity,
            "scheduling.modality_mismatch");
        await AssertProblemAsync(longReason, HttpStatusCode.UnprocessableEntity,
            "scheduling.reason_invalid");
    }

    [Fact]
    public async Task SameIdempotencyKeyIsIndependentAcrossAccounts()
    {
        using var firstClient = CreateAuthenticatedClient();
        using var secondClient = CreateAuthenticatedClient(secondAccessToken);
        var key = Guid.NewGuid();

        using var first = await firstClient.PostAsJsonAsync(
            Endpoint,
            Request(graph.Slots[7], key));
        using var second = await secondClient.PostAsJsonAsync(
            Endpoint,
            Request(
                graph.Slots[8],
                key,
                patientId: graph.SecondPatient.Id));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await using var dbContext = CreateDbContext();
        Assert.Equal(2, await dbContext.Appointments.CountAsync(value =>
            value.IdempotencyKey == EntityId.From(key)));
    }

    [Fact]
    public async Task RequestedAppointmentImmediatelyDisappearsFromPublicDiscovery()
    {
        using var client = CreateAuthenticatedClient();
        var slot = graph.Slots[6];
        using var booking = await client.PostAsJsonAsync(
            Endpoint,
            Request(slot, Guid.NewGuid()));

        using var discovery = await client.GetAsync(
            $"/api/v1/doctors/{graph.Doctor.Id.Value:D}/slots" +
            $"?from={Uri.EscapeDataString(now.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(now.AddDays(30).ToString("O"))}");
        using var slots = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        Assert.DoesNotContain(slots.RootElement.EnumerateArray(), value =>
            value.GetProperty("slotId").GetGuid() == slot.Id.Value);
    }

    [Fact]
    public async Task OpenApi_DocumentsAuthenticatedAppointmentRequestContract()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths.GetProperty(Endpoint).GetProperty("post");

        Assert.Equal(42, paths.EnumerateObject().Count());
        Assert.True(operation.TryGetProperty("security", out _));
        Assert.Equal(
            ["200", "201", "400", "401", "404", "409", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order());
        var responseSchema = document.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("RequestAppointmentResponse")
            .GetProperty("properties");
        Assert.DoesNotContain(responseSchema.EnumerateObject(), value =>
            value.Name.Contains("fingerprint", StringComparison.OrdinalIgnoreCase) ||
            value.Name.Contains("idempotency", StringComparison.OrdinalIgnoreCase) ||
            value.Name.Contains("account", StringComparison.OrdinalIgnoreCase));
    }

    public async Task InitializeAsync()
    {
        now = DateTimeOffset.UtcNow;
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await CleanupAsync();
        graph = CreateGraph();
        dbContext.AddRange(
            graph.Account,
            graph.Patient,
            graph.Preference,
            graph.SecondAccount,
            graph.SecondPatient,
            graph.SecondPreference,
            graph.ManagedPatient,
            graph.RevokedPatient,
            graph.ActiveRelationship,
            graph.RevokedRelationship,
            graph.Clinic,
            graph.Location,
            graph.Doctor,
            graph.Affiliation);
        dbContext.AvailabilitySlots.AddRange(graph.Slots);
        await dbContext.SaveChangesAsync();

        factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new StubClock(now));
            });
        using var client = factory.CreateApiClient();
        accessToken = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(graph.Account.Id, EntityId.New(), DateTimeOffset.UtcNow)
            .Value;
        secondAccessToken = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(graph.SecondAccount.Id, EntityId.New(), DateTimeOffset.UtcNow)
            .Value;
    }

    public async Task DisposeAsync()
    {
        factory.Dispose();
        await CleanupAsync();
    }

    private HttpClient CreateAuthenticatedClient(string? token = null)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token ?? accessToken);
        return client;
    }

    private object Request(
        AvailabilitySlot slot,
        Guid key,
        string? reason = null,
        string modality = "inPerson",
        EntityId? patientId = null) => new
    {
        patientId = (patientId ?? graph.Patient.Id).Value,
        slotId = slot.Id.Value,
        modality,
        reason,
        idempotencyKey = key
    };

    private FixtureGraph CreateGraph()
    {
        var createdAt = now.AddDays(-1);
        var account = Account.Create(
            NormalizedEmail.Create($"appointment-request-{suffix}@example.test"),
            createdAt);
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-{suffix}"),
            createdAt,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("America/Lima"),
            createdAt);
        var secondAccount = Account.Create(
            NormalizedEmail.Create($"appointment-request-second-{suffix}@example.test"),
            createdAt);
        var secondPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-SECOND-{suffix}"),
            createdAt,
            secondAccount.Id);
        var secondPreference = UserPreference.Create(
            secondAccount.Id,
            UserTimeZone.Create("America/Lima"),
            createdAt);
        var managedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-MANAGED-{suffix}"),
            createdAt,
            accountId: null);
        var revokedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-REVOKED-{suffix}"),
            createdAt,
            accountId: null);
        var activeRelationship = CareRelationship.Create(
            patient.Id,
            managedPatient.Id,
            CareRelationshipType.Caregiver,
            account.Id,
            AuthorizationAttestation.Create("phase-8.3-test", createdAt),
            createdAt);
        var revokedRelationship = CareRelationship.Create(
            patient.Id,
            revokedPatient.Id,
            CareRelationshipType.Caregiver,
            account.Id,
            AuthorizationAttestation.Create("phase-8.3-test", createdAt),
            createdAt);
        revokedRelationship.Revoke(account.Id, createdAt.AddMinutes(1));
        var clinic = Clinic.Create(
            DirectoryCode.Create($"appointment-request-clinic-{suffix}"),
            DirectoryName.Create("Synthetic appointment request clinic"),
            true,
            createdAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Synthetic appointment request location"),
            "Lima",
            "Lima",
            "PE",
            IanaTimeZone.Create("America/Lima"),
            true,
            createdAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"appointment-request-doctor-{suffix}"),
            DirectoryName.Create("Synthetic appointment request doctor"),
            true,
            createdAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id,
            clinic.Id,
            location.Id,
            true,
            createdAt);
        var slots = Enumerable.Range(1, 11)
            .Select(day => AvailabilitySlot.Create(
                doctor.Id,
                clinic.Id,
                location.Id,
                now.AddDays(day),
                now.AddDays(day).AddMinutes(30),
                IanaTimeZone.Create("America/Lima"),
                AppointmentModality.InPerson,
                true,
                createdAt))
            .Concat([
                AvailabilitySlot.Create(
                    doctor.Id, clinic.Id, location.Id, now, now.AddMinutes(30),
                    IanaTimeZone.Create("America/Lima"), AppointmentModality.InPerson,
                    true, createdAt),
                AvailabilitySlot.Create(
                    doctor.Id, clinic.Id, location.Id, now.AddDays(12),
                    now.AddDays(12).AddMinutes(30), IanaTimeZone.Create("America/Lima"),
                    AppointmentModality.Virtual, true, createdAt),
                AvailabilitySlot.Create(
                    doctor.Id, clinic.Id, location.Id, now.AddDays(13),
                    now.AddDays(13).AddMinutes(30), IanaTimeZone.Create("America/Lima"),
                    AppointmentModality.Virtual, true, createdAt)
            ])
            .ToArray();
        return new FixtureGraph(
            account,
            patient,
            preference,
            secondAccount,
            secondPatient,
            secondPreference,
            managedPatient,
            revokedPatient,
            activeRelationship,
            revokedRelationship,
            clinic,
            location,
            doctor,
            affiliation,
            slots);
    }

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<int> CountAppointmentsAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Appointments.CountAsync(value =>
            value.RequestingAccountId == graph.Account.Id);
    }

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM scheduling.appointment_status_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-request-%@example.test')); " +
            "DELETE FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-request-%@example.test'); " +
            "DELETE FROM patients.care_relationships WHERE created_by_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-request-%@example.test'); " +
            "DELETE FROM scheduling.availability_slots WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-request-doctor-%'); " +
            "DELETE FROM directory.doctor_affiliations WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-request-doctor-%'); " +
            "DELETE FROM directory.doctors WHERE code LIKE 'appointment-request-doctor-%'; " +
            "DELETE FROM directory.clinic_locations WHERE clinic_id IN " +
            "(SELECT id FROM directory.clinics WHERE code LIKE 'appointment-request-clinic-%'); " +
            "DELETE FROM directory.clinics WHERE code LIKE 'appointment-request-clinic-%'; " +
            "DELETE FROM patients.user_preferences WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-request-%@example.test'); " +
            "DELETE FROM patients.patient_profiles WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-request-%@example.test'); " +
            "DELETE FROM patients.patient_profiles WHERE beeexy_id LIKE 'BXY-APPOINTMENT-MANAGED-%' " +
            "OR beeexy_id LIKE 'BXY-APPOINTMENT-REVOKED-%'; " +
            "DELETE FROM identity.accounts WHERE normalized_email LIKE 'appointment-request-%@example.test';";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed record FixtureGraph(
        Account Account,
        PatientProfile Patient,
        UserPreference Preference,
        Account SecondAccount,
        PatientProfile SecondPatient,
        UserPreference SecondPreference,
        PatientProfile ManagedPatient,
        PatientProfile RevokedPatient,
        CareRelationship ActiveRelationship,
        CareRelationship RevokedRelationship,
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        AvailabilitySlot[] Slots);

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(errorCode, problem.RootElement.GetProperty("errorCode").GetString());
        var serialized = problem.RootElement.ToString();
        Assert.DoesNotContain("Postgres", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("constraint", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
