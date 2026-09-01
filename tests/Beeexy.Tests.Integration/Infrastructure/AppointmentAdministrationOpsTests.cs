using Beeexy.Api.Operations;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase8OpsPostgreSql")]
public sealed class AppointmentAdministrationOpsTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_ReturnsOnlyRequestedClinicInDeterministicBoundedOrder()
    {
        var graph = CreateGraph(slotCount: 4);
        var other = CreateGraph(slotCount: 1);
        var later = CreateAppointment(graph, graph.Slots[2]);
        var earlierHighId = CreateAppointment(
            graph,
            graph.Slots[0],
            EntityId.From(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        var earlierLowId = CreateAppointment(
            graph,
            graph.Slots[1],
            EntityId.From(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        var confirmed = CreateAppointment(graph, graph.Slots[3]);
        confirmed.Confirm(graph.Account.Id, CreatedAt.AddMinutes(1));
        var outsideClinic = CreateAppointment(other, other.Slots[0]);
        await SaveAsync(graph, [later, earlierHighId, earlierLowId, confirmed]);
        await SaveAsync(other, [outsideClinic]);

        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ListRequestedAppointmentsForOperations>()
            .ExecuteAsync(graph.Clinic.Id, limit: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(earlierLowId.Id, result[0].AppointmentId);
        Assert.Equal(earlierHighId.Id, result[1].AppointmentId);
        Assert.All(result, item =>
        {
            Assert.Equal(graph.Clinic.Id, item.ClinicId);
            Assert.Equal(AppointmentStatus.Requested, item.Status);
            Assert.Equal("Operational test doctor", item.Doctor);
        });
        Assert.DoesNotContain(
            typeof(OperationalAppointmentSummary).GetProperties(),
            property => property.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Patient", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Triage", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mutation_PersistsOneOperationalAuditAndCorrectReservation(bool confirm)
    {
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await SaveAsync(graph, [appointment]);
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        const string actor = "production-operator@example.test";

        var first = confirm
            ? await scope.ServiceProvider.GetRequiredService<ConfirmAppointmentForOperations>()
                .ExecuteAsync(appointment.Id, actor)
            : await scope.ServiceProvider.GetRequiredService<RejectAppointmentForOperations>()
                .ExecuteAsync(appointment.Id, actor);
        var retry = confirm
            ? await scope.ServiceProvider.GetRequiredService<ConfirmAppointmentForOperations>()
                .ExecuteAsync(appointment.Id, actor)
            : await scope.ServiceProvider.GetRequiredService<RejectAppointmentForOperations>()
                .ExecuteAsync(appointment.Id, actor);

        Assert.True(first.NewlyApplied);
        Assert.False(retry.NewlyApplied);
        await using (var verification = CreateDbContext())
        {
            var stored = await verification.Appointments
                .AsNoTracking()
                .SingleAsync(value => value.Id == appointment.Id);
            Assert.Equal(
                confirm ? AppointmentStatus.Confirmed : AppointmentStatus.Rejected,
                stored.Status);
            Assert.Equal(confirm, stored.ReservesSlot);
            var history = await verification.AppointmentStatusHistory
                .AsNoTracking()
                .Where(value => value.AppointmentId == appointment.Id)
                .OrderBy(value => value.Sequence)
                .ToArrayAsync();
            Assert.Equal(2, history.Length);
            Assert.Equal(AppointmentActorType.BeeexyOperations, history[1].ActorType);
            Assert.Null(history[1].ActorAccountId);
            Assert.Equal(actor, history[1].OperationalActorIdentifier);
            Assert.Empty(await verification.AppointmentRescheduleHistory
                .Where(value => value.AppointmentId == appointment.Id)
                .ToArrayAsync());
        }

        var replacement = CreateAppointment(graph, graph.Slots[0]);
        await using var reservationCheck = CreateDbContext();
        reservationCheck.Appointments.Add(replacement);
        if (confirm)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                reservationCheck.SaveChangesAsync());
        }
        else
        {
            await reservationCheck.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ConcurrentOppositeTransitions_DoNotOverwriteWinner()
    {
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await SaveAsync(graph, [appointment]);
        await using var provider = CreateProvider();

        async Task<Exception?> RunAsync(bool confirm)
        {
            await using var scope = provider.CreateAsyncScope();
            try
            {
                if (confirm)
                {
                    await scope.ServiceProvider
                        .GetRequiredService<ConfirmAppointmentForOperations>()
                        .ExecuteAsync(appointment.Id, "operator-confirm");
                }
                else
                {
                    await scope.ServiceProvider
                        .GetRequiredService<RejectAppointmentForOperations>()
                        .ExecuteAsync(appointment.Id, "operator-reject");
                }
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(RunAsync(true), RunAsync(false));

        Assert.Single(outcomes, value => value is null);
        Assert.Single(outcomes, value => value is AppointmentTransitionConflictException);
        await using var verification = CreateDbContext();
        Assert.Equal(2, await verification.AppointmentStatusHistory
            .CountAsync(value => value.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task RejectCli_YesBypassesPromptAndEmitsNoClinicalReason()
    {
        var graph = CreateGraph(slotCount: 1);
        var appointment = CreateAppointment(graph, graph.Slots[0]);
        await SaveAsync(graph, [appointment]);
        var output = new StringWriter();
        var error = new StringWriter();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BeeexyDatabase"] = postgres.ConnectionString
            })
            .Build();

        var exitCode = await AppointmentAdministrationCli.ExecuteAsync(
            [AppointmentAdministrationCli.RejectCommand,
                appointment.Id.Value.ToString("D"),
                "--actor", "integration-operator", "--yes"],
            configuration,
            "Development",
            new ThrowingReader(),
            output,
            error);

        Assert.Equal(AppointmentAdministrationCli.SuccessExitCode, exitCode);
        Assert.Contains("Current status: Rejected", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private scheduling reason", output.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reject this appointment?", output.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppointmentOperationsInfrastructure(postgres.ConnectionString);
        services.AddScoped<AppointmentTransitionEngine>();
        services.AddScoped<ListRequestedAppointmentsForOperations>();
        services.AddScoped<GetAppointmentForOperations>();
        services.AddScoped<ConfirmAppointmentForOperations>();
        services.AddScoped<RejectAppointmentForOperations>();
        return services.BuildServiceProvider();
    }

    private async Task SaveAsync(TestGraph graph, Appointment[] appointments)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(
            graph.Account,
            graph.Patient,
            graph.Clinic,
            graph.Location,
            graph.Doctor,
            graph.Affiliation);
        dbContext.AvailabilitySlots.AddRange(graph.Slots);
        dbContext.Appointments.AddRange(appointments);
        await dbContext.SaveChangesAsync();
    }

    private static TestGraph CreateGraph(int slotCount)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"phase8-ops-{suffix}@example.test"), CreatedAt);
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-OPS-{suffix}"), CreatedAt, account.Id);
        var clinic = Clinic.Create(
            DirectoryCode.Create($"phase8-ops-clinic-{suffix}"),
            DirectoryName.Create("Operational test clinic"), true, CreatedAt);
        var location = ClinicLocation.Create(
            clinic.Id, DirectoryName.Create("Operational test location"),
            "Lima", "Lima", "Peru", IanaTimeZone.Create("America/Lima"),
            true, CreatedAt);
        var doctor = Doctor.Create(
            DirectoryCode.Create($"phase8-ops-doctor-{suffix}"),
            DirectoryName.Create("Operational test doctor"), true, CreatedAt);
        var affiliation = DoctorAffiliation.Create(
            doctor.Id, clinic.Id, location.Id, true, CreatedAt);
        var slots = Enumerable.Range(0, slotCount)
            .Select(index => AvailabilitySlot.Create(
                doctor.Id, clinic.Id, location.Id,
                CreatedAt.AddDays(2).AddHours(index / 2),
                CreatedAt.AddDays(2).AddHours(index / 2).AddMinutes(30),
                IanaTimeZone.Create("America/Lima"), AppointmentModality.InPerson,
                true, CreatedAt))
            .ToArray();
        return new TestGraph(account, patient, clinic, location, doctor, affiliation, slots);
    }

    private static Appointment CreateAppointment(
        TestGraph graph,
        AvailabilitySlot slot,
        EntityId? id = null) => Appointment.Create(
            graph.Patient.Id,
            slot,
            graph.Account.Id,
            AppointmentModality.InPerson,
            AppointmentReason.Create("private scheduling reason"),
            EntityId.New(),
            AppointmentRequestFingerprint.Create(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            CreatedAt,
            id);

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private sealed record TestGraph(
        Account Account,
        PatientProfile Patient,
        Clinic Clinic,
        ClinicLocation Location,
        Doctor Doctor,
        DoctorAffiliation Affiliation,
        AvailabilitySlot[] Slots);

    private sealed class ThrowingReader : StringReader
    {
        public ThrowingReader() : base(string.Empty)
        {
        }

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The --yes path must not read input.");
    }
}
