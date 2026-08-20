using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Patients;

public sealed class GetCurrentAccount(CurrentAccountProfileResolver resolver)
{
    public async Task<GetCurrentAccountResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await resolver.ResolveAsync(cancellationToken);
        return new GetCurrentAccountResult(
            current.Account.Id,
            current.Account.Status,
            current.PrimaryProfile.Id,
            current.PrimaryProfile.BeeexyId.Value,
            current.Preference.TimeZone.Value);
    }
}

public sealed record GetCurrentAccountResult(
    EntityId AccountId,
    AccountStatus Status,
    EntityId PrimaryProfileId,
    string BeeexyId,
    string Timezone);
