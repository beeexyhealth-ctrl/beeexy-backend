using System.Net;
using System.Net.Http.Headers;
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
public sealed class AppointmentCancellationEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 1, 0, 0, TimeSpan.Zero);
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private FixtureGraph graph = null!;
    private BeeexyApiFactory factory = null!;
    private string ownerToken = null!;
    private string managerToken = null!;
    private string revokedManagerToken = null!;
    private string unrelatedToken = null!;
    private string schedulerToken = null!;

    [Fact]
    public async Task OwnerRequestedCancellation_IsIdempotentRetainedAndReleasesSlot()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.DoesNotContain(
            graph.OwnerRequestedSlot.Id.Value,
            await AvailableSlotIdsAsync(anonymous));
        using var owner = Client(ownerToken);

        using var first = await owner.PostAsync(Cancel(graph.OwnerRequested), null);
        using var second = await owner.PostAsync(Cancel(graph.OwnerRequested), null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var response = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", response.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            ["appointmentId", "clinicId", "clinicTimeZone", "createdAt", "doctorId",
                "endsAt", "locationId", "modality", "patientId", "slotId", "startsAt",
                "status"],
            response.RootElement.EnumerateObject().Select(value => value.Name).Order());
        AssertSafeCancellationPayload(response.RootElement.ToString());

        var persisted = await ReadAppointmentAsync(graph.OwnerRequested.Id);
        Assert.Equal(AppointmentStatus.Cancelled, persisted.Status);
        Assert.Equal(2, persisted.Version);
        Assert.False(persisted.ReservesSlot);
        var history = await ReadHistoryAsync(graph.OwnerRequested.Id);
        Assert.Equal([1L, 2L], history.Select(value => value.Sequence));
        Assert.Equal(AppointmentStatus.Requested, history[1].PreviousStatus);
        Assert.Equal(AppointmentStatus.Cancelled, history[1].NewStatus);
        Assert.Equal(AppointmentStatusAction.Cancellation, history[1].Action);
        Assert.Equal(AppointmentActorType.PatientAuthority, history[1].ActorType);
        Assert.Equal(graph.Owner.Account.Id, history[1].ActorAccountId);
        Assert.Contains(
            graph.OwnerRequestedSlot.Id.Value,
            await AvailableSlotIdsAsync(anonymous));

        using var detailResponse = await owner.GetAsync(
            $"/api/v1/appointments/{graph.OwnerRequested.Id.Value:D}");
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("Cancelled", detail.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Sensitive owner reason",
            detail.RootElement.GetProperty("reason").GetString());
        var projectedHistory = detail.RootElement.GetProperty("statusHistory")
            .EnumerateArray().ToArray();
        Assert.Equal(2, projectedHistory.Length);
        Assert.Equal(
            "patientAuthority",
            projectedHistory[1].GetProperty("actorType").GetString());
        Assert.False(projectedHistory[1].TryGetProperty("actorAccountId", out _));
    }

    [Fact]
    public async Task OwnerConfirmedCancellation_PreservesExactOrderedHistoryAndReleasesSlot()
    {
        using var owner = Client(ownerToken);

        using var response = await owner.PostAsync(Cancel(graph.OwnerConfirmed), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persisted = await ReadAppointmentAsync(graph.OwnerConfirmed.Id);
        Assert.Equal(AppointmentStatus.Cancelled, persisted.Status);
        Assert.Equal(3, persisted.Version);
        var history = await ReadHistoryAsync(graph.OwnerConfirmed.Id);
        Assert.Equal(
            [AppointmentStatus.Requested, AppointmentStatus.Confirmed,
                AppointmentStatus.Cancelled],
            history.Select(value => value.NewStatus));
        Assert.Equal(
            [AppointmentStatusAction.Creation, AppointmentStatusAction.Confirmation,
                AppointmentStatusAction.Cancellation],
            history.Select(value => value.Action));
        Assert.Equal(AppointmentStatus.Confirmed, history[2].PreviousStatus);
        Assert.Equal(graph.Owner.Account.Id, history[2].ActorAccountId);
        Assert.True(await IsSlotAvailableAsync(graph.OwnerConfirmedSlot.Id));

        using var detailResponse = await owner.GetAsync(
            $"/api/v1/appointments/{graph.OwnerConfirmed.Id.Value:D}");
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            ["Requested", "Confirmed", "Cancelled"],
            detail.RootElement.GetProperty("statusHistory").EnumerateArray()
                .Select(value => value.GetProperty("newStatus").GetString()));
    }

    [Fact]
    public async Task CurrentPatientAuthorityIsRequiredAndSchedulerAloneIsConcealed()
    {
        using var anonymous = factory.CreateApiClient();
        using var manager = Client(managerToken);
        using var revokedManager = Client(revokedManagerToken);
        using var unrelated = Client(unrelatedToken);
        using var scheduler = Client(schedulerToken);
        using var owner = Client(ownerToken);

        using var unauthenticated = await anonymous.PostAsync(
            Cancel(graph.OwnerRequested), null);
        using var managed = await manager.PostAsync(Cancel(graph.ManagedRequested), null);
        using var revoked = await revokedManager.PostAsync(
            Cancel(graph.RevokedManagedRequested), null);
        using var inaccessible = await unrelated.PostAsync(
            Cancel(graph.OwnerRequested), null);
        using var schedulerOnly = await scheduler.PostAsync(
            Cancel(graph.OwnerRequested), null);
        using var missing = await owner.PostAsync(
            $"/api/v1/appointments/{Guid.NewGuid():D}/cancel",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, managed.StatusCode);
        foreach (var concealed in new[] { revoked, inaccessible, schedulerOnly, missing })
        {
            await AssertProblemAsync(
                concealed,
                HttpStatusCode.NotFound,
                "scheduling.appointment_target_not_found");
        }

        Assert.Equal(
            AppointmentStatus.Cancelled,
            (await ReadAppointmentAsync(graph.ManagedRequested.Id)).Status);
        Assert.Equal(
            AppointmentStatus.Requested,
            (await ReadAppointmentAsync(graph.RevokedManagedRequested.Id)).Status);
        Assert.Equal(
            AppointmentStatus.Requested,
            (await ReadAppointmentAsync(graph.OwnerRequested.Id)).Status);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("completed")]
    [InlineData("noShow")]
    public async Task UnsupportedState_ReturnsConflictWithoutMutation(string state)
    {
        var appointment = state switch
        {
            "rejected" => graph.Rejected,
            "completed" => graph.Completed,
            "noShow" => graph.NoShow,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var before = await ReadAppointmentAsync(appointment.Id);
        var historyCount = (await ReadHistoryAsync(appointment.Id)).Count;
        using var owner = Client(ownerToken);

        using var response = await owner.PostAsync(Cancel(appointment), null);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "scheduling.appointment_transition_conflict");
        var after = await ReadAppointmentAsync(appointment.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(historyCount, (await ReadHistoryAsync(appointment.Id)).Count);
    }

    [Fact]
    public async Task ConcurrentCancellation_IsLogicallyIdempotentWithOneHistoryAppend()
    {
        using var firstClient = Client(ownerToken);
        using var secondClient = Client(ownerToken);

        var responses = await Task.WhenAll(
            firstClient.PostAsync(Cancel(graph.OwnerRequested), null),
            secondClient.PostAsync(Cancel(graph.OwnerRequested), null));
        using var first = responses[0];
        using var second = responses[1];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var persisted = await ReadAppointmentAsync(graph.OwnerRequested.Id);
        Assert.Equal(AppointmentStatus.Cancelled, persisted.Status);
        Assert.Equal(2, persisted.Version);
        Assert.Equal(2, (await ReadHistoryAsync(graph.OwnerRequested.Id)).Count);
        Assert.True(await IsSlotAvailableAsync(graph.OwnerRequestedSlot.Id));
    }

    [Theory]
    [InlineData("confirm", AppointmentStatus.Confirmed)]
    [InlineData("reject", AppointmentStatus.Rejected)]
    public async Task ConcurrentCancellationAndSchedulerAction_AllowOnlyOneStaleWinner(
        string schedulerAction,
        AppointmentStatus schedulerStatus)
    {
        using var owner = Client(ownerToken);
        using var scheduler = Client(schedulerToken);

        var responses = await Task.WhenAll(
            owner.PostAsync(Cancel(graph.OwnerRequested), null),
            scheduler.PostAsync(
                SchedulerAction(graph.OwnerRequested, schedulerAction),
                null));
        using var cancellation = responses[0];
        using var scheduling = responses[1];

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(value => value.StatusCode).Order());
        var persisted = await ReadAppointmentAsync(graph.OwnerRequested.Id);
        Assert.Contains(
            persisted.Status,
            new[] { AppointmentStatus.Cancelled, schedulerStatus });
        Assert.Equal(2, persisted.Version);
        var history = await ReadHistoryAsync(graph.OwnerRequested.Id);
        Assert.Equal([1L, 2L], history.Select(value => value.Sequence));
        Assert.Equal(persisted.Status, history[1].NewStatus);
        Assert.Equal(
            persisted.Status != AppointmentStatus.Confirmed,
            await IsSlotAvailableAsync(graph.OwnerRequestedSlot.Id));
    }

    [Fact]
    public async Task OpenApiDocumentsPatientCancellationAndFortyThreePaths()
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths
            .GetProperty("/api/v1/appointments/{id}/cancel")
            .GetProperty("post");

        Assert.Equal(43, paths.EnumerateObject().Count());
        Assert.True(operation.TryGetProperty("security", out _));
        Assert.Equal(
            ["200", "401", "404", "409", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order());
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await CleanupAsync();
        graph = CreateGraph();
        dbContext.AddRange(
            graph.Owner.Account,
            graph.Owner.Patient,
            graph.Owner.Preference,
            graph.Manager.Account,
            graph.Manager.Patient,
            graph.Manager.Preference,
            graph.RevokedManager.Account,
            graph.RevokedManager.Patient,
            graph.RevokedManager.Preference,
            graph.Unrelated.Account,
            graph.Unrelated.Patient,
            graph.Unrelated.Preference,
            graph.Scheduler.Account,
            graph.Scheduler.Patient,
            graph.Scheduler.Preference,
            graph.ManagedPatient,
            graph.RevokedManagedPatient,
            graph.ActiveRelationship,
            graph.RevokedRelationship,
            graph.Clinic,
            graph.Location,
            graph.Doctor,
            graph.Affiliation);
        dbContext.AvailabilitySlots.AddRange(graph.Slots);
        dbContext.Appointments.AddRange(graph.Appointments);
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE scheduling.appointments SET status = 'completed' WHERE id = {graph.Completed.Id.Value}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE scheduling.appointments SET status = 'no_show' WHERE id = {graph.NoShow.Id.Value}");

        factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Scheduling:AppointmentSchedulers:Assignments:0:AccountId"] =
                    graph.Scheduler.Account.Id.Value.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:0"] =
                    graph.Clinic.Id.Value.ToString("D")
            },
            configureServices: services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new StubClock(Now));
            });
        using var client = factory.CreateApiClient();
        var issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        ownerToken = Issue(issuer, graph.Owner.Account.Id);
        managerToken = Issue(issuer, graph.Manager.Account.Id);
        revokedManagerToken = Issue(issuer, graph.RevokedManager.Account.Id);
        unrelatedToken = Issue(issuer, graph.Unrelated.Account.Id);
        schedulerToken = Issue(issuer, graph.Scheduler.Account.Id);
    }

    public async Task DisposeAsync()
    {
        factory.Dispose();
        await CleanupAsync();
    }

    private FixtureGraph CreateGraph()
    {
        var createdAt = Now.AddDays(-2);
        var owner = CreateIdentity("owner", createdAt);
        var manager = CreateIdentity("manager", createdAt);
        var revokedManager = CreateIdentity("revoked", createdAt);
        var unrelated = CreateIdentity("unrelated", createdAt);
        var scheduler = CreateIdentity("scheduler", createdAt);
        var managedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APC-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt,
            accountId: null);
        var revokedManagedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APC-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt,
            accountId: null);
        var activeRelationship = CareRelationship.Create(
            manager.Patient.Id,
            managedPatient.Id,
            CareRelationshipType.Caregiver,
            manager.Account.Id,
            AuthorizationAttestation.Create("phase-8.6-test", createdAt),
            createdAt);
        var revokedRelationship = CareRelationship.Create(
            revokedManager.Patient.Id,
            revokedManagedPatient.Id,
            CareRelationshipType.Caregiver,
            revokedManager.Account.Id,
            AuthorizationAttestation.Create("phase-8.6-test", createdAt),
            createdAt);
        revokedRelationship.Revoke(revokedManager.Account.Id, createdAt.AddMinutes(1));
        var clinic = Clinic.Create(
            DirectoryCode.Create($"appointment-cancel-clinic-{suffix}"),
            DirectoryName.Create("Synthetic cancellation clinic"),
            true,
            createdAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Synthetic cancellation location"),
            "Lima",
            "Lima",
            "PE",
            IanaTimeZone.Create("America/Lima"),
            true,
            createdAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"appointment-cancel-doctor-{suffix}"),
            DirectoryName.Create("Synthetic cancellation doctor"),
            true,
            createdAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id,
            clinic.Id,
            location.Id,
            true,
            createdAt);
        var slots = Enumerable.Range(1, 7).Select(day => AvailabilitySlot.Create(
            doctor.Id,
            clinic.Id,
            location.Id,
            Now.AddDays(day),
            Now.AddDays(day).AddMinutes(30),
            IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson,
            true,
            createdAt)).ToArray();

        Appointment Create(PatientProfile patient, AvailabilitySlot slot, string reason) =>
            Appointment.Create(
                patient.Id,
                slot,
                patient.AccountId ?? manager.Account.Id,
                AppointmentModality.InPerson,
                AppointmentReason.Create(reason),
                EntityId.New(),
                AppointmentRequestFingerprint.Create(
                    Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Now.AddHours(-1));

        var ownerRequested = Create(owner.Patient, slots[0], "Sensitive owner reason");
        var ownerConfirmed = Create(owner.Patient, slots[1], "Sensitive confirmed reason");
        ownerConfirmed.Confirm(scheduler.Account.Id, Now.AddMinutes(-40));
        var managedRequested = Create(managedPatient, slots[2], "Sensitive managed reason");
        var revokedManagedRequested = Create(
            revokedManagedPatient,
            slots[3],
            "Sensitive revoked reason");
        var rejected = Create(owner.Patient, slots[4], "Sensitive rejected reason");
        rejected.Reject(scheduler.Account.Id, Now.AddMinutes(-40));
        var completed = Create(owner.Patient, slots[5], "Sensitive completed reason");
        var noShow = Create(owner.Patient, slots[6], "Sensitive no-show reason");
        return new FixtureGraph(
            owner,
            manager,
            revokedManager,
            unrelated,
            scheduler,
            managedPatient,
            revokedManagedPatient,
            activeRelationship,
            revokedRelationship,
            clinic,
            location,
            doctor,
            affiliation,
            slots,
            ownerRequested,
            ownerConfirmed,
            managedRequested,
            revokedManagedRequested,
            rejected,
            completed,
            noShow);
    }

    private IdentityGraph CreateIdentity(string category, DateTimeOffset createdAt)
    {
        var account = Account.Create(
            NormalizedEmail.Create(
                $"appointment-cancel-{category}-{suffix}@example.test"),
            createdAt);
        return new IdentityGraph(
            account,
            PatientProfile.Create(
                BeeexyId.Create($"BXY-APC-{Guid.NewGuid():N}".ToUpperInvariant()),
                createdAt,
                account.Id),
            UserPreference.Create(
                account.Id,
                UserTimeZone.Create("America/Lima"),
                createdAt));
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Cancel(Appointment appointment) =>
        $"/api/v1/appointments/{appointment.Id.Value:D}/cancel";

    private static string SchedulerAction(Appointment appointment, string action) =>
        $"/api/v1/appointments/{appointment.Id.Value:D}/{action}";

    private static string Issue(IAccessTokenIssuer issuer, EntityId accountId) =>
        issuer.Issue(accountId, EntityId.New(), DateTimeOffset.UtcNow).Value;

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<Appointment> ReadAppointmentAsync(EntityId appointmentId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Appointments.AsNoTracking()
            .SingleAsync(value => value.Id == appointmentId);
    }

    private async Task<List<AppointmentStatusHistory>> ReadHistoryAsync(
        EntityId appointmentId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.AppointmentStatusHistory.AsNoTracking()
            .Where(value => value.AppointmentId == appointmentId)
            .OrderBy(value => value.Sequence)
            .ToListAsync();
    }

    private async Task<Guid[]> AvailableSlotIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            $"/api/v1/doctors/{graph.Doctor.Id.Value:D}/slots");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return document.RootElement.EnumerateArray()
            .Select(value => value.GetProperty("slotId").GetGuid())
            .ToArray();
    }

    private async Task<bool> IsSlotAvailableAsync(EntityId slotId)
    {
        using var client = factory.CreateApiClient();
        return (await AvailableSlotIdsAsync(client)).Contains(slotId.Value);
    }

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM scheduling.appointment_status_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-cancel-%@example.test')); " +
            "DELETE FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-cancel-%@example.test'); " +
            "DELETE FROM patients.care_relationships WHERE created_by_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-cancel-%@example.test'); " +
            "DELETE FROM scheduling.availability_slots WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-cancel-doctor-%'); " +
            "DELETE FROM directory.doctor_affiliations WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-cancel-doctor-%'); " +
            "DELETE FROM directory.doctors WHERE code LIKE 'appointment-cancel-doctor-%'; " +
            "DELETE FROM directory.clinic_locations WHERE clinic_id IN " +
            "(SELECT id FROM directory.clinics WHERE code LIKE 'appointment-cancel-clinic-%'); " +
            "DELETE FROM directory.clinics WHERE code LIKE 'appointment-cancel-clinic-%'; " +
            "DELETE FROM patients.user_preferences WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-cancel-%@example.test'); " +
            "DELETE FROM patients.patient_profiles WHERE beeexy_id LIKE 'BXY-APC-%'; " +
            "DELETE FROM identity.accounts WHERE normalized_email LIKE 'appointment-cancel-%@example.test';";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(errorCode, problem.RootElement.GetProperty("errorCode").GetString());
        AssertSafeCancellationPayload(problem.RootElement.ToString());
    }

    private static void AssertSafeCancellationPayload(string value)
    {
        foreach (var forbidden in new[]
        {
            "reason", "version", "actorAccount", "fingerprint", "idempotency",
            "diagnosis", "urgency", "clinicalHistory", "preTriage", "fhir",
            "Postgres", "DbUpdate"
        })
        {
            Assert.DoesNotContain(forbidden, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed record IdentityGraph(
        Account Account,
        PatientProfile Patient,
        UserPreference Preference);

    private sealed record FixtureGraph(
        IdentityGraph Owner,
        IdentityGraph Manager,
        IdentityGraph RevokedManager,
        IdentityGraph Unrelated,
        IdentityGraph Scheduler,
        PatientProfile ManagedPatient,
        PatientProfile RevokedManagedPatient,
        CareRelationship ActiveRelationship,
        CareRelationship RevokedRelationship,
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        AvailabilitySlot[] Slots,
        Appointment OwnerRequested,
        Appointment OwnerConfirmed,
        Appointment ManagedRequested,
        Appointment RevokedManagedRequested,
        Appointment Rejected,
        Appointment Completed,
        Appointment NoShow)
    {
        public AvailabilitySlot OwnerRequestedSlot => Slots[0];

        public AvailabilitySlot OwnerConfirmedSlot => Slots[1];

        public Appointment[] Appointments =>
            [OwnerRequested, OwnerConfirmed, ManagedRequested, RevokedManagedRequested,
                Rejected, Completed, NoShow];
    }
}
