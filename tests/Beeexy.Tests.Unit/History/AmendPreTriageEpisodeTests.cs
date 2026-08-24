using Beeexy.Application.Common;
using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.History;

public sealed class AmendPreTriageEpisodeTests
{
    [Fact]
    public async Task PrimaryPatientCreatesServerControlledTraceableAmendment()
    {
        var fixture = new Fixture();
        var key = Guid.NewGuid();

        var result = await fixture.ExecuteAsync(
            fixture.Source.HistoryEvent.SourceId,
            key.ToString(),
            "  Correct patient-reported duration  ");

        var amendment = result.Amendment;
        Assert.Equal(fixture.Profiles.Account.Id, amendment.AuthorAccountId);
        Assert.Equal(fixture.Clock.UtcNow, amendment.CreatedAt);
        Assert.Equal(EntityId.From(key), amendment.IdempotencyKey);
        Assert.Equal("Correct patient-reported duration", amendment.Reason.Value);
        Assert.Equal(fixture.Source.HistoryEvent.Id, amendment.ClinicalHistoryEventId);
        Assert.Equal(fixture.Source.HistoryEvent.SourceReference,
            amendment.SourceReference);
        Assert.Equal(fixture.Source.HistoryEvent.SourceProvenance,
            amendment.SourceProvenance);
        Assert.Equal(fixture.Profiles.PrimaryProfile.BeeexyId.Value,
            result.AuthorBeeexyId);
        Assert.Single(fixture.Repository.Created);
        Assert.Single(fixture.Audit.CreatedAmendmentIds, amendment.Id);
    }

    [Fact]
    public async Task ActiveManagerIsActualAuthorAndRevokedManagerIsConcealed()
    {
        var fixture = new Fixture(patientId: EntityId.New());
        var relationshipId = EntityId.New();
        fixture.Authorization.Set(
            fixture.Source.PatientProfileId,
            targetExists: true,
            relationshipId);

        var created = await fixture.ExecuteAsync(
            fixture.Source.HistoryEvent.SourceId,
            Guid.NewGuid().ToString(),
            "Manager correction reason");

        Assert.Equal(fixture.Profiles.Account.Id, created.Amendment.AuthorAccountId);
        Assert.Equal(PatientAccessReason.Managed, fixture.Audit.LastAccessReason);

        fixture.Authorization.Set(
            fixture.Source.PatientProfileId,
            targetExists: true,
            relationshipId: null);
        var failure = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.ExecuteAsync(
                fixture.Source.HistoryEvent.SourceId,
                Guid.NewGuid().ToString(),
                "Another reason"));

        Assert.NotNull(failure);
        Assert.Single(fixture.Repository.Created);
    }

    [Fact]
    public async Task MissingSourceAndDeniedPatientHaveOneConcealedFailure()
    {
        var missing = new Fixture();
        missing.Repository.Source = null;
        var denied = new Fixture(patientId: EntityId.New());
        denied.Authorization.Set(denied.Source.PatientProfileId, true, null);

        var missingFailure = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            missing.ExecuteAsync(EntityId.New(), null, null, unsupported: true));
        var deniedFailure = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            denied.ExecuteAsync(
                denied.Source.HistoryEvent.SourceId,
                null,
                null,
                unsupported: true));

        Assert.Equal(missingFailure.Message, deniedFailure.Message);
        Assert.Empty(missing.Repository.Created);
        Assert.Empty(denied.Repository.Created);
    }

    [Theory]
    [InlineData(null, "reason", false, "clinical_amendment.invalid_idempotency_key")]
    [InlineData("not-a-uuid", "reason", false,
        "clinical_amendment.invalid_idempotency_key")]
    [InlineData("00000000-0000-0000-0000-000000000000", "reason", false,
        "clinical_amendment.invalid_idempotency_key")]
    [InlineData("83ba528c-6cd5-44a2-adc0-d717f1bfc670", null, false,
        "clinical_amendment.invalid_reason")]
    [InlineData("83ba528c-6cd5-44a2-adc0-d717f1bfc670", "  ", false,
        "clinical_amendment.invalid_reason")]
    [InlineData("83ba528c-6cd5-44a2-adc0-d717f1bfc670", "reason", true,
        "clinical_amendment.unsupported_fields")]
    public async Task InvalidSupportedRequestShapeFailsSafelyWithoutWrite(
        string? key,
        string? reason,
        bool unsupported,
        string expectedCode)
    {
        var fixture = new Fixture();

        var failure = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ExecuteAsync(
                fixture.Source.HistoryEvent.SourceId,
                key,
                reason,
                unsupported));

        Assert.Equal(expectedCode, failure.Code);
        Assert.Empty(fixture.Repository.Created);
    }

    [Fact]
    public async Task DuplicateConflictIsPropagatedAndAuditedWithoutSecondWrite()
    {
        var fixture = new Fixture();
        var key = Guid.NewGuid().ToString();
        await fixture.ExecuteAsync(
            fixture.Source.HistoryEvent.SourceId,
            key,
            "First reason");
        fixture.Repository.RejectAsDuplicate = true;

        await Assert.ThrowsAsync<ClinicalAmendmentDuplicateException>(() =>
            fixture.ExecuteAsync(
                fixture.Source.HistoryEvent.SourceId,
                key,
                "Changed retry reason"));

        Assert.Single(fixture.Repository.Created);
        Assert.Equal(1, fixture.Audit.DuplicateCount);
    }

    private sealed class Fixture
    {
        public Fixture(EntityId? patientId = null)
        {
            var targetPatient = patientId ?? Profiles.PrimaryProfile.Id;
            Source = CreateSource(targetPatient);
            Repository.Source = Source;
            UseCase = new AmendPreTriageEpisode(
                Clock,
                Profiles.Resolver,
                new AuthorizePatientAccess(
                    Clock,
                    Profiles.Resolver,
                    Authorization,
                    Profiles.MyCircleAudit),
                Repository,
                Audit);
        }

        public FakeClock Clock { get; } = new();

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeAuthorizationRepository Authorization { get; } = new();

        public FakeRepository Repository { get; } = new();

        public FakeAuditLogger Audit { get; } = new();

        public AmendablePreTriageSource Source { get; }

        public AmendPreTriageEpisode UseCase { get; }

        public Task<AmendPreTriageEpisodeResult> ExecuteAsync(
            EntityId episodeId,
            string? key,
            string? reason,
            bool unsupported = false) =>
            UseCase.ExecuteAsync(new AmendPreTriageEpisodeCommand(
                episodeId,
                key,
                reason,
                unsupported));

        private static AmendablePreTriageSource CreateSource(EntityId patientId)
        {
            var session = PreTriageSession.CreateForPatient(
                patientId,
                EntityId.New(),
                FakeClock.Now.AddDays(1),
                FakeClock.Now.AddHours(-2));
            var episode = PreTriageEpisode.CreateFrom(
                session,
                EntityId.New(),
                FakeClock.Now.AddHours(-1));
            var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
                episode,
                FakeClock.Now.AddMinutes(-30));
            return new AmendablePreTriageSource(patientId, historyEvent);
        }
    }

    private sealed class FakeRepository : IPreTriageAmendmentRepository
    {
        public AmendablePreTriageSource? Source { get; set; }

        public bool RejectAsDuplicate { get; set; }

        public List<ClinicalAmendment> Created { get; } = [];

        public async Task<ClinicalAmendment?> CreateLockedAsync(
            EntityId episodeId,
            Func<AmendablePreTriageSource, Task<ClinicalAmendment>> createAmendment,
            CancellationToken cancellationToken = default)
        {
            if (Source is null || Source.HistoryEvent.SourceId != episodeId)
            {
                return null;
            }

            var amendment = await createAmendment(Source);
            if (RejectAsDuplicate)
            {
                throw new ClinicalAmendmentDuplicateException();
            }

            Created.Add(amendment);
            return amendment;
        }
    }

    private sealed class FakeAuthorizationRepository
        : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> _values = [];

        public void Set(EntityId patientId, bool targetExists, EntityId? relationshipId) =>
            _values[patientId] = new PatientAccessAuthorizationLookup(
                targetExists,
                relationshipId);

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(
                targetProfileId,
                new PatientAccessAuthorizationLookup(false, null)));
    }

    private sealed class FakeAuditLogger : IClinicalAmendmentAuditLogger
    {
        public List<EntityId> CreatedAmendmentIds { get; } = [];

        public PatientAccessReason? LastAccessReason { get; private set; }

        public int DuplicateCount { get; private set; }

        public void Created(
            EntityId actorAccountId,
            EntityId historyEventId,
            EntityId sourceEpisodeId,
            EntityId amendmentId,
            PatientAccessReason accessReason,
            DateTimeOffset createdAt)
        {
            CreatedAmendmentIds.Add(amendmentId);
            LastAccessReason = accessReason;
        }

        public void DuplicateRejected(
            EntityId actorAccountId,
            EntityId sourceEpisodeId,
            PatientAccessReason? accessReason,
            DateTimeOffset rejectedAt) => DuplicateCount++;
    }

    private sealed class FakeClock : IClock
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Now;
    }
}
