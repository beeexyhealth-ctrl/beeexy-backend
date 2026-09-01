using Beeexy.Api.Operations;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;
using Microsoft.Extensions.Configuration;

namespace Beeexy.Tests.Unit.Scheduling;

[Trait("Category", "Phase8Ops")]
public sealed class AppointmentOperationsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Production")]
    public void EnvironmentAllowlist_AcceptsDevelopmentAndProduction(string environment) =>
        Assert.True(AppointmentAdministrationCli.IsAllowedEnvironment(environment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void EnvironmentAllowlist_RejectsMissingAndUnsupportedValues(string? environment) =>
        Assert.False(AppointmentAdministrationCli.IsAllowedEnvironment(environment));

    [Fact]
    public async Task MissingEnvironment_ReturnsConfigurationExitCodeWithoutDatabaseAccess()
    {
        var error = new StringWriter();

        var exitCode = await AppointmentAdministrationCli.ExecuteAsync(
            [AppointmentAdministrationCli.ListCommand, "--clinic", Guid.NewGuid().ToString("D")],
            new ConfigurationManager(),
            environmentName: null,
            error: error);

        Assert.Equal(AppointmentAdministrationCli.ConfigurationExitCode, exitCode);
        Assert.Contains("explicitly set", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_RequiresNonblankOperationalActor()
    {
        var appointmentId = Guid.NewGuid();

        var exception = Assert.ThrowsAny<Exception>(() =>
            AppointmentAdministrationCli.Parse([
                AppointmentAdministrationCli.ConfirmCommand,
                appointmentId.ToString("D"),
                "--actor",
                "   "]));

        Assert.Contains("actor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectParser_AcceptsExplicitYesSafeguard()
    {
        var options = Assert.IsType<AppointmentAdministrationCli.MutationOptions>(
            AppointmentAdministrationCli.Parse([
                AppointmentAdministrationCli.RejectCommand,
                Guid.NewGuid().ToString("D"),
                "--actor",
                "ops@example.test",
                "--yes"]));

        Assert.True(options.Reject);
        Assert.True(options.Yes);
        Assert.Equal("ops@example.test", options.Actor);
    }

    [Fact]
    public async Task EmptyList_PrintsClearSuccessMessage()
    {
        var clinicId = Guid.NewGuid();
        var output = new StringWriter();

        await AppointmentAdministrationCli.WriteListAsync(output, clinicId, []);

        Assert.Equal(
            $"No requested appointments found for clinic {clinicId:D}." +
            Environment.NewLine,
            output.ToString());
    }

    [Fact]
    public async Task ListOutput_ContainsOnlySafeOperationalFields()
    {
        var clinicId = EntityId.New();
        var output = new StringWriter();
        var item = new OperationalAppointmentSummary(
            EntityId.New(), clinicId, "Dr. Safe", Now.AddHours(1), Now.AddHours(1.5),
            "America/Lima", AppointmentModality.InPerson, AppointmentStatus.Requested, Now);

        await AppointmentAdministrationCli.WriteListAsync(output, clinicId.Value, [item]);

        var value = output.ToString();
        Assert.Contains("AppointmentId | ClinicId | Doctor | StartsAt | EndsAt | " +
            "ClinicTimeZone | Modality | Status | CreatedAt", value, StringComparison.Ordinal);
        Assert.DoesNotContain("Reason", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Patient", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Triage", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Version", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestedList_EnforcesClinicAndLimitBoundary()
    {
        var repository = new CapturingOperationsRepository();
        var useCase = new ListRequestedAppointmentsForOperations(repository);
        var clinicId = EntityId.New();

        await useCase.ExecuteAsync(clinicId, 200);

        Assert.Equal(clinicId, repository.ClinicId);
        Assert.Equal(200, repository.Limit);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.ExecuteAsync(clinicId, 201));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationalTransition_ReusesLifecycleAndAuditsActorOnce(bool confirm)
    {
        var state = CreateState();
        var transaction = new FakeTransaction(state);
        var engine = new AppointmentTransitionEngine(new StubClock(Now), transaction);
        const string actor = "beeexy-ops@example.test";

        var first = await engine.ExecuteAsync(
            state.Appointment.Id,
            confirm ? AppointmentStatus.Confirmed : AppointmentStatus.Rejected,
            AppointmentActor.BeeexyOperations(actor),
            authorizeClinic: null,
            CancellationToken.None);
        var second = await engine.ExecuteAsync(
            state.Appointment.Id,
            confirm ? AppointmentStatus.Confirmed : AppointmentStatus.Rejected,
            AppointmentActor.BeeexyOperations(actor),
            authorizeClinic: null,
            CancellationToken.None);

        Assert.True(first.NewlyApplied);
        Assert.False(second.NewlyApplied);
        Assert.Equal(2, state.Appointment.StatusHistory.Count);
        var audit = state.Appointment.StatusHistory.Single(value => value.Sequence == 2);
        Assert.Equal(AppointmentActorType.BeeexyOperations, audit.ActorType);
        Assert.Null(audit.ActorAccountId);
        Assert.Equal(actor, audit.OperationalActorIdentifier);
        Assert.Equal(confirm, state.Appointment.ReservesSlot);
    }

    [Fact]
    public async Task OperationalTransition_RejectsIncompatibleLifecycle()
    {
        var state = CreateState();
        var engine = new AppointmentTransitionEngine(
            new StubClock(Now),
            new FakeTransaction(state));
        await engine.ExecuteAsync(
            state.Appointment.Id,
            AppointmentStatus.Confirmed,
            AppointmentActor.BeeexyOperations("operator-a"),
            null,
            CancellationToken.None);

        await Assert.ThrowsAsync<AppointmentTransitionConflictException>(() =>
            engine.ExecuteAsync(
                state.Appointment.Id,
                AppointmentStatus.Rejected,
                AppointmentActor.BeeexyOperations("operator-a"),
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task OperationalTransition_HandlesMissingAndConcurrencyConflict()
    {
        var state = CreateState();
        var transaction = new FakeTransaction(state) { Missing = true };
        var engine = new AppointmentTransitionEngine(new StubClock(Now), transaction);
        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => engine.ExecuteAsync(
            state.Appointment.Id,
            AppointmentStatus.Confirmed,
            AppointmentActor.BeeexyOperations("operator-a"),
            null,
            CancellationToken.None));

        transaction.Missing = false;
        transaction.ConcurrencyReload = CreateState(state.Appointment.Id);
        transaction.ConcurrencyReload.Appointment.Reject(
            AppointmentActor.BeeexyOperations("operator-b"),
            Now);
        await Assert.ThrowsAsync<AppointmentTransitionConflictException>(() =>
            engine.ExecuteAsync(
                state.Appointment.Id,
                AppointmentStatus.Confirmed,
                AppointmentActor.BeeexyOperations("operator-a"),
                null,
                CancellationToken.None));
    }

    private static AppointmentTransitionState CreateState(EntityId? appointmentId = null)
    {
        var slot = AvailabilitySlot.Create(
            EntityId.New(), EntityId.New(), EntityId.New(),
            Now.AddHours(2), Now.AddHours(2.5),
            IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson, true, Now.AddDays(-1));
        var appointment = Appointment.Create(
            EntityId.New(), slot, EntityId.New(), AppointmentModality.InPerson,
            AppointmentReason.Create("never exposed"), EntityId.New(),
            AppointmentRequestFingerprint.Create(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            Now.AddMinutes(-1), appointmentId);
        return new AppointmentTransitionState(appointment, slot);
    }

    private sealed class CapturingOperationsRepository : IAppointmentOperationsReadRepository
    {
        public EntityId ClinicId { get; private set; }
        public int Limit { get; private set; }

        public Task<IReadOnlyList<OperationalAppointmentSummary>> ListRequestedAsync(
            EntityId clinicId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            ClinicId = clinicId;
            Limit = limit;
            return Task.FromResult<IReadOnlyList<OperationalAppointmentSummary>>([]);
        }

        public Task<OperationalAppointmentSummary?> GetAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationalAppointmentSummary?>(null);
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeTransaction(AppointmentTransitionState state)
        : IAppointmentTransitionTransaction
    {
        public bool Missing { get; set; }
        public AppointmentTransitionState? ConcurrencyReload { get; set; }

        public Task BeginAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AppointmentTransitionState?> LoadAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppointmentTransitionState?>(Missing ? null : state);

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            if (ConcurrencyReload is not null)
            {
                throw new AppointmentTransitionConcurrencyException(
                    new InvalidOperationException("Simulated concurrency."));
            }
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AppointmentTransitionState?> ReloadAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConcurrencyReload);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
