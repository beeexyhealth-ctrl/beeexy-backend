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
public sealed class AppointmentRescheduleEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private FixtureGraph graph = null!;
    private BeeexyApiFactory factory = null!;
    private string ownerToken = null!;
    private string managerToken = null!;
    private string revokedToken = null!;
    private string unrelatedToken = null!;
    private string schedulerToken = null!;

    [Fact]
    public async Task RequestedReschedule_IsAtomicAuditedAndRemainsConfirmable()
    {
        using var anonymous = factory.CreateApiClient();
        Assert.False(await IsAvailableAsync(graph.SourceRequestedSlot));
        Assert.True(await IsAvailableAsync(graph.TargetRequestedSlot));
        using var owner = Client(ownerToken);

        using var response = await owner.PostAsJsonAsync(
            Reschedule(graph.OwnerRequested),
            new { slotId = graph.TargetRequestedSlot.Id.Value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(graph.OwnerRequested.Id.Value,
            document.RootElement.GetProperty("appointmentId").GetGuid());
        Assert.Equal(graph.TargetRequestedSlot.Id.Value,
            document.RootElement.GetProperty("slotId").GetGuid());
        Assert.Equal(graph.DoctorB.Id.Value,
            document.RootElement.GetProperty("doctorId").GetGuid());
        Assert.Equal(graph.ClinicB.Id.Value,
            document.RootElement.GetProperty("clinicId").GetGuid());
        Assert.Equal("America/New_York",
            document.RootElement.GetProperty("clinicTimeZone").GetString());
        Assert.Equal("Requested", document.RootElement.GetProperty("status").GetString());
        AssertSafe(document.RootElement.ToString());

        var persisted = await ReadAppointmentAsync(graph.OwnerRequested.Id);
        Assert.Equal(graph.OwnerRequested.Id, persisted.Id);
        Assert.Equal(graph.TargetRequestedSlot.Id, persisted.AvailabilitySlotId);
        Assert.Equal(graph.TargetRequestedSlot.StartsAt, persisted.ScheduledStartAt);
        Assert.Equal(AppointmentStatus.Requested, persisted.Status);
        Assert.Equal(2, persisted.Version);
        Assert.Single(await ReadStatusHistoryAsync(persisted.Id));
        var audit = Assert.Single(await ReadRescheduleHistoryAsync(persisted.Id));
        Assert.Equal(graph.SourceRequestedSlot.Id, audit.PreviousSlotId);
        Assert.Equal(graph.TargetRequestedSlot.Id, audit.NewSlotId);
        Assert.Equal(graph.Owner.Account.Id, audit.ActorAccountId);
        Assert.True(await IsAvailableAsync(graph.SourceRequestedSlot));
        Assert.False(await IsAvailableAsync(graph.TargetRequestedSlot));

        using var detailResponse = await owner.GetAsync(
            $"/api/v1/appointments/{persisted.Id.Value:D}");
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(graph.TargetRequestedSlot.Id.Value,
            detail.RootElement.GetProperty("slotId").GetGuid());
        Assert.Single(detail.RootElement.GetProperty("statusHistory").EnumerateArray());
        var projectedAudit = Assert.Single(
            detail.RootElement.GetProperty("rescheduleHistory").EnumerateArray());
        Assert.Equal(graph.SourceRequestedSlot.Id.Value,
            projectedAudit.GetProperty("previousSlotId").GetGuid());
        Assert.Equal(graph.TargetRequestedSlot.Id.Value,
            projectedAudit.GetProperty("newSlotId").GetGuid());
        Assert.False(projectedAudit.TryGetProperty("actorAccountId", out _));

        using var sameSlot = await owner.PostAsJsonAsync(
            Reschedule(graph.OwnerRequested),
            new { slotId = graph.TargetRequestedSlot.Id.Value });
        Assert.Equal(HttpStatusCode.OK, sameSlot.StatusCode);
        Assert.Equal(2, (await ReadAppointmentAsync(persisted.Id)).Version);
        Assert.Single(await ReadRescheduleHistoryAsync(persisted.Id));

        using var scheduler = Client(schedulerToken);
        using var confirmed = await scheduler.PostAsync(
            SchedulerAction(graph.OwnerRequested, "confirm"), null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [Fact]
    public async Task ConfirmedReschedule_PreservesStatusAndRemainsCancellable()
    {
        using var owner = Client(ownerToken);
        var statusHistoryCount = (await ReadStatusHistoryAsync(graph.OwnerConfirmed.Id)).Count;

        using var response = await owner.PostAsJsonAsync(
            Reschedule(graph.OwnerConfirmed),
            new { slotId = graph.TargetConfirmedSlot.Id.Value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persisted = await ReadAppointmentAsync(graph.OwnerConfirmed.Id);
        Assert.Equal(AppointmentStatus.Confirmed, persisted.Status);
        Assert.Equal(graph.TargetConfirmedSlot.Id, persisted.AvailabilitySlotId);
        Assert.Equal(3, persisted.Version);
        Assert.Equal(statusHistoryCount,
            (await ReadStatusHistoryAsync(persisted.Id)).Count);
        Assert.Single(await ReadRescheduleHistoryAsync(persisted.Id));
        Assert.True(await IsAvailableAsync(graph.SourceConfirmedSlot));
        Assert.False(await IsAvailableAsync(graph.TargetConfirmedSlot));

        using var cancelled = await owner.PostAsync(
            $"/api/v1/appointments/{persisted.Id.Value:D}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(
            AppointmentStatus.Cancelled,
            (await ReadAppointmentAsync(persisted.Id)).Status);
        Assert.True(await IsAvailableAsync(graph.TargetConfirmedSlot));
    }

    [Fact]
    public async Task RescheduledRequestedAppointment_RemainsRejectable()
    {
        using var owner = Client(ownerToken);
        using var moved = await owner.PostAsJsonAsync(
            Reschedule(graph.RaceOne),
            new { slotId = graph.RaceTarget.Id.Value });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        using var scheduler = Client(schedulerToken);

        using var rejected = await scheduler.PostAsync(
            SchedulerAction(graph.RaceOne, "reject"), null);

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var persisted = await ReadAppointmentAsync(graph.RaceOne.Id);
        Assert.Equal(AppointmentStatus.Rejected, persisted.Status);
        Assert.Equal(graph.RaceTarget.Id, persisted.AvailabilitySlotId);
        Assert.True(await IsAvailableAsync(graph.RaceTarget));
        Assert.Single(await ReadRescheduleHistoryAsync(persisted.Id));
        Assert.Equal(2, (await ReadStatusHistoryAsync(persisted.Id)).Count);
    }

    [Fact]
    public async Task CurrentManagerAuthorityIsRequiredAndSchedulerAloneIsConcealed()
    {
        using var anonymous = factory.CreateApiClient();
        using var manager = Client(managerToken);
        using var revoked = Client(revokedToken);
        using var unrelated = Client(unrelatedToken);
        using var scheduler = Client(schedulerToken);
        using var owner = Client(ownerToken);

        using var unauthenticated = await anonymous.PostAsJsonAsync(
            Reschedule(graph.OwnerRequested),
            new { slotId = graph.TargetRequestedSlot.Id.Value });
        using var managed = await manager.PostAsJsonAsync(
            Reschedule(graph.ManagedRequested),
            new { slotId = graph.ManagedTargetSlot.Id.Value });
        using var revokedResult = await revoked.PostAsJsonAsync(
            Reschedule(graph.RevokedRequested),
            new { slotId = graph.RevokedTargetSlot.Id.Value });
        using var inaccessible = await unrelated.PostAsJsonAsync(
            Reschedule(graph.OwnerRequested),
            new { slotId = graph.TargetRequestedSlot.Id.Value });
        using var schedulerOnly = await scheduler.PostAsJsonAsync(
            Reschedule(graph.OwnerRequested),
            new { slotId = graph.TargetRequestedSlot.Id.Value });
        using var missing = await owner.PostAsJsonAsync(
            $"/api/v1/appointments/{Guid.NewGuid():D}/reschedule",
            new { slotId = graph.TargetRequestedSlot.Id.Value });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, managed.StatusCode);
        foreach (var concealed in new[]
            { revokedResult, inaccessible, schedulerOnly, missing })
        {
            await AssertProblemAsync(
                concealed,
                HttpStatusCode.NotFound,
                "scheduling.appointment_target_not_found");
        }

        Assert.Equal(graph.ManagedTargetSlot.Id,
            (await ReadAppointmentAsync(graph.ManagedRequested.Id)).AvailabilitySlotId);
        Assert.Equal(graph.RevokedSourceSlot.Id,
            (await ReadAppointmentAsync(graph.RevokedRequested.Id)).AvailabilitySlotId);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, "scheduling.appointment_target_not_found")]
    [InlineData("expired", HttpStatusCode.UnprocessableEntity, "scheduling.slot_expired")]
    [InlineData("unpublished", HttpStatusCode.UnprocessableEntity, "scheduling.slot_unbookable")]
    [InlineData("modality", HttpStatusCode.UnprocessableEntity, "scheduling.modality_mismatch")]
    [InlineData("occupied", HttpStatusCode.Conflict, "scheduling.slot_reserved")]
    public async Task InvalidTarget_RollsBackCompletely(
        string targetKind,
        HttpStatusCode expectedStatus,
        string errorCode)
    {
        var targetId = targetKind switch
        {
            "missing" => EntityId.New(),
            "expired" => graph.ExpiredTarget.Id,
            "unpublished" => graph.UnpublishedTarget.Id,
            "modality" => graph.VirtualTarget.Id,
            "occupied" => graph.OccupiedTarget.Id,
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind))
        };
        using var owner = Client(ownerToken);
        var before = await ReadAppointmentAsync(graph.OwnerRequested.Id);
        var statusHistoryCount = (await ReadStatusHistoryAsync(before.Id)).Count;

        using var response = await owner.PostAsJsonAsync(
            Reschedule(graph.OwnerRequested),
            new { slotId = targetId.Value });

        await AssertProblemAsync(response, expectedStatus, errorCode);
        var after = await ReadAppointmentAsync(before.Id);
        Assert.Equal(before.AvailabilitySlotId, after.AvailabilitySlotId);
        Assert.Equal(before.ScheduledStartAt, after.ScheduledStartAt);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(statusHistoryCount, (await ReadStatusHistoryAsync(after.Id)).Count);
        Assert.Empty(await ReadRescheduleHistoryAsync(after.Id));
        Assert.False(await IsAvailableAsync(graph.SourceRequestedSlot));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidSourceState_ConflictsWithoutSlotOrHistoryMutation(bool cancelled)
    {
        var appointment = cancelled ? graph.Cancelled : graph.Rejected;
        var before = await ReadAppointmentAsync(appointment.Id);
        var statusHistoryCount = (await ReadStatusHistoryAsync(before.Id)).Count;
        using var owner = Client(ownerToken);

        using var response = await owner.PostAsJsonAsync(
            Reschedule(appointment),
            new { slotId = graph.TargetRequestedSlot.Id.Value });

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "scheduling.appointment_reschedule_conflict");
        var after = await ReadAppointmentAsync(before.Id);
        Assert.Equal(before.AvailabilitySlotId, after.AvailabilitySlotId);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(statusHistoryCount, (await ReadStatusHistoryAsync(after.Id)).Count);
        Assert.Empty(await ReadRescheduleHistoryAsync(after.Id));
    }

    [Fact]
    public async Task TwoAppointmentsRacingForOneTarget_LeaveLoserOnReservedSource()
    {
        using var firstClient = Client(ownerToken);
        using var secondClient = Client(ownerToken);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(
                Reschedule(graph.RaceOne),
                new { slotId = graph.RaceTarget.Id.Value }),
            secondClient.PostAsJsonAsync(
                Reschedule(graph.RaceTwo),
                new { slotId = graph.RaceTarget.Id.Value }));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(value => value.StatusCode).Order());
        var firstSaved = await ReadAppointmentAsync(graph.RaceOne.Id);
        var secondSaved = await ReadAppointmentAsync(graph.RaceTwo.Id);
        var winners = new[] { firstSaved, secondSaved }
            .Where(value => value.AvailabilitySlotId == graph.RaceTarget.Id)
            .ToArray();
        var loser = Assert.Single(new[] { firstSaved, secondSaved }
            .Where(value => value.AvailabilitySlotId != graph.RaceTarget.Id));
        Assert.Single(winners);
        Assert.Contains(
            loser.AvailabilitySlotId,
            new[] { graph.RaceSourceOne.Id, graph.RaceSourceTwo.Id });
        Assert.False(await IsAvailableAsync(graph.RaceTarget));
        Assert.False(await IsAvailableAsync(
            loser.AvailabilitySlotId == graph.RaceSourceOne.Id
                ? graph.RaceSourceOne
                : graph.RaceSourceTwo));
        Assert.Equal(1,
            (await ReadRescheduleHistoryAsync(firstSaved.Id)).Count +
            (await ReadRescheduleHistoryAsync(secondSaved.Id)).Count);
        Assert.Single(await ReadStatusHistoryAsync(firstSaved.Id));
        Assert.Single(await ReadStatusHistoryAsync(secondSaved.Id));
    }

    [Fact]
    public async Task OpenApiDocumentsRescheduleRequestAndFortyThreePaths()
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths
            .GetProperty("/api/v1/appointments/{id}/reschedule")
            .GetProperty("post");

        Assert.Equal(43, paths.EnumerateObject().Count());
        Assert.True(operation.TryGetProperty("security", out _));
        Assert.Equal(
            ["200", "400", "401", "404", "409", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order());
        var schema = document.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("RescheduleAppointmentRequest")
            .GetProperty("properties");
        Assert.Equal(["slotId"], schema.EnumerateObject().Select(value => value.Name));
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await CleanupAsync();
        graph = CreateGraph();
        dbContext.AddRange(
            graph.Owner.Account, graph.Owner.Patient, graph.Owner.Preference,
            graph.Manager.Account, graph.Manager.Patient, graph.Manager.Preference,
            graph.RevokedManager.Account, graph.RevokedManager.Patient,
            graph.RevokedManager.Preference,
            graph.Unrelated.Account, graph.Unrelated.Patient, graph.Unrelated.Preference,
            graph.Scheduler.Account, graph.Scheduler.Patient, graph.Scheduler.Preference,
            graph.ManagedPatient, graph.RevokedManagedPatient,
            graph.ActiveRelationship, graph.RevokedRelationship,
            graph.ClinicA, graph.LocationA, graph.DoctorA, graph.AffiliationA,
            graph.ClinicB, graph.LocationB, graph.DoctorB, graph.AffiliationB);
        dbContext.AvailabilitySlots.AddRange(graph.Slots);
        dbContext.Appointments.AddRange(graph.Appointments);
        await dbContext.SaveChangesAsync();

        factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Scheduling:AppointmentSchedulers:Assignments:0:AccountId"] =
                    graph.Scheduler.Account.Id.Value.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:0"] =
                    graph.ClinicA.Id.Value.ToString("D"),
                ["Scheduling:AppointmentSchedulers:Assignments:0:ClinicIds:1"] =
                    graph.ClinicB.Id.Value.ToString("D")
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
        revokedToken = Issue(issuer, graph.RevokedManager.Account.Id);
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
            BeeexyId.Create($"BXY-APR-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt, accountId: null);
        var revokedManagedPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-APR-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt, accountId: null);
        var activeRelationship = CareRelationship.Create(
            manager.Patient.Id, managedPatient.Id, CareRelationshipType.Caregiver,
            manager.Account.Id,
            AuthorizationAttestation.Create("phase-8.7-test", createdAt), createdAt);
        var revokedRelationship = CareRelationship.Create(
            revokedManager.Patient.Id, revokedManagedPatient.Id,
            CareRelationshipType.Caregiver, revokedManager.Account.Id,
            AuthorizationAttestation.Create("phase-8.7-test", createdAt), createdAt);
        revokedRelationship.Revoke(revokedManager.Account.Id, createdAt.AddMinutes(1));

        var clinicA = Clinic.Create(
            DirectoryCode.Create($"appointment-reschedule-clinic-a-{suffix}"),
            DirectoryName.Create("Synthetic Lima reschedule clinic"), true, createdAt);
        var locationA = ClinicLocation.Create(
            clinicA.Id, DirectoryName.Create("Synthetic Lima location"),
            "Lima", "Lima", "PE", IanaTimeZone.Create("America/Lima"), true, createdAt);
        var doctorA = Doctor.Create(
            DirectoryCode.Create($"appointment-reschedule-doctor-a-{suffix}"),
            DirectoryName.Create("Synthetic Lima doctor"), true, createdAt);
        var affiliationA = DoctorAffiliation.Create(
            doctorA.Id, clinicA.Id, locationA.Id, true, createdAt);
        var clinicB = Clinic.Create(
            DirectoryCode.Create($"appointment-reschedule-clinic-b-{suffix}"),
            DirectoryName.Create("Synthetic New York reschedule clinic"), true, createdAt);
        var locationB = ClinicLocation.Create(
            clinicB.Id, DirectoryName.Create("Synthetic New York location"),
            "New York", "NY", "US", IanaTimeZone.Create("America/New_York"),
            true, createdAt);
        var doctorB = Doctor.Create(
            DirectoryCode.Create($"appointment-reschedule-doctor-b-{suffix}"),
            DirectoryName.Create("Synthetic New York doctor"), true, createdAt);
        var affiliationB = DoctorAffiliation.Create(
            doctorB.Id, clinicB.Id, locationB.Id, true, createdAt);

        AvailabilitySlot A(int day, bool published = true) => AvailabilitySlot.Create(
            doctorA.Id, clinicA.Id, locationA.Id, Now.AddDays(day),
            Now.AddDays(day).AddMinutes(30), IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson, published, createdAt);
        AvailabilitySlot B(
            int day,
            bool published = true,
            AppointmentModality modality = AppointmentModality.InPerson) =>
            AvailabilitySlot.Create(
                doctorB.Id, clinicB.Id, locationB.Id, Now.AddDays(day),
                Now.AddDays(day).AddMinutes(30),
                IanaTimeZone.Create("America/New_York"), modality, published, createdAt);

        var slots = Enumerable.Range(1, 16)
            .Select(day => day % 2 == 0 ? B(day) : A(day))
            .ToArray();
        var expired = AvailabilitySlot.Create(
            doctorB.Id, clinicB.Id, locationB.Id, Now, Now.AddMinutes(30),
            IanaTimeZone.Create("America/New_York"), AppointmentModality.InPerson,
            true, createdAt);
        var unpublished = B(17, published: false);
        var virtualTarget = B(18, modality: AppointmentModality.Virtual);
        var allSlots = slots.Concat([expired, unpublished, virtualTarget]).ToArray();

        Appointment Create(
            PatientProfile patient,
            AvailabilitySlot slot,
            EntityId requester,
            string reason) => Appointment.Create(
                patient.Id, slot, requester, AppointmentModality.InPerson,
                AppointmentReason.Create(reason), EntityId.New(),
                AppointmentRequestFingerprint.Create(
                    Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Now.AddHours(-1));

        var ownerRequested = Create(owner.Patient, slots[0], owner.Account.Id, "Owner request");
        var ownerConfirmed = Create(owner.Patient, slots[2], owner.Account.Id, "Confirmed request");
        ownerConfirmed.Confirm(scheduler.Account.Id, Now.AddMinutes(-40));
        var managedRequested = Create(managedPatient, slots[4], manager.Account.Id, "Managed request");
        var revokedRequested = Create(
            revokedManagedPatient, slots[6], revokedManager.Account.Id, "Revoked request");
        var occupier = Create(owner.Patient, slots[8], owner.Account.Id, "Occupied target");
        var raceOne = Create(owner.Patient, slots[10], owner.Account.Id, "Race one");
        var raceTwo = Create(owner.Patient, slots[12], owner.Account.Id, "Race two");
        var cancelled = Create(owner.Patient, slots[14], owner.Account.Id, "Cancelled");
        cancelled.Cancel(owner.Account.Id, Now.AddMinutes(-30));
        var rejected = Create(owner.Patient, slots[15], owner.Account.Id, "Rejected");
        rejected.Reject(scheduler.Account.Id, Now.AddMinutes(-30));
        return new FixtureGraph(
            owner, manager, revokedManager, unrelated, scheduler,
            managedPatient, revokedManagedPatient, activeRelationship, revokedRelationship,
            clinicA, locationA, doctorA, affiliationA,
            clinicB, locationB, doctorB, affiliationB,
            allSlots, ownerRequested, ownerConfirmed, managedRequested, revokedRequested,
            occupier, raceOne, raceTwo, cancelled, rejected,
            slots[0], slots[1], slots[2], slots[3], slots[4], slots[5], slots[6], slots[7],
            slots[8], expired, unpublished, virtualTarget,
            slots[10], slots[12], slots[13]);
    }

    private IdentityGraph CreateIdentity(string category, DateTimeOffset createdAt)
    {
        var account = Account.Create(
            NormalizedEmail.Create(
                $"appointment-reschedule-{category}-{suffix}@example.test"), createdAt);
        return new IdentityGraph(
            account,
            PatientProfile.Create(
                BeeexyId.Create($"BXY-APR-{Guid.NewGuid():N}".ToUpperInvariant()),
                createdAt, account.Id),
            UserPreference.Create(account.Id, UserTimeZone.Create("America/Lima"), createdAt));
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Reschedule(Appointment appointment) =>
        $"/api/v1/appointments/{appointment.Id.Value:D}/reschedule";

    private static string SchedulerAction(Appointment appointment, string action) =>
        $"/api/v1/appointments/{appointment.Id.Value:D}/{action}";

    private static string Issue(IAccessTokenIssuer issuer, EntityId accountId) =>
        issuer.Issue(accountId, EntityId.New(), DateTimeOffset.UtcNow).Value;

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString).Options);

    private async Task<Appointment> ReadAppointmentAsync(EntityId id)
    {
        await using var context = CreateDbContext();
        return await context.Appointments.AsNoTracking().SingleAsync(value => value.Id == id);
    }

    private async Task<List<AppointmentStatusHistory>> ReadStatusHistoryAsync(EntityId id)
    {
        await using var context = CreateDbContext();
        return await context.AppointmentStatusHistory.AsNoTracking()
            .Where(value => value.AppointmentId == id)
            .OrderBy(value => value.Sequence).ToListAsync();
    }

    private async Task<List<AppointmentRescheduleHistory>> ReadRescheduleHistoryAsync(EntityId id)
    {
        await using var context = CreateDbContext();
        return await context.AppointmentRescheduleHistory.AsNoTracking()
            .Where(value => value.AppointmentId == id)
            .OrderBy(value => value.OccurredAt).ThenBy(value => value.Id).ToListAsync();
    }

    private async Task<bool> IsAvailableAsync(AvailabilitySlot slot)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync(
            $"/api/v1/doctors/{slot.DoctorId.Value:D}/slots");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return document.RootElement.EnumerateArray()
            .Any(value => value.GetProperty("slotId").GetGuid() == slot.Id.Value);
    }

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM scheduling.appointment_reschedule_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-reschedule-%@example.test')); " +
            "DELETE FROM scheduling.appointment_status_history WHERE appointment_id IN " +
            "(SELECT id FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-reschedule-%@example.test')); " +
            "DELETE FROM scheduling.appointments WHERE requesting_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-reschedule-%@example.test'); " +
            "DELETE FROM patients.care_relationships WHERE created_by_account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-reschedule-%@example.test'); " +
            "DELETE FROM scheduling.availability_slots WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-reschedule-doctor-%'); " +
            "DELETE FROM directory.doctor_affiliations WHERE doctor_id IN " +
            "(SELECT id FROM directory.doctors WHERE code LIKE 'appointment-reschedule-doctor-%'); " +
            "DELETE FROM directory.doctors WHERE code LIKE 'appointment-reschedule-doctor-%'; " +
            "DELETE FROM directory.clinic_locations WHERE clinic_id IN " +
            "(SELECT id FROM directory.clinics WHERE code LIKE 'appointment-reschedule-clinic-%'); " +
            "DELETE FROM directory.clinics WHERE code LIKE 'appointment-reschedule-clinic-%'; " +
            "DELETE FROM patients.user_preferences WHERE account_id IN " +
            "(SELECT id FROM identity.accounts WHERE normalized_email LIKE 'appointment-reschedule-%@example.test'); " +
            "DELETE FROM patients.patient_profiles WHERE beeexy_id LIKE 'BXY-APR-%'; " +
            "DELETE FROM identity.accounts WHERE normalized_email LIKE 'appointment-reschedule-%@example.test';";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("errorCode").GetString());
        AssertSafe(document.RootElement.ToString());
    }

    private static void AssertSafe(string value)
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
        Clinic ClinicA,
        ClinicLocation LocationA,
        Doctor DoctorA,
        DoctorAffiliation AffiliationA,
        Clinic ClinicB,
        ClinicLocation LocationB,
        Doctor DoctorB,
        DoctorAffiliation AffiliationB,
        AvailabilitySlot[] Slots,
        Appointment OwnerRequested,
        Appointment OwnerConfirmed,
        Appointment ManagedRequested,
        Appointment RevokedRequested,
        Appointment Occupier,
        Appointment RaceOne,
        Appointment RaceTwo,
        Appointment Cancelled,
        Appointment Rejected,
        AvailabilitySlot SourceRequestedSlot,
        AvailabilitySlot TargetRequestedSlot,
        AvailabilitySlot SourceConfirmedSlot,
        AvailabilitySlot TargetConfirmedSlot,
        AvailabilitySlot ManagedSourceSlot,
        AvailabilitySlot ManagedTargetSlot,
        AvailabilitySlot RevokedSourceSlot,
        AvailabilitySlot RevokedTargetSlot,
        AvailabilitySlot OccupiedTarget,
        AvailabilitySlot ExpiredTarget,
        AvailabilitySlot UnpublishedTarget,
        AvailabilitySlot VirtualTarget,
        AvailabilitySlot RaceSourceOne,
        AvailabilitySlot RaceSourceTwo,
        AvailabilitySlot RaceTarget)
    {
        public Appointment[] Appointments =>
            [OwnerRequested, OwnerConfirmed, ManagedRequested, RevokedRequested,
                Occupier, RaceOne, RaceTwo, Cancelled, Rejected];
    }
}
