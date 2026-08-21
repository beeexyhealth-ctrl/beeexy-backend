using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Beeexy.Api.Errors;

internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = MapException(exception);
        httpContext.Response.StatusCode = problemDetails.Status
            ?? StatusCodes.Status500InternalServerError;

        if (exception is RateLimitExceededException rateLimitException)
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(
                    rateLimitException.RetryAfter.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        logger.LogWarning(
            "Request failure {FailureType} mapped to safe Problem Details with status {StatusCode}.",
            exception.GetType().Name,
            httpContext.Response.StatusCode);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    internal static ProblemDetails MapException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DomainException domainException)
        {
            var domainProblem = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Domain validation failed.",
                Detail = domainException.Error.Message
            };
            domainProblem.Extensions["errorCode"] = domainException.Error.Code;
            return domainProblem;
        }

        if (exception is RequestValidationException validationException)
        {
            var validationProblem = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Request validation failed.",
                Detail = validationException.Message
            };
            validationProblem.Extensions["errorCode"] = validationException.Code;
            return validationProblem;
        }

        if (exception is RateLimitExceededException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = "Please try again later."
            };
        }

        if (exception is EmailChallengeAttemptLimitException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many verification attempts.",
                Detail = "This verification challenge can no longer be attempted."
            };
        }

        if (exception is EmailChallengeReplayException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Verification challenge already used.",
                Detail = "Request a new verification challenge."
            };
        }

        if (exception is EmailChallengeUnauthorizedException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed.",
                Detail = "The email challenge could not be verified."
            };
        }

        if (exception is SessionAuthenticationException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed.",
                Detail = "The authentication session is invalid."
            };
        }

        if (exception is ExternalIdentityAuthenticationException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed.",
                Detail = "The external identity could not be authenticated."
            };
        }

        if (exception is ExternalIdentityProviderUnavailableException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Authentication provider unavailable.",
                Detail = "The external identity provider is currently unavailable."
            };
        }

        if (exception is ProfileUpdateConcurrencyException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Profile update conflict.",
                Detail = "The profile changed after it was read. Retrieve it and try again."
            };
        }

        if (exception is ManagedPatientCreationConflictException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Care relationship creation conflict.",
                Detail = "The managed patient and care relationship could not be created."
            };
        }

        if (exception is PatientProfileNotFoundException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Patient profile not found.",
                Detail = "The requested patient profile could not be found."
            };
        }

        if (exception is CareRelationshipNotFoundException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Care relationship not found.",
                Detail = "The requested care relationship could not be found."
            };
        }

        if (exception is BadHttpRequestException)
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request is malformed."
            };
        }

        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred."
        };
    }
}
