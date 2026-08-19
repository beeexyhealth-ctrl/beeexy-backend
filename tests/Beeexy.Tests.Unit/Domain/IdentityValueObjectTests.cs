using Beeexy.Domain.Identity;

namespace Beeexy.Tests.Unit.Domain;

public sealed class IdentityValueObjectTests
{
    [Theory]
    [InlineData("  Person@Example.COM  ", "person@example.com")]
    [InlineData("first.last+tag@example.com", "first.last+tag@example.com")]
    public void NormalizedEmail_Create_NormalizesValidAddress(string input, string expected)
    {
        var email = NormalizedEmail.Create(input);

        Assert.Equal(expected, email.Value);
        Assert.Equal(expected, email.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Display Name <person@example.com>")]
    [InlineData("person @example.com")]
    public void NormalizedEmail_Create_RejectsInvalidAddress(string input)
    {
        Assert.Throws<ArgumentException>(() => NormalizedEmail.Create(input));
    }

    [Fact]
    public void TokenHash_FromHash_PreservesHashWithoutExposingRawTokenBehavior()
    {
        const string hash = "sha256:VGVzdEhhc2hWYWx1ZQ==";

        var value = TokenHash.FromHash(hash);

        Assert.Equal(hash, value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hash with whitespace")]
    public void TokenHash_FromHash_RejectsInvalidRepresentation(string input)
    {
        Assert.Throws<ArgumentException>(() => TokenHash.FromHash(input));
    }
}
