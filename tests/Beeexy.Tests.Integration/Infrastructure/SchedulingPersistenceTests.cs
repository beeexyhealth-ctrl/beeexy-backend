using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase8Acceptance")]
public sealed class SchedulingPersistenceTests(PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 31, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SchedulingGraph_PersistsDirectoryPatientAccountAndInitialHistoryRelationships()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 2);
        var appointment = CreateAppointment(graph, graph.Slots[0], AppointmentReason.Create("Follow-up"));
        var rescheduleAudit = AppointmentRescheduleHistory.Create(
            appointment.Id,
            graph.Slots[0].Id,
            graph.Slots[1].Id,
            graph.Account.Id,
            CreatedAt.AddMinutes(1));

        await using (var dbContext = CreateDbContext())
        {
            AddGraph(dbContext, graph);
            dbContext.Appointments.Add(appointment);
            dbContext.AppointmentRescheduleHistory.Add(rescheduleAudit);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        var savedSlot = await verify.AvailabilitySlots.AsNoTracking()
            .SingleAsync(value => value.Id == graph.Slots[0].Id);
        var savedAppointment = await verify.Appointments.AsNoTracking()
            .SingleAsync(value => value.Id == appointment.Id);
        var initialHistory = await verify.AppointmentStatusHistory.AsNoTracking()
            .SingleAsync(value => value.AppointmentId == appointment.Id);
        var savedAudit = await verify.AppointmentRescheduleHistory.AsNoTracking()
            .SingleAsync(value => value.Id == rescheduleAudit.Id);

        Assert.Equal(graph.Doctor.Id, savedSlot.DoctorId);
        Assert.Equal(graph.Clinic.Id, savedSlot.ClinicId);
        Assert.Equal(graph.Location.Id, savedSlot.ClinicLocationId);
        Assert.Equal("America/Lima", savedSlot.ClinicTimeZone.Value);
        Assert.Equal(graph.Patient.Id, savedAppointment.PatientProfileId);
        Assert.Equal(graph.Account.Id, savedAppointment.RequestingAccountId);
        Assert.Equal(graph.Slots[0].StartsAt, savedAppointment.ScheduledStartAt);
        Assert.NotNull(savedAppointment.Reason);
        Assert.Equal(AppointmentStatus.Requested, initialHistory.NewStatus);
        Assert.Null(initialHistory.PreviousStatus);
        Assert.Equal(1, initialHistory.Sequence);
        Assert.Equal(graph.Slots[0].Id, savedAudit.PreviousSlotId);
        Assert.Equal(graph.Slots[1].Id, savedAudit.NewSlotId);
    }

    [Fact]
    public async Task PostgreSql_EnforcesAccountScopedIdempotencyKey()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 2);
        var key = EntityId.New();
        await SaveGraphAsync(graph);
        await SaveAppointmentAsync(CreateAppointment(graph, graph.Slots[0], idempotencyKey: key));

        await using var duplicateContext = CreateDbContext();
        duplicateContext.Appointments.Add(CreateAppointment(
            graph,
            graph.Slots[1],
            idempotencyKey: key));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            duplicateContext.SaveChangesAsync());
        AssertUniqueViolation(exception, "ux_appointments_account_idempotency_key");

        var otherGraph = CreateGraph(slotCount: 1);
        await SaveGraphAsync(otherGraph);
        await SaveAppointmentAsync(CreateAppointment(
            otherGraph,
            otherGraph.Slots[0],
            idempotencyKey: key));
    }

    [Fact]
    public async Task PostgreSql_RejectsSlotLocationFromAnotherClinic()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 0);
        var otherClinic = Clinic.Create(
            DirectoryCode.Create($"scheduling-clinic-{Guid.NewGuid():N}"),
            DirectoryName.Create("Other synthetic scheduling clinic"),
            true,
            CreatedAt);
        var otherLocation = ClinicLocation.Create(
            otherClinic.Id,
            DirectoryName.Create("Other synthetic scheduling location"),
            "Lima",
            "Lima",
            "Peru",
            IanaTimeZone.Create("America/Lima"),
            true,
            CreatedAt);
        await using (var setup = CreateDbContext())
        {
            AddGraph(setup, graph);
            setup.AddRange(otherClinic, otherLocation);
            await setup.SaveChangesAsync();
        }

        var invalidSlot = AvailabilitySlot.Create(
            graph.Doctor.Id,
            graph.Clinic.Id,
            otherLocation.Id,
            CreatedAt.AddDays(1),
            CreatedAt.AddDays(1).AddMinutes(30),
            IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson,
            true,
            CreatedAt);
        await using var invalidContext = CreateDbContext();
        invalidContext.AvailabilitySlots.Add(invalidSlot);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            invalidContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
        Assert.Equal(
            "fk_availability_slots_clinic_locations",
            postgresException.ConstraintName);
    }

    [Fact]
    public async Task PostgreSql_RejectsSecondRequestedOrConfirmedReservationForOneSlot()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 2);
        await SaveGraphAsync(graph);

        await SaveAppointmentAsync(CreateAppointment(graph, graph.Slots[0]));
        await AssertReservationConflictAsync(CreateAppointment(graph, graph.Slots[0]));

        var confirmed = CreateAppointment(graph, graph.Slots[1]);
        confirmed.Confirm(graph.Account.Id, CreatedAt.AddMinutes(1));
        await SaveAppointmentAsync(confirmed);
        await AssertReservationConflictAsync(CreateAppointment(graph, graph.Slots[1]));
    }

    [Fact]
    public async Task RejectedAndCancelledHistory_DoesNotPermanentlyConsumeSlot()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 1);
        await SaveGraphAsync(graph);

        var rejected = CreateAppointment(graph, graph.Slots[0]);
        rejected.Reject(graph.Account.Id, CreatedAt.AddMinutes(1));
        await SaveAppointmentAsync(rejected);

        var cancelled = CreateAppointment(graph, graph.Slots[0]);
        cancelled.Cancel(graph.Account.Id, CreatedAt.AddMinutes(2));
        await SaveAppointmentAsync(cancelled);

        var currentReservation = CreateAppointment(graph, graph.Slots[0]);
        await SaveAppointmentAsync(currentReservation);

        await using var verify = CreateDbContext();
        var statuses = await verify.Appointments.AsNoTracking()
            .Where(value => value.AvailabilitySlotId == graph.Slots[0].Id)
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Select(value => value.Status)
            .ToListAsync();
        Assert.Equal(3, statuses.Count);
        Assert.Contains(AppointmentStatus.Rejected, statuses);
        Assert.Contains(AppointmentStatus.Cancelled, statuses);
        Assert.Contains(AppointmentStatus.Requested, statuses);
    }

    [Fact]
    public async Task ConcurrentTransitions_AllowOneWinnerAndOneOrderedHistoryAppend()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await using (var setup = CreateDbContext())
        {
            AddGraph(setup, graph);
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        await using var confirmContext = CreateDbContext();
        await using var rejectContext = CreateDbContext();
        var confirmCandidate = await confirmContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == appointment.Id);
        var rejectCandidate = await rejectContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == appointment.Id);

        confirmCandidate.Confirm(graph.Account.Id, CreatedAt.AddMinutes(1));
        rejectCandidate.Reject(graph.Account.Id, CreatedAt.AddMinutes(1));
        await confirmContext.SaveChangesAsync();
        var losingException = await Assert.ThrowsAsync<DbUpdateException>(() =>
            rejectContext.SaveChangesAsync());
        AssertUniqueViolation(
            losingException,
            "ux_appointment_status_history_appointment_sequence");

        await using var verify = CreateDbContext();
        var saved = await verify.Appointments.AsNoTracking()
            .SingleAsync(value => value.Id == appointment.Id);
        var history = await verify.AppointmentStatusHistory.AsNoTracking()
            .Where(value => value.AppointmentId == appointment.Id)
            .OrderBy(value => value.Sequence)
            .ToListAsync();
        Assert.Equal(AppointmentStatus.Confirmed, saved.Status);
        Assert.Equal(2, saved.Version);
        Assert.Equal([1L, 2L], history.Select(value => value.Sequence));
        Assert.Equal(AppointmentStatus.Requested, history[1].PreviousStatus);
        Assert.Equal(AppointmentStatus.Confirmed, history[1].NewStatus);
        Assert.Equal(AppointmentStatusAction.Confirmation, history[1].Action);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleCancellationCannotFollowConcurrentSchedulerTransition(
        bool confirmWins)
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await using (var setup = CreateDbContext())
        {
            AddGraph(setup, graph);
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        await using var cancellationContext = CreateDbContext();
        await using var schedulerContext = CreateDbContext();
        var staleCancellation = await cancellationContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == appointment.Id);
        var schedulerTransition = await schedulerContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == appointment.Id);
        staleCancellation.Cancel(graph.Account.Id, CreatedAt.AddMinutes(2));
        if (confirmWins)
        {
            schedulerTransition.Confirm(graph.Account.Id, CreatedAt.AddMinutes(1));
        }
        else
        {
            schedulerTransition.Reject(graph.Account.Id, CreatedAt.AddMinutes(1));
        }

        await schedulerContext.SaveChangesAsync();
        var losingException = await Assert.ThrowsAsync<DbUpdateException>(() =>
            cancellationContext.SaveChangesAsync());
        AssertUniqueViolation(
            losingException,
            "ux_appointment_status_history_appointment_sequence");

        await using var verify = CreateDbContext();
        var saved = await verify.Appointments.AsNoTracking()
            .SingleAsync(value => value.Id == appointment.Id);
        var history = await verify.AppointmentStatusHistory.AsNoTracking()
            .Where(value => value.AppointmentId == appointment.Id)
            .OrderBy(value => value.Sequence)
            .ToListAsync();
        var winningStatus = confirmWins
            ? AppointmentStatus.Confirmed
            : AppointmentStatus.Rejected;
        Assert.Equal(winningStatus, saved.Status);
        Assert.Equal(2, saved.Version);
        Assert.Equal([1L, 2L], history.Select(value => value.Sequence));
        Assert.Equal(winningStatus, history[1].NewStatus);
        Assert.Equal(confirmWins, saved.ReservesSlot);
    }

    [Theory]
    [InlineData("cancel", AppointmentStatus.Cancelled)]
    [InlineData("confirm", AppointmentStatus.Confirmed)]
    [InlineData("reject", AppointmentStatus.Rejected)]
    public async Task StaleRescheduleCannotOverwriteConcurrentStatusMutation(
        string winningAction,
        AppointmentStatus winningStatus)
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 2);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await using (var setup = CreateDbContext())
        {
            AddGraph(setup, graph);
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        await using var rescheduleContext = CreateDbContext();
        await using var transitionContext = CreateDbContext();
        var staleReschedule = await rescheduleContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == appointment.Id);
        var concurrentTransition = await transitionContext.Appointments
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == appointment.Id);
        var audit = staleReschedule.Reschedule(
            graph.Slots[1],
            graph.Account.Id,
            CreatedAt.AddMinutes(2));
        Assert.NotNull(audit);
        rescheduleContext.AppointmentRescheduleHistory.Add(audit);
        switch (winningAction)
        {
            case "cancel":
                concurrentTransition.Cancel(graph.Account.Id, CreatedAt.AddMinutes(1));
                break;
            case "confirm":
                concurrentTransition.Confirm(graph.Account.Id, CreatedAt.AddMinutes(1));
                break;
            case "reject":
                concurrentTransition.Reject(graph.Account.Id, CreatedAt.AddMinutes(1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(winningAction));
        }

        await transitionContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            rescheduleContext.SaveChangesAsync());

        await using var verify = CreateDbContext();
        var saved = await verify.Appointments.AsNoTracking()
            .SingleAsync(value => value.Id == appointment.Id);
        var statusHistory = await verify.AppointmentStatusHistory.AsNoTracking()
            .Where(value => value.AppointmentId == appointment.Id)
            .OrderBy(value => value.Sequence)
            .ToListAsync();
        var rescheduleCount = await verify.AppointmentRescheduleHistory.AsNoTracking()
            .CountAsync(value => value.AppointmentId == appointment.Id);
        Assert.Equal(winningStatus, saved.Status);
        Assert.Equal(graph.Slots[0].Id, saved.AvailabilitySlotId);
        Assert.Equal(2, saved.Version);
        Assert.Equal([1L, 2L], statusHistory.Select(value => value.Sequence));
        Assert.Equal(0, rescheduleCount);
        Assert.Equal(winningStatus == AppointmentStatus.Confirmed, saved.ReservesSlot);
    }

    [Fact]
    public async Task RestrictiveForeignKeys_PreserveAppointmentsAndHistory()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await using (var setup = CreateDbContext())
        {
            AddGraph(setup, graph);
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await AssertForeignKeyDeleteFailureAsync(
            connection,
            "directory.doctors",
            graph.Doctor.Id.Value);
        await AssertForeignKeyDeleteFailureAsync(
            connection,
            "patients.patient_profiles",
            graph.Patient.Id.Value);
        await AssertForeignKeyDeleteFailureAsync(
            connection,
            "scheduling.availability_slots",
            graph.Slots[0].Id.Value);
        await AssertForeignKeyDeleteFailureAsync(
            connection,
            "scheduling.appointments",
            appointment.Id.Value);

        await using var verify = CreateDbContext();
        Assert.True(await verify.Appointments.AnyAsync(value => value.Id == appointment.Id));
        Assert.True(await verify.AppointmentStatusHistory.AnyAsync(
            value => value.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task DbContext_RejectsNormalDeletionOfAppointmentsAndAppendOnlyHistory()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await using (var setup = CreateDbContext())
        {
            AddGraph(setup, graph);
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        await using (var historyContext = CreateDbContext())
        {
            var history = await historyContext.AppointmentStatusHistory
                .SingleAsync(value => value.AppointmentId == appointment.Id);
            historyContext.Remove(history);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                historyContext.SaveChangesAsync());
        }

        await using (var appointmentContext = CreateDbContext())
        {
            var savedAppointment = await appointmentContext.Appointments
                .SingleAsync(value => value.Id == appointment.Id);
            appointmentContext.Remove(savedAppointment);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                appointmentContext.SaveChangesAsync());
        }

        await using var verify = CreateDbContext();
        Assert.True(await verify.Appointments.AnyAsync(value => value.Id == appointment.Id));
        Assert.True(await verify.AppointmentStatusHistory.AnyAsync(
            value => value.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task Migration_CreatesSchedulingSchemaStableValuesIndexesAndRestrictedForeignKeys()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        var tables = await ReadStringsAsync(
            connection,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'scheduling' ORDER BY table_name;");
        Assert.Equal(
            [
                "appointment_reschedule_history",
                "appointment_status_history",
                "appointments",
                "availability_slots",
                "demo_availability_imports"
            ],
            tables);

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                "SELECT indexname, indexdef FROM pg_indexes " +
                "WHERE schemaname = 'scheduling' AND indexname IN " +
                "('ux_appointments_reserving_slot'," +
                "'ux_appointments_account_idempotency_key'," +
                "'ux_appointment_status_history_appointment_sequence'," +
                "'ix_appointments_patient_start_status'," +
                "'ix_appointments_slot_status'," +
                "'ix_appointments_status'," +
                "'ix_availability_slots_doctor_published_start'," +
                "'ix_availability_slots_clinic_published_start'," +
                "'ix_availability_slots_location_start'," +
                "'ix_appointment_reschedule_history_appointment_occurred_id') " +
                "ORDER BY indexname;";
            var indexes = new Dictionary<string, string>();
            await using var reader = await indexCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0), reader.GetString(1));
            }

            Assert.Equal(10, indexes.Count);
            Assert.Contains("UNIQUE", indexes["ux_appointments_reserving_slot"]);
            Assert.Contains(
                "WHERE ((status)::text = ANY ((ARRAY['requested'::character varying, " +
                "'confirmed'::character varying])::text[]))",
                indexes["ux_appointments_reserving_slot"]);
            Assert.Contains("UNIQUE", indexes["ux_appointments_account_idempotency_key"]);
            Assert.Contains(
                "UNIQUE",
                indexes["ux_appointment_status_history_appointment_sequence"]);
            Assert.Contains(
                "appointment_id, occurred_at, id",
                indexes["ix_appointment_reschedule_history_appointment_occurred_id"]);
        }

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText =
            "SELECT rc.delete_rule FROM information_schema.table_constraints tc " +
            "JOIN information_schema.referential_constraints rc " +
            "ON rc.constraint_schema = tc.constraint_schema " +
            "AND rc.constraint_name = tc.constraint_name " +
            "WHERE tc.constraint_type = 'FOREIGN KEY' " +
            "AND tc.table_schema = 'scheduling';";
        var deleteRules = new List<string>();
        await using var foreignKeyReader = await foreignKeyCommand.ExecuteReaderAsync();
        while (await foreignKeyReader.ReadAsync())
        {
            deleteRules.Add(foreignKeyReader.GetString(0));
        }

        Assert.Equal(12, deleteRules.Count);
        Assert.All(deleteRules, rule => Assert.Equal("RESTRICT", rule));
    }

    private async Task AssertReservationConflictAsync(Appointment appointment)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Appointments.Add(appointment);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync());
        AssertUniqueViolation(exception, "ux_appointments_reserving_slot");
    }

    private static void AssertUniqueViolation(DbUpdateException exception, string constraintName)
    {
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    private static async Task AssertForeignKeyDeleteFailureAsync(
        NpgsqlConnection connection,
        string table,
        Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    private static async Task<List<string>> ReadStringsAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task SaveGraphAsync(SchedulingGraph graph)
    {
        await using var dbContext = CreateDbContext();
        AddGraph(dbContext, graph);
        await dbContext.SaveChangesAsync();
    }

    private async Task SaveAppointmentAsync(Appointment appointment)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Appointments.Add(appointment);
        await dbContext.SaveChangesAsync();
    }

    private static void AddGraph(BeeexyDbContext dbContext, SchedulingGraph graph)
    {
        dbContext.AddRange(
            graph.Account,
            graph.Patient,
            graph.Clinic,
            graph.Location,
            graph.Doctor,
            graph.Affiliation);
        dbContext.AvailabilitySlots.AddRange(graph.Slots);
    }

    private static SchedulingGraph CreateGraph(int slotCount)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"scheduling-{suffix}@example.test"),
            CreatedAt);
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-SCHED-{suffix}"),
            CreatedAt,
            account.Id);
        var clinic = Clinic.Create(
            DirectoryCode.Create($"scheduling-clinic-{suffix}"),
            DirectoryName.Create("Synthetic scheduling clinic"),
            true,
            CreatedAt);
        var location = ClinicLocation.Create(
            clinic.Id,
            DirectoryName.Create("Synthetic scheduling location"),
            "Lima",
            "Lima",
            "Peru",
            IanaTimeZone.Create("America/Lima"),
            true,
            CreatedAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"scheduling-doctor-{suffix}"),
            DirectoryName.Create("Synthetic scheduling doctor"),
            true,
            CreatedAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id,
            clinic.Id,
            location.Id,
            true,
            CreatedAt);
        var slots = Enumerable.Range(0, slotCount)
            .Select(index => AvailabilitySlot.Create(
                doctor.Id,
                clinic.Id,
                location.Id,
                CreatedAt.AddDays(1).AddHours(index),
                CreatedAt.AddDays(1).AddHours(index).AddMinutes(30),
                IanaTimeZone.Create("America/Lima"),
                AppointmentModality.InPerson,
                true,
                CreatedAt))
            .ToArray();
        return new SchedulingGraph(
            account,
            patient,
            clinic,
            location,
            doctor,
            affiliation,
            slots);
    }

    private static Appointment CreateAppointment(
        SchedulingGraph graph,
        AvailabilitySlot slot,
        AppointmentReason? reason = null,
        EntityId? idempotencyKey = null)
    {
        return Appointment.Create(
            graph.Patient.Id,
            slot,
            graph.Account.Id,
            slot.Modality,
            reason,
            idempotencyKey ?? EntityId.New(),
            AppointmentRequestFingerprint.Create(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            CreatedAt);
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task InitializeAsync()
    {
        await EnsureMigratedAsync();
        await DeleteSchedulingFixturesAsync();
    }

    public Task DisposeAsync() => DeleteSchedulingFixturesAsync();

    private async Task DeleteSchedulingFixturesAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            TRUNCATE TABLE
                scheduling.demo_availability_imports,
                scheduling.appointment_reschedule_history,
                scheduling.appointment_status_history,
                scheduling.appointments,
                scheduling.availability_slots;

            DELETE FROM directory.doctor_affiliations
            WHERE doctor_id IN (
                SELECT id FROM directory.doctors WHERE code LIKE 'scheduling-doctor-%');

            DELETE FROM directory.doctors WHERE code LIKE 'scheduling-doctor-%';

            DELETE FROM directory.clinic_locations
            WHERE clinic_id IN (
                SELECT id FROM directory.clinics WHERE code LIKE 'scheduling-clinic-%');

            DELETE FROM directory.clinics WHERE code LIKE 'scheduling-clinic-%';

            DELETE FROM patients.patient_profiles
            WHERE account_id IN (
                SELECT id FROM identity.accounts
                WHERE normalized_email LIKE 'scheduling-%@example.test');

            DELETE FROM identity.accounts
            WHERE normalized_email LIKE 'scheduling-%@example.test';
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record SchedulingGraph(
        Account Account,
        PatientProfile Patient,
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        AvailabilitySlot[] Slots);
}
