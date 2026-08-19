using Beeexy.Application.Identity;

namespace Beeexy.Api.Configuration;

internal sealed record EmailChallengeStartupSettings(
    EmailChallengePolicy Policy,
    string OtpHashingKey,
    bool UseInMemoryEmailSender);
