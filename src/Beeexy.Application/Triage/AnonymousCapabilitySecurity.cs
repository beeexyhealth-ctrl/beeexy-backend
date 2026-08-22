using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public interface IAnonymousPreTriageCapabilityService
{
    GeneratedAnonymousCapability Generate();

    AnonymousCapabilityHash Hash(string capability);

    bool Verify(string? capability, AnonymousCapabilityHash expectedHash);
}

public sealed class GeneratedAnonymousCapability
{
    public GeneratedAnonymousCapability(
        string value,
        AnonymousCapabilityHash hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentNullException.ThrowIfNull(hash);
        Value = value;
        Hash = hash;
    }

    public string Value { get; }

    public AnonymousCapabilityHash Hash { get; }

    public override string ToString() => "[REDACTED]";
}
