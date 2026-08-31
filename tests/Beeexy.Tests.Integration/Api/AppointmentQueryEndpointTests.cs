using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
using Npgsql;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class AppointmentQueryEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset timeline =
        new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
    private FixtureGraph graph = null!;
    private BeeexyApiFactory factory = null!;
    private string ownerToken = null!;
    private string unrelatedToken = null!;

    [Fact]
    public async Task OwnerList_ReturnsOnlyCurrentlyAccessibleSafeSummariesInStableOrder()
    {
        using var client = CreateClient(ownerToken);

        using var response = await client.GetAsync("/api/v1/appointments?pageSize=100");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(5, items.Length);
        Assert.DoesNotContain(items, item =>
            item.GetProperty("patientId").GetGuid() == graph.RevokedPatient.Id.Value ||
            item.GetProperty("patientId").GetGuid() == graph.UnrelatedPatient.Id.Value);
        Assert.Equal(
            items.OrderBy(item => item.GetProperty("startsAt").GetDateTimeOffset())
                .Select(item => item.GetProperty("appointmentId").GetGuid()),
            items.Select(item => item.GetProperty("appointmentId").GetGuid()));
        Assert.All(items, item => Assert.Equal(
            ["appointmentId", "clinicId", "clinicTimeZone", "createdAt", "doctorId",
                "endsAt", "locationId", "modality", "patientId", "slotId", "startsAt",
                "status"],
            item.EnumerateObject().Select(value => value.Name).Order().ToArray()));
        Assert.Null(document.RootElement.GetProperty("nextCursor").GetString());
        AssertSafe(document.RootElement.ToString());
        Assert.DoesNotContain("Private reason", document.RootElement.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatientStatusAndHalfOpenTimeFiltersWorkAndEmptyIsSuccessful()
    {
        using var client = CreateClient(ownerToken);

        using var patient = await client.GetAsync(
            $"/api/v1/appointments?patientId={graph.ManagedPatient.Id.Value:D}");
        using var status = await client.GetAsync(
            "/api/v1/appointments?status=Confirmed");
        using var range = await client.GetAsync(
            "/api/v1/appointments" +
            $"?from={Uri.EscapeDataString(timeline.AddDays(2).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(timeline.AddDays(5).ToString("O"))}");
        using var empty = await client.GetAsync(
            "/api/v1/appointments?status=Completed");

        Assert.Single(await ItemsAsync(patient), item =>
            item.GetProperty("patientId").GetGuid() == graph.ManagedPatient.Id.Value);
        Assert.Single(await ItemsAsync(status), item =>
            item.GetProperty("status").GetString() == "Confirmed");
        Assert.Equal(
            ["Confirmed", "Cancelled"],
            (await ItemsAsync(range)).Select(item =>
                item.GetProperty("status").GetString()).ToArray());
        Assert.Empty(await ItemsAsync(empty));
    }

    [Fact]
    public async Task InvalidFiltersRangesAndPatientAuthorityReturnStableProblems()
    {
        using var client = CreateClient(ownerToken);

        using var status = await client.GetAsync(
            "/api/v1/appointments?status=requested");
        using var range = await client.GetAsync(
            "/api/v1/appointments" +
            $"?from={Uri.EscapeDataString(timeline.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(timeline.ToString("O"))}");
        using var page = await client.GetAsync("/api/v1/appointments?pageSize=101");
        using var patient = await client.GetAsync(
            $"/api/v1/appointments?patientId={graph.RevokedPatient.Id.Value:D}");

        await AssertProblemAsync(status, HttpStatusCode.UnprocessableEntity,
            "scheduling.appointment_status_invalid");
        await AssertProblemAsync(range, HttpStatusCode.UnprocessableEntity,
            "scheduling.appointment_range_invalid");
        await AssertProblemAsync(page, HttpStatusCode.UnprocessableEntity,
            "scheduling.appointment_page_size_invalid");
        await AssertProblemAsync(patient, HttpStatusCode.NotFound,
            "scheduling.appointment_target_not_found");
    }

    [Fact]
    public async Task OpaquePaginationAcrossTiedStartsHasNoDuplicatesOrOmissions()
    {
        using var client = CreateClient(ownerToken);
        var collected = new List<Guid>();
        string? cursor = null;
        do
        {
            var endpoint = "/api/v1/appointments?pageSize=2" +
                (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var response = await client.GetAsync(endpoint);
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            collected.AddRange(page.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("appointmentId").GetGuid()));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        Assert.Equal(5, collected.Count);
        Assert.Equal(5, collected.Distinct().Count());
        Assert.Equal(
            graph.AccessibleAppointments.Select(value => value.Id.Value).Order(),
            collected.Order());

        using var first = await client.GetAsync("/api/v1/appointments?pageSize=1");
        using var firstPage = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var boundCursor = firstPage.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(boundCursor);
        Assert.DoesNotContain(
            firstPage.RootElement.GetProperty("items")[0]
                .GetProperty("appointmentId").GetGuid().ToString("D"),
            boundCursor,
            StringComparison.OrdinalIgnoreCase);
        using var malformed = await client.GetAsync(
            "/api/v1/appointments?cursor=not.a.cursor");
        using var mismatch = await client.GetAsync(
            $"/api/v1/appointments?pageSize=1&status=Requested" +
            $"&cursor={Uri.EscapeDataString(boundCursor)}");
        await AssertProblemAsync(malformed, HttpStatusCode.UnprocessableEntity,
            "scheduling.appointment_cursor_invalid");
        await AssertProblemAsync(mismatch, HttpStatusCode.UnprocessableEntity,
            "scheduling.appointment_cursor_invalid");
    }

    [Fact]
    public async Task DetailReturnsReasonAndCompleteOrderedSafeSeparateAuditStreams()
    {
        using var client = CreateClient(ownerToken);

        using var response = await client.GetAsync(
            $"/api/v1/appointments/{graph.ConfirmedAppointment.Id.Value:D}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("Confirmed", root.GetProperty("status").GetString());
        Assert.Equal("Private reason", root.GetProperty("reason").GetString());
        Assert.Equal("America/Lima", root.GetProperty("clinicTimeZone").GetString());
        var history = root.GetProperty("statusHistory").EnumerateArray().ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal([1L, 2L], history.Select(value =>
            value.GetProperty("sequence").GetInt64()));
        Assert.Equal("creation", history[0].GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null,
            history[0].GetProperty("previousStatus").ValueKind);
        Assert.Equal("Requested", history[0].GetProperty("newStatus").GetString());
        Assert.Equal("patientAuthority", history[0].GetProperty("actorType").GetString());
        Assert.Equal("confirmation", history[1].GetProperty("action").GetString());
        Assert.Equal("appointmentScheduler", history[1].GetProperty("actorType").GetString());
        var reschedules = root.GetProperty("rescheduleHistory").EnumerateArray().ToArray();
        var reschedule = Assert.Single(reschedules);
        Assert.Equal(graph.RescheduleHistory.PreviousSlotId.Value,
            reschedule.GetProperty("previousSlotId").GetGuid());
        Assert.Equal(graph.RescheduleHistory.NewSlotId.Value,
            reschedule.GetProperty("newSlotId").GetGuid());
        Assert.DoesNotContain("actorAccount", root.ToString(), StringComparison.OrdinalIgnoreCase);
        AssertSafe(root.ToString());
    }

    [Fact]
    public async Task ActiveManagerCanReadDetailWhileRevokedAndUnrelatedCallersSeeConcealedNotFound()
    {
        using var owner = CreateClient(ownerToken);
        using var unrelated = CreateClient(unrelatedToken);

        using var managed = await owner.GetAsync(
            $"/api/v1/appointments/{graph.ManagedAppointment.Id.Value:D}");
        using var revoked = await owner.GetAsync(
            $"/api/v1/appointments/{graph.RevokedAppointment.Id.Value:D}");
        using var denied = await unrelated.GetAsync(
            $"/api/v1/appointments/{graph.ConfirmedAppointment.Id.Value:D}");
        using var missing = await owner.GetAsync(
            $"/api/v1/appointments/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.OK, managed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var revokedProblem = JsonDocument.Parse(
            await revoked.Content.ReadAsStringAsync());
        using var missingProblem = JsonDocument.Parse(
            await missing.Content.ReadAsStringAsync());
        Assert.Equal(
            revokedProblem.RootElement.GetProperty("title").GetString(),
            missingProblem.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            revokedProblem.RootElement.GetProperty("detail").GetString(),
            missingProblem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            revokedProblem.RootElement.GetProperty("errorCode").GetString(),
            missingProblem.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AuthorizedHistoricalDetailSurvivesDirectoryUnpublication()
    {
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE directory.doctors SET is_published = false
                WHERE id = {graph.Doctor.Id.Value};
                UPDATE directory.clinics SET is_published = false
                WHERE id = {graph.Clinic.Id.Value};
                UPDATE directory.clinic_locations SET is_published = false
                WHERE id = {graph.Location.Id.Value};
                UPDATE directory.doctor_affiliations SET is_published = false
                WHERE id = {graph.Affiliation.Id.Value};
                """);
        }
        using var client = CreateClient(ownerToken);

        using var response = await client.GetAsync(
            $"/api/v1/appointments/{graph.ConfirmedAppointment.Id.Value:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BothEndpointsRequireBearerAuthentication()
    {
        using var client = factory.CreateApiClient();

        using var list = await client.GetAsync("/api/v1/appointments");
        using var detail = await client.GetAsync(
            $"/api/v1/appointments/{graph.ConfirmedAppointment.Id.Value:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentsBothAuthenticatedReadOperationsAndFortyOnePaths()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty("/api/v1/appointments");
        var detail = paths.GetProperty("/api/v1/appointments/{id}").GetProperty("get");

        Assert.Equal(41, paths.EnumerateObject().Count());
        Assert.True(collection.TryGetProperty("post", out _));
        Assert.True(collection.TryGetProperty("get", out var list));
        Assert.True(list.TryGetProperty("security", out _));
        Assert.True(detail.TryGetProperty("security", out _));
        Assert.Equal(
            ["cursor", "from", "pageSize", "patientId", "status", "to"],
            list.GetProperty("parameters").EnumerateArray()
                .Select(value => value.GetProperty("name").GetString()).Order());
        Assert.Equal(["200", "400", "401", "404", "422", "500"],
            list.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order());
        Assert.Equal(["200", "401", "404", "500"],
            detail.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order());
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await CleanupAsync();
        graph = CreateGraph();
        dbContext.AddRange(
            graph.OwnerAccount,
            graph.OwnerPatient,
            graph.OwnerPreference,
            graph.UnrelatedAccount,
            graph.UnrelatedPatient,
            graph.UnrelatedPreference,
            graph.ManagedPatient,
            graph.RevokedPatient,
            graph.ActiveRelationship,
            graph.RevokedRelationship,
            graph.Clinic,
            graph.Location,
            graph.Doctor,
            graph.Affiliation);
        dbContext.AvailabilitySlots.AddRange(graph.Slots);
        dbContext.Appointments.AddRange(graph.AllAppointments);
        dbContext.AppointmentRescheduleHistory.Add(graph.RescheduleHistory);
        await dbContext.SaveChangesAsync();

        factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        ownerToken = issuer.Issue(
            graph.OwnerAccount.Id,
            EntityId.New(),
            DateTimeOffset.UtcNow).Value;
        unrelatedToken = issuer.Issue(
            graph.UnrelatedAccount.Id,
            EntityId.New(),
            DateTimeOffset.UtcNow).Value;
    }

    public async Task DisposeAsync()
    {
        factory.Dispose();
        await CleanupAsync();
    }

    private FixtureGraph CreateGraph()
    {
        var createdAt = timeline.AddDays(-10);
        var owner = Account.Create(
            NormalizedEmail.Create($"appointment-query-owner-{suffix}@example.test"),
            createdAt);
        var ownerPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-QUERY-OWNER-{suffix}"),
            createdAt,
            owner.Id);
        var ownerPreference = UserPreference.Create(
            owner.Id,
            UserTimeZone.Create("America/Lima"),
            createdAt);
        var unrelated = Account.Create(
            NormalizedEmail.Create($"appointment-query-unrelated-{suffix}@example.test"),
            createdAt);
        var unrelatedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-QUERY-UNRELATED-{suffix}"),
            createdAt,
            unrelated.Id);
        var unrelatedPreference = UserPreference.Create(
            unrelated.Id,
            UserTimeZone.Create("America/Lima"),
            createdAt);
        var managedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-QUERY-MANAGED-{suffix}"),
            createdAt,
            accountId: null);
        var revokedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APPOINTMENT-QUERY-REVOKED-{suffix}"),
            createdAt,
            accountId: null);
        var active = CareRelationship.Create(
            ownerPatient.Id,
            managedPatient.Id,
            CareRelationshipType.Caregiver,
            owner.Id,
            AuthorizationAttestation.Create("phase-8.4-test", createdAt),
            createdAt);
        var revoked = CareRelationship.Create(
            ownerPatient.Id,
            revokedPatient.Id,
            CareRelationshipType.Caregiver,
            owner.Id,
            AuthorizationAttestation.Create("phase-8.4-test", createdAt),
            createdAt);
        revoked.Revoke(owner.Id, createdAt.AddMinutes(1));
        var clinic = Clinic.Create(
            DirectoryCode.Create($"appointment-query-clinic-{suffix}"),
            DirectoryName.Create("Synthetic query clinic"),
            true,
            createdAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Synthetic query location"),
            "Lima",
            "Lima",
            "PE",
            IanaTimeZone.Create("America/Lima"),
            true,
            createdAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"appointment-query-doctor-{suffix}"),
            DirectoryName.Create("Synthetic query doctor"),
            true,
            createdAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id, clinic.Id, location.Id, true, createdAt);
        var slots = Enumerable.Range(1, 9).Select(index =>
        {
            var start = index == 2 ? timeline.AddDays(1) : timeline.AddDays(index);
            return AvailabilitySlot.Create(
                doctor.Id,
                clinic.Id,
                location.Id,
                start,
                start.AddMinutes(30),
                IanaTimeZone.Create("America/Lima"),
                AppointmentModality.InPerson,
                true,
                createdAt);
        }).ToArray();

        Appointment Create(PatientProfile patient, AvailabilitySlot slot, string? reason = null) =>
            Appointment.Create(
                patient.Id,
                slot,
                patient.AccountId ?? owner.Id,
                AppointmentModality.InPerson,
                reason is null ? null : AppointmentReason.Create(reason),
                EntityId.New(),
                AppointmentRequestFingerprint.Create(
                    Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                createdAt);

        var requested = Create(ownerPatient, slots[0]);
        var managed = Create(managedPatient, slots[1]);
        var confirmed = Create(ownerPatient, slots[2], "Private reason");
        confirmed.Confirm(owner.Id, createdAt.AddMinutes(2));
        var cancelled = Create(ownerPatient, slots[3]);
        cancelled.Cancel(owner.Id, createdAt.AddMinutes(3));
        var rejected = Create(ownerPatient, slots[4]);
        rejected.Reject(owner.Id, createdAt.AddMinutes(4));
        var revokedAppointment = Create(revokedPatient, slots[5]);
        var unrelatedAppointment = Create(unrelatedPatient, slots[6]);
        var reschedule = AppointmentRescheduleHistory.Create(
            confirmed.Id,
            slots[7].Id,
            confirmed.AvailabilitySlotId,
            owner.Id,
            createdAt.AddMinutes(5));
        return new FixtureGraph(
            owner,
            ownerPatient,
            ownerPreference,
            unrelated,
            unrelatedPatient,
            unrelatedPreference,
            managedPatient,
            revokedPatient,
            active,
            revoked,
            clinic,
            location,
            doctor,
            affiliation,
            slots,
            [requested, managed, confirmed, cancelled, rejected],
            [requested, managed, confirmed, cancelled, rejected,
                revokedAppointment, unrelatedAppointment],
            confirmed,
            managed,
            revokedAppointment,
            reschedule);
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        return client;
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM scheduling.appointment_reschedule_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-query-%@example.test')); " +
            "DELETE FROM scheduling.appointment_status_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-query-%@example.test')); " +
            "DELETE FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-query-%@example.test'); " +
            "DELETE FROM patients.care_relationships WHERE created_by_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-query-%@example.test'); " +
            "DELETE FROM scheduling.availability_slots WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-query-doctor-%'); " +
            "DELETE FROM directory.doctor_affiliations WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-query-doctor-%'); " +
            "DELETE FROM directory.doctors WHERE code LIKE 'appointment-query-doctor-%'; " +
            "DELETE FROM directory.clinic_locations WHERE clinic_id IN " +
            "(SELECT id FROM directory.clinics WHERE code LIKE 'appointment-query-clinic-%'); " +
            "DELETE FROM directory.clinics WHERE code LIKE 'appointment-query-clinic-%'; " +
            "DELETE FROM patients.user_preferences WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-query-%@example.test'); " +
            "DELETE FROM patients.patient_profiles WHERE beeexy_id LIKE 'BXY-APPOINTMENT-QUERY-%'; " +
            "DELETE FROM identity.accounts WHERE normalized_email LIKE 'appointment-query-%@example.test';";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement[]> ItemsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.Clone()).ToArray();
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(errorCode, problem.RootElement.GetProperty("errorCode").GetString());
        AssertSafe(problem.RootElement.ToString());
    }

    private static void AssertSafe(string value)
    {
        foreach (var forbidden in new[]
        {
            "fingerprint", "idempotency", "requestingAccount", "version",
            "preTriage", "clinicalHistory", "fhir", "diagnosis", "urgency",
            "Postgres", "constraint", "stack"
        })
        {
            Assert.DoesNotContain(forbidden, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record FixtureGraph(
        Account OwnerAccount,
        PatientProfile OwnerPatient,
        UserPreference OwnerPreference,
        Account UnrelatedAccount,
        PatientProfile UnrelatedPatient,
        UserPreference UnrelatedPreference,
        PatientProfile ManagedPatient,
        PatientProfile RevokedPatient,
        CareRelationship ActiveRelationship,
        CareRelationship RevokedRelationship,
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        AvailabilitySlot[] Slots,
        Appointment[] AccessibleAppointments,
        Appointment[] AllAppointments,
        Appointment ConfirmedAppointment,
        Appointment ManagedAppointment,
        Appointment RevokedAppointment,
        AppointmentRescheduleHistory RescheduleHistory);
}
