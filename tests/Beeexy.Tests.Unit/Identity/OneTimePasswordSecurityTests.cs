using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class OneTimePasswordSecurityTests
{
    [Fact]
    public void Generator_ProducesConfiguredNumericFormat()
    {
        var generator = new CryptographicOneTimePasswordGenerator();

        var codes = Enumerable.Range(0, 100)
            .Select(_ => generator.Generate(6))
            .ToArray();

        Assert.All(codes, code => Assert.Matches("^[0-9]{6}$", code));
        Assert.True(codes.Distinct(StringComparer.Ordinal).Count() > 1);
    }

    [Fact]
    public void Hasher_IsReproducibleAndChallengeSpecificWithoutContainingPlaintext()
    {
        const string oneTimeCode = "583104";
        IOneTimePasswordHasher hasher = new HmacOneTimePasswordHasher(
            "unit-test-only-hmac-key-with-at-least-32-bytes");
        var firstChallengeId = EntityId.New();
        var secondChallengeId = EntityId.New();

        var firstHash = hasher.Hash(firstChallengeId, oneTimeCode);
        var repeatedHash = hasher.Hash(firstChallengeId, oneTimeCode);
        var secondHash = hasher.Hash(secondChallengeId, oneTimeCode);

        Assert.Equal(firstHash, repeatedHash);
        Assert.NotEqual(firstHash, secondHash);
        Assert.StartsWith("hmac-sha256:", firstHash.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(oneTimeCode, firstHash.Value, StringComparison.Ordinal);
        Assert.True(hasher.Verify(firstChallengeId, oneTimeCode, firstHash));
        Assert.False(hasher.Verify(firstChallengeId, "000000", firstHash));
        Assert.False(hasher.Verify(secondChallengeId, oneTimeCode, firstHash));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void Generator_RejectsUnsupportedCodeLength(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CryptographicOneTimePasswordGenerator().Generate(length));
    }
}
