using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Domain;

public sealed class DomainErrorTests
{
    [Fact]
    public void Constructor_StoresCodeAndMessage()
    {
        var error = new DomainError("triage.invalid_answer", "The answer is invalid.");

        Assert.Equal("triage.invalid_answer", error.Code);
        Assert.Equal("The answer is invalid.", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingCode(string? code)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DomainError(code!, "Safe message."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingMessage(string? message)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DomainError("domain.error", message!));
    }
}
