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
            current.Preference.TimeZone.Value,
            current.Preference.Version);
    }
}

public sealed record PrimaryProfileResult(
    EntityId ProfileId,
    string BeeexyId,
    string Timezone,
    long Version);
