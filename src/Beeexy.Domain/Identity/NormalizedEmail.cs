using System.Net.Mail;

namespace Beeexy.Domain.Identity;

public sealed record NormalizedEmail
{
    public const int MaximumLength = 320;

    private NormalizedEmail(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static NormalizedEmail Create(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var candidate = email.Trim();
        if (candidate.Length > MaximumLength ||
            !MailAddress.TryCreate(candidate, out var parsed) ||
            !string.Equals(parsed.Address, candidate, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The email address is invalid.", nameof(email));
        }

        return new NormalizedEmail(candidate.ToLowerInvariant());
    }

    public override string ToString()
    {
        return Value;
    }
}
