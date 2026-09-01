using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Tests.Unit.Scheduling;

[Trait("Category", "Phase8Acceptance")]
public sealed class ListAvailableSlotsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
    private static readonly EntityId DoctorId =
        EntityId.From(Guid.Parse("82000000-0000-4000-8000-000000000001"));

    [Fact]
    public async Task OmittedRange_UsesClockControlledNextThirtyDays()
    {
        var repository = new RecordingSlotRepository();
        var useCase = CreateUseCase(repository);

        await useCase.ExecuteAsync(DoctorId, new(null, null));

        Assert.Equal(Now, repository.From);
        Assert.Equal(Now.AddDays(30), repository.To);
        Assert.Equal(Now, repository.FutureCutoff);
    }

    [Fact]
    public async Task ExplicitRange_NormalizesOffsetsAndPassesCancellationToken()
    {
        var repository = new RecordingSlotRepository();
        var useCase = CreateUseCase(repository);
        using var source = new CancellationTokenSource();
        var from = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var to = from.AddDays(90);

        await useCase.ExecuteAsync(DoctorId, new(from, to), source.Token);

        Assert.Equal(from.ToUniversalTime(), repository.From);
        Assert.Equal(to.ToUniversalTime(), repository.To);
        Assert.Equal(source.Token, repository.CancellationToken);
    }

    [Fact]
    public async Task SingleBoundary_ResolvesAThirtyDayWindow()
    {
        var repository = new RecordingSlotRepository();
        var useCase = CreateUseCase(repository);
        var from = Now.AddDays(2);

        await useCase.ExecuteAsync(DoctorId, new(from, null));

        Assert.Equal(from, repository.From);
        Assert.Equal(from.AddDays(30), repository.To);
    }

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public async Task InvalidOrOverLimitRange_IsRejected(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var useCase = CreateUseCase(new RecordingSlotRepository());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(DoctorId, new(from, to)));

        Assert.Equal("availability.range_invalid", exception.Code);
    }

    [Fact]
    public async Task OmittedEndThatWouldOverflow_IsRejectedAsValidation()
    {
        var useCase = CreateUseCase(new RecordingSlotRepository());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(DoctorId, new(DateTimeOffset.MaxValue, null)));

        Assert.Equal("availability.range_invalid", exception.Code);
    }

    [Fact]
    public async Task PastRange_ReturnsEmptyAfterVerifyingDoctorWithoutQueryingSlots()
    {
        var repository = new RecordingSlotRepository();
        var useCase = CreateUseCase(repository);

        var result = await useCase.ExecuteAsync(
            DoctorId,
            new(Now.AddDays(-2), Now.AddDays(-1)));

        Assert.Empty(result);
        Assert.Null(repository.From);
    }

    [Fact]
    public async Task UnknownOrConcealedDoctor_UsesDirectoryNotFoundSemantics()
    {
        var useCase = new ListAvailableSlots(
            new StubDoctorRepository(exists: false),
            new RecordingSlotRepository(),
            new StubClock(Now));

        await Assert.ThrowsAsync<DoctorNotFoundException>(() =>
            useCase.ExecuteAsync(DoctorId, new(null, null)));
    }

    public static TheoryData<DateTimeOffset, DateTimeOffset> InvalidRanges => new()
    {
        { Now, Now },
        { Now.AddMinutes(1), Now },
        { Now, Now.AddDays(90).AddTicks(1) },
        { DateTimeOffset.MaxValue, DateTimeOffset.MaxValue }
    };

    private static ListAvailableSlots CreateUseCase(RecordingSlotRepository repository) =>
        new(new StubDoctorRepository(exists: true), repository, new StubClock(Now));

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingSlotRepository : IAvailabilitySlotReadRepository
    {
        public DateTimeOffset? From { get; private set; }

        public DateTimeOffset? To { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public DateTimeOffset? FutureCutoff { get; private set; }

        public Task<IReadOnlyList<AvailableSlot>> ListAvailableAsync(
            EntityId doctorId,
            DateTimeOffset from,
            DateTimeOffset to,
            DateTimeOffset futureCutoff,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(DoctorId, doctorId);
            From = from;
            To = to;
            FutureCutoff = futureCutoff;
            CancellationToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<AvailableSlot>>([]);
        }
    }

    private sealed class StubDoctorRepository(bool exists) : IDoctorDirectoryReadRepository
    {
        public Task<DoctorDirectoryProfile?> GetAsync(
            EntityId doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(exists
                ? new DoctorDirectoryProfile(
                    doctorId,
                    "test-doctor",
                    "Synthetic test doctor",
                    [],
                    [],
                    [],
                    [],
                    [])
                : null);

        public Task<bool> CursorExistsAsync(
            DoctorDirectoryPageCursor cursor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DoctorDirectoryProfile>> SearchAsync(
            DoctorDirectoryFilter filter,
            DoctorDirectoryPageCursor? after,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EntityId>> ListFilteredDoctorIdsAsync(
            DoctorDirectoryFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DoctorDirectoryProfile>> GetManyAsync(
            IReadOnlyList<EntityId> doctorIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
