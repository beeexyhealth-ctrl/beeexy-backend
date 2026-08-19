using Beeexy.Application.Common;
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
            "Request failure mapped to safe Problem Details with status {StatusCode}.",
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
