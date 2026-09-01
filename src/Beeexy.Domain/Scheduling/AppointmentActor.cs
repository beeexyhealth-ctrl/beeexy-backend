using Beeexy.Domain.Common;

namespace Beeexy.Domain.Scheduling;

public sealed record AppointmentActor
{
    public const int MaximumOperationalIdentifierLength = 128;

    private AppointmentActor(
        AppointmentActorType type,
        EntityId? accountId,
        string? operationalIdentifier)
    {
        Type = type;
        AccountId = accountId;
        OperationalIdentifier = operationalIdentifier;
    }

    public AppointmentActorType Type { get; }

    public EntityId? AccountId { get; }

    public string? OperationalIdentifier { get; }

    public static AppointmentActor PatientAuthority(EntityId accountId) =>
        AccountActor(AppointmentActorType.PatientAuthority, accountId);

    public static AppointmentActor AppointmentScheduler(EntityId accountId) =>
        AccountActor(AppointmentActorType.AppointmentScheduler, accountId);

    public static AppointmentActor BeeexyOperations(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var normalized = identifier.Trim();
        if (normalized.Length > MaximumOperationalIdentifierLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The operational actor identifier must contain 1 to " +
                $"{MaximumOperationalIdentifierLength} non-control characters.",
                nameof(identifier));
        }

        return new AppointmentActor(
            AppointmentActorType.BeeexyOperations,
            null,
            normalized);
    }

    private static AppointmentActor AccountActor(
        AppointmentActorType type,
        EntityId accountId)
    {
        if (accountId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "An actor account identifier cannot be empty.",
                nameof(accountId));
        }

        return new AppointmentActor(type, accountId, null);
    }
}
