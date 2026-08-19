using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed record AuthenticationEmailMessage(
    NormalizedEmail Recipient,
    string OneTimeCode,
    DateTimeOffset ExpiresAt);
