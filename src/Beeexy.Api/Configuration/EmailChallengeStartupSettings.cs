using Beeexy.Application.Identity;
using Beeexy.Infrastructure.Identity;

namespace Beeexy.Api.Configuration;

internal sealed record EmailChallengeStartupSettings(
    EmailChallengePolicy Policy,
    string OtpHashingKey,
    AuthenticationEmailSenderOptions EmailSender);
