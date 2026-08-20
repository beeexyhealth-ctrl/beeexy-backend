using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public interface ICurrentSessionIdentity
{
    CurrentSessionIdentity GetRequired();
}

public sealed record CurrentSessionIdentity(EntityId AccountId, EntityId SessionId);
