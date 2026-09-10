using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed class ShareScopeEvaluator : IShareScopeEvaluator
{
    public SharedProfileSelection Evaluate(
        ShareGrant grant,
        IReadOnlyList<ShareGrantItem> items,
        ShareScope tokenScope)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(items);
        if (grant.Scope != tokenScope ||
            items.Any(item => item.ShareGrantId != grant.Id) ||
            items.Select(item => new { item.ResourceType, item.ResourceId })
                .Distinct()
                .Count() != items.Count)
        {
            throw new ShareAccessDeniedException();
        }

        var references = items
            .OrderBy(item => item.ResourceType.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId.Value)
            .Select(item => new ShareItemReference(item.ResourceType, item.ResourceId))
            .ToArray();

        return grant.Scope switch
        {
            ShareScope.FullProfile when items.Count == 0 =>
                new SharedProfileSelection(grant.Scope, true, []),
            ShareScope.PreTriage when items.Count > 0 && items.All(item =>
                item.ResourceType.Value == SupportedShareResourceTypes.PreTriageEpisode) =>
                new SharedProfileSelection(grant.Scope, false, references),
            ShareScope.SpecificRecords when items.Count > 0 && items.All(item =>
                SupportedShareResourceTypes.Contains(item.ResourceType)) =>
                new SharedProfileSelection(grant.Scope, false, references),
            _ => throw new ShareAccessDeniedException()
        };
    }
}
