using Beeexy.Domain.Common;

namespace Beeexy.Domain.History;

public sealed record AuthoritativeSourceReference
{
    private AuthoritativeSourceReference(
        AuthoritativeClinicalSourceType sourceType,
        EntityId sourceId)
    {
        SourceType = sourceType;
        SourceId = sourceId;
    }

    public AuthoritativeClinicalSourceType SourceType { get; }

    public EntityId SourceId { get; }

    public static AuthoritativeSourceReference ForPreTriageEpisode(EntityId episodeId)
    {
        EnsureNonEmpty(episodeId, nameof(episodeId));
        return new AuthoritativeSourceReference(
            AuthoritativeClinicalSourceType.PreTriageEpisode,
            episodeId);
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
