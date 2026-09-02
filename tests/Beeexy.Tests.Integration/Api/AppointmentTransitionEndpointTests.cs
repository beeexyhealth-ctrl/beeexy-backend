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
[Trait("Category", "Phase8Acceptance")]
public sealed class AppointmentTransitionEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private FixtureGraph graph = null!;
    private BeeexyApiFactory factory = null!;
    private string ownerToken = null!;
    private string schedulerToken = null!;
    private string wrongClinicToken = null!;
    private string nonSchedulerToken = null!;

    [Fact]
    public async Task Confirm_IsPersistentIdempotentSafeAndVisibleInPatientDetail()
    {
        var clinicalCounts = await ReadClinicalCountsAsync();
        using var scheduler = Client(schedulerToken);

        using var first = await scheduler.PostAsync(Action("confirm"), null);
        using var second = await scheduler.PostAsync(Action("confirm"), null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var response = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal("Confirmed", response.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            ["appointmentId", "clinicId", "clinicTimeZone", "createdAt", "doctorId",
                "endsAt", "locationId", "modality", "patientId", "slotId", "startsAt",
                "status"],
            response.RootElement.EnumerateObject().Select(value => value.Name).Order());
        AssertSafeSchedulerPayload(response.RootElement.ToString());

        var persisted = await ReadAppointmentAsync();
        Assert.Equal(AppointmentStatus.Confirmed, persisted.Status);
        Assert.Equal(2, persisted.Version);
        Assert.True(persisted.ReservesSlot);
        var history = await ReadHistoryAsync();
        Assert.Equal([1L, 2L], history.Select(value => value.Sequence));
        Assert.Equal(AppointmentActorType.AppointmentScheduler, history[1].ActorType);
        Assert.Equal(AppointmentStatusAction.Confirmation, history[1].Action);
        Assert.Equal(graph.Scheduler.Id, history[1].ActorAccountId);

        using var owner = Client(ownerToken);
        using var detailResponse = await owner.GetAsync(
            $"/api/v1/appointments/{graph.Appointment.Id.Value:D}");
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("Confirmed", detail.RootElement.GetProperty("status").GetString());
        var projectedHistory = detail.RootElement.GetProperty("statusHistory")
            .EnumerateArray().ToArray();
        Assert.Equal(2, projectedHistory.Length);
        Assert.Equal(
            "appointmentScheduler",
            projectedHistory[1].GetProperty("actorType").GetString());
        Assert.False(projectedHistory[1].TryGetProperty("actorAccountId", out _));
        Assert.Equal(clinicalCounts, await ReadClinicalCountsAsync());
    }

    [Fact]
    public async Task Reject_PersistsAndReleasesSlotThroughPublicDiscovery()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.DoesNotContain(
            graph.Slot.Id.Value,
            await AvailableSlotIdsAsync(anonymous));
        using var scheduler = Client(schedulerToken);

        using var first = await scheduler.PostAsync(Action("reject"), null);
        using var second = await scheduler.PostAsync(Action("reject"), null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(graph.Slot.Id.Value, await AvailableSlotIdsAsync(anonymous));
        var persisted = await ReadAppointmentAsync();
        Assert.Equal(AppointmentStatus.Rejected, persisted.Status);
        Assert.Equal(2, persisted.Version);
        Assert.False(persisted.ReservesSlot);
        var history = await ReadHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(AppointmentStatusAction.Rejection, history[1].Action);
        Assert.Equal(graph.Scheduler.Id, history[1].ActorAccountId);
    }

    [Fact]
    public async Task AuthenticationClinicScopeAndMissingResourceUseStableSemantics()
    {
        using var anonymous = factory.CreateApiClient();
        using var nonScheduler = Client(nonSchedulerToken);
        using var wrongClinic = Client(wrongClinicToken);
        using var scheduler = Client(schedulerToken);

        using var unauthenticated = await anonymous.PostAsync(Action("confirm"), null);
        using var forbidden = await nonScheduler.PostAsync(Action("confirm"), null);
        using var crossClinic = await wrongClinic.PostAsync(Action("reject"), null);
        using var missing = await scheduler.PostAsync(
            $"/api/v1/appointments/{Guid.NewGuid():D}/confirm",
            null);
        using var patientDetail = await scheduler.GetAsync(
            $"/api/v1/appointments/{graph.Appointment.Id.Value:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        await AssertProblemAsync(
            forbidden,
            HttpStatusCode.Forbidden,
            "scheduling.appointment_scheduler_forbidden");
        await AssertProblemAsync(
            crossClinic,
            HttpStatusCode.Forbidden,
            "scheduling.appointment_scheduler_forbidden");
        await AssertProblemAsync(
            missing,
            HttpStatusCode.NotFound,
            "scheduling.appointment_target_not_found");
        await AssertProblemAsync(
            patientDetail,
            HttpStatusCode.NotFound,
            "scheduling.appointment_target_not_found");
        Assert.Equal(AppointmentStatus.Requested, (await ReadAppointmentAsync()).Status);
        Assert.Single(await ReadHistoryAsync());
    }

    [Fact]
    public async Task OppositeTransition_ReturnsConflictWithoutFurtherHistory()
    {
        using var scheduler = Client(schedulerToken);
        using var confirmed = await scheduler.PostAsync(Action("confirm"), null);
        using var rejected = await scheduler.PostAsync(Action("reject"), null);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        await AssertProblemAsync(
            rejected,
            HttpStatusCode.Conflict,
            "scheduling.appointment_transition_conflict");
        var persisted = await ReadAppointmentAsync();
        Assert.Equal(AppointmentStatus.Confirmed, persisted.Status);
        Assert.Equal(2, persisted.Version);
        Assert.Equal(2, (await ReadHistoryAsync()).Count);
    }

    [Theory]
    [InlineData("confirm", "reject")]
    [InlineData("confirm", "confirm")]
    [InlineData("reject", "reject")]
    public async Task ConcurrentActions_ApplyExactlyOneTransition(
        string firstAction,
        string secondAction)
    {
        using var firstClient = Client(schedulerToken);
        using var secondClient = Client(schedulerToken);

        var responses = await Task.WhenAll(
            firstClient.PostAsync(Action(firstAction), null),
            secondClient.PostAsync(Action(secondAction), null));
        using var first = responses[0];
        using var second = responses[1];

        if (firstAction == secondAction)
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        else
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Conflict],
                responses.Select(value => value.StatusCode).Order());
        }

        var persisted = await ReadAppointmentAsync();
        Assert.Contains(
            persisted.Status,
            new[] { AppointmentStatus.Confirmed, AppointmentStatus.Rejected });
        Assert.Equal(2, persisted.Version);
        var history = await ReadHistoryAsync();
        Assert.Equal([1L, 2L], history.Select(value => value.Sequence));
        Assert.Equal(persisted.Status, history[1].NewStatus);
        Assert.Equal(
            persisted.Status == AppointmentStatus.Confirmed,
            !await IsSlotAvailableAsync());
    }

    [Fact]
    public async Task OpenApiDocumentsBothBearerActionsAndFortyThreePaths()
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal(51, paths.EnumerateObject().Count());
        foreach (var action in new[] { "confirm", "reject" })
        {
            var operation = paths
                .GetProperty($"/api/v1/appointments/{{id}}/{action}")
                .GetProperty("post");
            Assert.True(operation.TryGetProperty("security", out _));
            Assert.Equal(
                ["200", "401", "403", "404", "409", "500"],
                operation.GetProperty("responses").EnumerateObject()
                    .Select(value => value.Name).Order());
        }
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await CleanupAsync();
        graph = CreateGraph();
        dbContext.AddRange(
            graph.Owner,
            graph.OwnerPatient,
            graph.OwnerPreference,
            graph.Scheduler,
            graph.SchedulerPatient,
            graph.SchedulerPreference,
            graph.WrongClinicScheduler,
            graph.WrongClinicPatient,
            graph.WrongClinicPreference,
            graph.NonScheduler,
            graph.NonSchedulerPatient,
            graph.NonSchedulerPreference,
            graph.Clinic,
            graph.Location,
            graph.Doctor,
            graph.Affiliation,
            graph.Slot,
            graph.Appointment);
        await dbContext.SaveChangesAsync();

        factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Scheduling:AppointmentSchedulers:Assignments:0:AccountId"] =
                    graph.Scheduler.Id.Value.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:0"] =
                    graph.Clinic.Id.Value.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:1:AccountId"] =
                    graph.WrongClinicScheduler.Id.Value.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:1:ClinicIds:0"] =
                    Guid.NewGuid().ToString("D")
            },
            configureServices: services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new StubClock(Now));
            });
        using var client = factory.CreateApiClient();
        var issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        ownerToken = Issue(issuer, graph.Owner.Id);
        schedulerToken = Issue(issuer, graph.Scheduler.Id);
        wrongClinicToken = Issue(issuer, graph.WrongClinicScheduler.Id);
        nonSchedulerToken = Issue(issuer, graph.NonScheduler.Id);
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
        var scheduler = CreateIdentity("scheduler", createdAt);
        var wrong = CreateIdentity("wrong", createdAt);
        var nonScheduler = CreateIdentity("non-scheduler", createdAt);
        var clinic = Clinic.Create(
            DirectoryCode.Create($"appointment-transition-clinic-{suffix}"),
            DirectoryName.Create("Synthetic transition clinic"),
            true,
            createdAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Synthetic transition location"),
            "Lima",
            "Lima",
            "PE",
            IanaTimeZone.Create("America/Lima"),
            true,
            createdAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"appointment-transition-doctor-{suffix}"),
            DirectoryName.Create("Synthetic transition doctor"),
            true,
            createdAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id,
            clinic.Id,
            location.Id,
            true,
            createdAt);
        var slot = AvailabilitySlot.Create(
            doctor.Id,
            clinic.Id,
            location.Id,
            Now.AddDays(1),
            Now.AddDays(1).AddMinutes(30),
            IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson,
            true,
            createdAt);
        var appointment = Appointment.Create(
            owner.Patient.Id,
            slot,
            owner.Account.Id,
            AppointmentModality.InPerson,
            AppointmentReason.Create("Private scheduling reason"),
            EntityId.New(),
            AppointmentRequestFingerprint.Create(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            Now.AddHours(-1));
        return new FixtureGraph(
            owner.Account,
            owner.Patient,
            owner.Preference,
            scheduler.Account,
            scheduler.Patient,
            scheduler.Preference,
            wrong.Account,
            wrong.Patient,
            wrong.Preference,
            nonScheduler.Account,
            nonScheduler.Patient,
            nonScheduler.Preference,
            clinic,
            location,
            doctor,
            affiliation,
            slot,
            appointment);
    }

    private IdentityGraph CreateIdentity(string category, DateTimeOffset createdAt)
    {
        var account = Account.Create(
            NormalizedEmail.Create(
                $"appointment-transition-{category}-{suffix}@example.test"),
            createdAt);
        return new IdentityGraph(
            account,
            PatientProfile.Create(
                BeeexyId.Create(
                    $"BXY-APT-{category}-{suffix}".ToUpperInvariant()),
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

    private string Action(string action) =>
        $"/api/v1/appointments/{graph.Appointment.Id.Value:D}/{action}";

    private static string Issue(IAccessTokenIssuer issuer, EntityId accountId) =>
        issuer.Issue(accountId, EntityId.New(), DateTimeOffset.UtcNow).Value;

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<Appointment> ReadAppointmentAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Appointments.AsNoTracking()
            .SingleAsync(value => value.Id == graph.Appointment.Id);
    }

    private async Task<List<AppointmentStatusHistory>> ReadHistoryAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.AppointmentStatusHistory.AsNoTracking()
            .Where(value => value.AppointmentId == graph.Appointment.Id)
            .OrderBy(value => value.Sequence)
            .ToListAsync();
    }

    private async Task<(int PreTriage, int ClinicalHistory, int Fhir)> ReadClinicalCountsAsync()
    {
        await using var dbContext = CreateDbContext();
        return (
            await dbContext.PreTriageEpisodes.CountAsync(),
            await dbContext.ClinicalHistoryEvents.CountAsync(),
            await dbContext.FhirExports.CountAsync());
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

    private async Task<bool> IsSlotAvailableAsync()
    {
        using var client = factory.CreateApiClient();
        return (await AvailableSlotIdsAsync(client)).Contains(graph.Slot.Id.Value);
    }

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM scheduling.appointment_status_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-transition-%@example.test')); " +
            "DELETE FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-transition-%@example.test'); " +
            "DELETE FROM scheduling.availability_slots WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-transition-doctor-%'); " +
            "DELETE FROM directory.doctor_affiliations WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-transition-doctor-%'); " +
            "DELETE FROM directory.doctors WHERE code LIKE 'appointment-transition-doctor-%'; " +
            "DELETE FROM directory.clinic_locations WHERE clinic_id IN " +
            "(SELECT id FROM directory.clinics WHERE code LIKE 'appointment-transition-clinic-%'); " +
            "DELETE FROM directory.clinics WHERE code LIKE 'appointment-transition-clinic-%'; " +
            "DELETE FROM patients.user_preferences WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-transition-%@example.test'); " +
            "DELETE FROM patients.patient_profiles WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-transition-%@example.test'); " +
            "DELETE FROM identity.accounts WHERE normalized_email LIKE 'appointment-transition-%@example.test';";
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
        AssertSafeSchedulerPayload(problem.RootElement.ToString());
    }

    private static void AssertSafeSchedulerPayload(string value)
    {
        foreach (var forbidden in new[]
        {
            "reason", "version", "actorAccount", "scheduler@", "fingerprint",
            "idempotency", "diagnosis", "urgency", "clinicalHistory", "preTriage", "fhir"
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
        Account Owner,
        PatientProfile OwnerPatient,
        UserPreference OwnerPreference,
        Account Scheduler,
        PatientProfile SchedulerPatient,
        UserPreference SchedulerPreference,
        Account WrongClinicScheduler,
        PatientProfile WrongClinicPatient,
        UserPreference WrongClinicPreference,
        Account NonScheduler,
        PatientProfile NonSchedulerPatient,
        UserPreference NonSchedulerPreference,
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        AvailabilitySlot Slot,
        Appointment Appointment);
}
