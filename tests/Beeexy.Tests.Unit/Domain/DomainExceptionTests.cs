using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Domain;

public sealed class DomainExceptionTests
{
    [Fact]
    public void Constructor_ExposesDomainErrorAndSafeMessage()
    {
        var error = new DomainError("domain.invalid", "The operation is invalid.");

        var exception = new DomainException(error);

        Assert.Same(error, exception.Error);
        Assert.Equal(error.Message, exception.Message);
    }

    [Fact]
    public void Constructor_RejectsNullError()
    {
        Assert.Throws<ArgumentNullException>(() => new DomainException(null!));
    }
}
