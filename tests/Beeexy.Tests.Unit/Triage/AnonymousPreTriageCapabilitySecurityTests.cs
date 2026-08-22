using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class AnonymousPreTriageCapabilitySecurityTests
{
    private readonly CryptographicAnonymousPreTriageCapabilityService _service = new();

    [Fact]
    public void Generate_UsesVersionedTransportSafeCapabilityWith256BitsOfRandomInput()
    {
        var generated = _service.Generate();

        Assert.StartsWith("ptc1.", generated.Value, StringComparison.Ordinal);
        Assert.Equal(48, generated.Value.Length);
        Assert.Matches("^ptc1\\.[A-Za-z0-9_-]{43}$", generated.Value);
        Assert.Equal("[REDACTED]", generated.ToString());
    }

    [Fact]
    public void Generate_ReturnsHashThatDiffersFromRawCapability()
    {
        var generated = _service.Generate();

        Assert.NotEqual(generated.Value, generated.Hash.Value);
        Assert.StartsWith("sha256:", generated.Hash.Value, StringComparison.Ordinal);
        Assert.Equal(71, generated.Hash.Value.Length);
        Assert.DoesNotContain(generated.Value, generated.Hash.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ProducesIndependentCapabilitiesAndHashes()
    {
        var generated = Enumerable.Range(0, 128)
            .Select(_ => _service.Generate())
            .ToArray();

        Assert.Equal(128, generated.Select(value => value.Value).Distinct().Count());
        Assert.Equal(128, generated.Select(value => value.Hash.Value).Distinct().Count());
    }

    [Fact]
    public void Hash_IsStableOnlyForTheSameCapability()
    {
        var first = _service.Generate();
        var second = _service.Generate();

        Assert.Equal(first.Hash, _service.Hash(first.Value));
        Assert.NotEqual(first.Hash, _service.Hash(second.Value));
    }

    [Fact]
    public void Verify_AcceptsOnlyMatchingCapability()
    {
        var first = _service.Generate();
        var second = _service.Generate();

        Assert.True(_service.Verify(first.Value, first.Hash));
        Assert.False(_service.Verify(second.Value, first.Hash));
        Assert.False(_service.Verify(null, first.Hash));
        Assert.False(_service.Verify("not-a-capability", first.Hash));
    }

    [Fact]
    public void Verify_FailsClosedForMalformedPersistedHashRepresentation()
    {
        var capability = _service.Generate();
        var malformedHash = AnonymousCapabilityHash.FromHash(new string('x', 64));

        Assert.False(_service.Verify(capability.Value, malformedHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ptc1.short")]
    [InlineData("rt1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ptc1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void Hash_RejectsMalformedCapabilities(string capability)
    {
        Assert.Throws<ArgumentException>(() => _service.Hash(capability));
    }
}
