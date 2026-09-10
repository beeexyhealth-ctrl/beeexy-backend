using Beeexy.Application.Patients;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Sharing;

public sealed class ListShares(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    IShareReadRepository repository)
{
    public async Task<IReadOnlyList<ShareSummary>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var states = await repository.ListAsync(current.PrimaryProfile.Id, cancellationToken);
        var now = clock.UtcNow.ToUniversalTime();
        return states.Select(state => CreateShare.ToSummary(state, now)).ToArray();
    }
}
