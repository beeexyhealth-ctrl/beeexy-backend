using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

internal sealed class MyCircleListingTestFixture
{
    public static readonly DateTimeOffset Now =
        new(2026, 8, 20, 20, 0, 0, TimeSpan.Zero);

    public MyCircleListingTestFixture()
    {
        Account = Account.Create(
            NormalizedEmail.Create($"circle-{Guid.NewGuid():N}@example.com"),
            Now);
        PrimaryProfile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            Now,
            Account.Id);
        Preference = UserPreference.Create(
            Account.Id,
            UserTimeZone.Create("Etc/UTC"),
            Now);
        CurrentRepository = new FakeCurrentAccountProfileRepository(
            new CurrentAccountProfileState(Account, [PrimaryProfile], [Preference]));
        ProfileAudit = new FakeAccountProfileAuditLogger();
        Resolver = new CurrentAccountProfileResolver(
            new FakeCurrentSessionIdentity(Account.Id),
            CurrentRepository,
            ProfileAudit);
        MyCircleAudit = new FakeMyCircleAuditLogger();
    }

    public Account Account { get; }

    public PatientProfile PrimaryProfile { get; }

    public UserPreference Preference { get; }

    public FakeCurrentAccountProfileRepository CurrentRepository { get; }

    public FakeAccountProfileAuditLogger ProfileAudit { get; }

    public FakeMyCircleReadRepository ReadRepository { get; } = new();

    public FakeMyCircleAuditLogger MyCircleAudit { get; }

    public CurrentAccountProfileResolver Resolver { get; }

    public ListAccessiblePatients CreateAccessiblePatientsUseCase() =>
        new(Resolver, ReadRepository, MyCircleAudit);

    public ListCareRelationships CreateCareRelationshipsUseCase() =>
        new(Resolver, ReadRepository);

    public ManagedPatientAccessRecord ManagedPatient(
        int order,
        CareRelationshipStatus status = CareRelationshipStatus.Active,
        EntityId? profileId = null,
        EntityId? relationshipId = null)
    {
        var resolvedProfileId = profileId ?? EntityId.New();
        return new ManagedPatientAccessRecord(
            resolvedProfileId,
            $"BXY-{resolvedProfileId.Value:N}".ToUpperInvariant(),
            relationshipId ?? EntityId.New(),
            CareRelationshipType.Child,
            status,
            Now.AddMinutes(order));
    }

    public CareRelationshipListRecord Relationship(
        int order,
        CareRelationshipStatus status = CareRelationshipStatus.Active,
        EntityId? relationshipId = null)
    {
        var subjectId = EntityId.New();
        return new CareRelationshipListRecord(
            relationshipId ?? EntityId.New(),
            subjectId,
            $"BXY-{subjectId.Value:N}".ToUpperInvariant(),
            CareRelationshipType.Caregiver,
            status,
            "phase-3.3-test",
            Now.AddMinutes(order),
            Now.AddMinutes(order),
            status == CareRelationshipStatus.Revoked
                ? Now.AddMinutes(order + 1)
                : null);
    }

    internal sealed class FakeMyCircleReadRepository : IMyCircleReadRepository
    {
        public Dictionary<EntityId, IReadOnlyList<ManagedPatientAccessRecord>>
            ManagedPatientsByManager
        { get; } = [];

        public Dictionary<EntityId, IReadOnlyList<CareRelationshipListRecord>>
            RelationshipsByManager
        { get; } = [];

        public EntityId? RequestedManagedPatientManagerId { get; private set; }

        public EntityId? RequestedRelationshipManagerId { get; private set; }

        public Task<IReadOnlyList<ManagedPatientAccessRecord>>
            ListActiveManagedPatientsAsync(
                EntityId managerProfileId,
                CancellationToken cancellationToken = default)
        {
            RequestedManagedPatientManagerId = managerProfileId;
            return Task.FromResult(
                ManagedPatientsByManager.GetValueOrDefault(
                    managerProfileId,
                    Array.Empty<ManagedPatientAccessRecord>()));
        }

        public Task<IReadOnlyList<CareRelationshipListRecord>> ListRelationshipsAsync(
            EntityId managerProfileId,
            CancellationToken cancellationToken = default)
        {
            RequestedRelationshipManagerId = managerProfileId;
            return Task.FromResult(
                RelationshipsByManager.GetValueOrDefault(
                    managerProfileId,
                    Array.Empty<CareRelationshipListRecord>()));
        }
    }

    internal sealed class FakeCurrentAccountProfileRepository(CurrentAccountProfileState state)
        : ICurrentAccountProfileRepository
    {
        public CurrentAccountProfileState State { get; set; } = state;

        public Task<CurrentAccountProfileState> LoadAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class FakeAccountProfileAuditLogger : IAccountProfileAuditLogger
    {
        public List<string> InvariantNames { get; } = [];

        public void InvariantViolation(EntityId accountId, string invariant) =>
            InvariantNames.Add(invariant);

        public void ProfileUpdateSucceeded(
            EntityId accountId,
            EntityId profileId,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt)
        {
        }

        public void ProfileUpdateConflict(EntityId accountId, EntityId profileId)
        {
        }
    }

    internal sealed class FakeMyCircleAuditLogger : IMyCircleAuditLogger
    {
        public List<EntityId> DuplicateSubjectIds { get; } = [];

        public void DuplicateAccessiblePatientDetected(
            EntityId accountId,
            EntityId managerProfileId,
            EntityId subjectProfileId) => DuplicateSubjectIds.Add(subjectProfileId);
    }

    private sealed class FakeCurrentSessionIdentity(EntityId accountId)
        : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }
}
