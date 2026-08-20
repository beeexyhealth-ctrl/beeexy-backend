using Beeexy.Api.Errors;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace Beeexy.Tests.Unit.Api;

public sealed class Phase22ExceptionMappingTests
{
    [Fact]
    public void InvalidRequest_MapsToSafeUnprocessableEntity()
    {
        var problem = ApiExceptionHandler.MapException(
            new RequestValidationException(
                "authentication.invalid_email",
                "A valid email address is required."));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.Status);
        Assert.Equal("authentication.invalid_email", problem.Extensions["errorCode"]);
        Assert.DoesNotContain("Exception", problem.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RateLimit_MapsToSafeTooManyRequests()
    {
        var problem = ApiExceptionHandler.MapException(
            new RateLimitExceededException(TimeSpan.FromMinutes(1)));

        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.DoesNotContain("email", problem.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedBody_MapsToBadRequestWithoutParserDetails()
    {
        var problem = ApiExceptionHandler.MapException(
            new BadHttpRequestException("sensitive parser detail"));

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.DoesNotContain("sensitive parser detail", problem.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(EmailChallengeUnauthorizedException), StatusCodes.Status401Unauthorized)]
    [InlineData(typeof(EmailChallengeReplayException), StatusCodes.Status409Conflict)]
    [InlineData(typeof(EmailChallengeAttemptLimitException), StatusCodes.Status429TooManyRequests)]
    [InlineData(typeof(SessionAuthenticationException), StatusCodes.Status401Unauthorized)]
    public void VerificationFailures_MapToPhase23StatusWithoutSecretDetails(
        Type exceptionType,
        int expectedStatus)
    {
        var exception = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(exceptionType));

        var problem = ApiExceptionHandler.MapException(exception);

        Assert.Equal(expectedStatus, problem.Status);
        Assert.DoesNotContain("583104", problem.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("otp", problem.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
