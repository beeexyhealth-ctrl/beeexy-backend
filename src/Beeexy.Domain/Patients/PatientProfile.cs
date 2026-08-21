using Beeexy.Domain.Common;

namespace Beeexy.Domain.Patients;

public sealed class PatientProfile
{
    private PatientProfile()
    {
        BeeexyId = null!;
    }

    private PatientProfile(
        EntityId id,
        EntityId? accountId,
        BeeexyId beeexyId,
        PatientName? firstName,
        PatientName? lastName,
        DateOnly? dateOfBirth,
        SexAssignedAtBirth? sexAssignedAtBirth,
        UsState? state,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        BeeexyId = beeexyId;
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        SexAssignedAtBirth = sexAssignedAtBirth;
        State = state;
        Version = 1;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId? AccountId { get; private set; }

    public BeeexyId BeeexyId { get; private set; }

    public PatientName? FirstName { get; private set; }

    public PatientName? LastName { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public SexAssignedAtBirth? SexAssignedAtBirth { get; private set; }

    public UsState? State { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static PatientProfile Create(
        BeeexyId beeexyId,
        DateTimeOffset createdAt,
        EntityId? accountId = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(beeexyId);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));

        return new PatientProfile(
            id ?? EntityId.New(),
            accountId,
            beeexyId,
            null,
            null,
            null,
            null,
            null,
            createdAt);
    }

    public static PatientProfile CreateManaged(
        BeeexyId beeexyId,
        PatientName firstName,
        PatientName lastName,
        DateOnly dateOfBirth,
        SexAssignedAtBirth sexAssignedAtBirth,
        UsState state,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(beeexyId);
        ArgumentNullException.ThrowIfNull(firstName);
        ArgumentNullException.ThrowIfNull(lastName);
        ArgumentNullException.ThrowIfNull(state);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        EnsureValidDateOfBirth(dateOfBirth, createdAt);
        EnsureValidSexAssignedAtBirth(sexAssignedAtBirth);

        return new PatientProfile(
            id ?? EntityId.New(),
            null,
            beeexyId,
            firstName,
            lastName,
            dateOfBirth,
            sexAssignedAtBirth,
            state,
            createdAt);
    }

    public IReadOnlyList<string> UpdateDemographics(
        PatientName? firstName,
        PatientName? lastName,
        DateOnly? dateOfBirth,
        SexAssignedAtBirth? sexAssignedAtBirth,
        UsState? state,
        DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        if (dateOfBirth.HasValue)
        {
            EnsureValidDateOfBirth(dateOfBirth.Value, updatedAt);
        }

        if (sexAssignedAtBirth.HasValue)
        {
            EnsureValidSexAssignedAtBirth(sexAssignedAtBirth.Value);
        }

        var changedFields = new List<string>(5);
        if (firstName is not null && FirstName != firstName)
        {
            FirstName = firstName;
            changedFields.Add("firstName");
        }

        if (lastName is not null && LastName != lastName)
        {
            LastName = lastName;
            changedFields.Add("lastName");
        }

        if (dateOfBirth.HasValue && DateOfBirth != dateOfBirth)
        {
            DateOfBirth = dateOfBirth;
            changedFields.Add("dateOfBirth");
        }

        if (sexAssignedAtBirth.HasValue && SexAssignedAtBirth != sexAssignedAtBirth)
        {
            SexAssignedAtBirth = sexAssignedAtBirth;
            changedFields.Add("sexAssignedAtBirth");
        }

        if (state is not null && State != state)
        {
            State = state;
            changedFields.Add("state");
        }

        if (changedFields.Count > 0)
        {
            Version = checked(Version + 1);
            UpdatedAt = updatedAt;
        }

        return changedFields;
    }

    private static void EnsureValidDateOfBirth(
        DateOnly dateOfBirth,
        DateTimeOffset referenceTime)
    {
        var referenceDate = DateOnly.FromDateTime(referenceTime.UtcDateTime);
        if (dateOfBirth > referenceDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateOfBirth),
                "The date of birth cannot be in the future.");
        }
    }

    private static void EnsureValidSexAssignedAtBirth(SexAssignedAtBirth value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The sex assigned at birth is not supported.");
        }
    }

}
