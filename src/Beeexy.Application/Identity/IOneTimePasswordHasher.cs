using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IOneTimePasswordHasher
{
    TokenHash Hash(EntityId challengeId, string oneTimeCode);
}
