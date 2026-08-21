using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public sealed class GetPrimaryProfile(CurrentAccountProfileResolver resolver)
{
    public async Task<PrimaryProfileResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await resolver.ResolveAsync(cancellationToken);
        return ToResult(current);
    }

    internal static PrimaryProfileResult ToResult(ResolvedCurrentAccountProfile current)
    {
        return new PrimaryProfileResult(
            current.PrimaryProfile.Id,
            current.PrimaryProfile.BeeexyId.Value,
            current.PrimaryProfile.FirstName?.Value,
            current.PrimaryProfile.LastName?.Value,
            current.PrimaryProfile.DateOfBirth,
            current.PrimaryProfile.SexAssignedAtBirth,
            current.PrimaryProfile.State?.Code,
            current.PrimaryProfile.Version,
            current.Preference.TimeZone.Value,
            current.Preference.Version);
    }
}

public sealed record PrimaryProfileResult(
    EntityId ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    Beeexy.Domain.Patients.SexAssignedAtBirth? SexAssignedAtBirth,
    string? State,
    long ProfileVersion,
    string Timezone,
    long Version);
