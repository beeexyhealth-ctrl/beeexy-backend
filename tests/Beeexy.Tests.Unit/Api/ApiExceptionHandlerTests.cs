using Beeexy.Api.Errors;
using Beeexy.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace Beeexy.Tests.Unit.Api;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public void MapException_MapsDomainErrorToSafeUnprocessableEntity()
    {
        var exception = new DomainException(
            new DomainError("domain.invalid_state", "The requested state is invalid."));

        var problem = ApiExceptionHandler.MapException(exception);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.Status);
        Assert.Equal("Domain validation failed.", problem.Title);
        Assert.Equal("The requested state is invalid.", problem.Detail);
        Assert.Equal("domain.invalid_state", problem.Extensions["errorCode"]);
        Assert.DoesNotContain(nameof(DomainException), problem.ToString());
    }

    [Fact]
    public void MapException_HidesUnexpectedExceptionDetails()
    {
        const string sensitiveMessage = "Password=never-expose-this";

        var problem = ApiExceptionHandler.MapException(
            new InvalidOperationException(sensitiveMessage));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Title);
        Assert.Null(problem.Detail);
        Assert.DoesNotContain(sensitiveMessage, problem.ToString());
        Assert.DoesNotContain(nameof(InvalidOperationException), problem.ToString());
    }
}
