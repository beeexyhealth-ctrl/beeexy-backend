using Beeexy.Domain.Common;

namespace Beeexy.Domain.Patients;

public sealed record AuthorizationAttestation
{
    public const int MaximumVersionLength = 64;

    private AuthorizationAttestation()
    {
        Version = null!;
    }

    private AuthorizationAttestation(string version, DateTimeOffset attestedAt)
    {
        Version = version;
        AttestedAt = attestedAt;
    }

    public string Version { get; private init; }

    public DateTimeOffset AttestedAt { get; private init; }

    public static AuthorizationAttestation Create(
        string version,
        DateTimeOffset attestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        InstantGuard.EnsureUtc(attestedAt, nameof(attestedAt));

        var candidate = version.Trim();
        if (candidate.Length > MaximumVersionLength)
        {
            throw new ArgumentException(
                $"The attestation version cannot exceed {MaximumVersionLength} characters.",
                nameof(version));
        }

        return new AuthorizationAttestation(candidate, attestedAt);
    }
}
