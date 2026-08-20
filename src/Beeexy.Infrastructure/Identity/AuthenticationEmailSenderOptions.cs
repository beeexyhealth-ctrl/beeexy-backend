using Beeexy.Domain.Identity;

namespace Beeexy.Infrastructure.Identity;

public enum AuthenticationEmailSenderProvider
{
    InMemory,
    Resend
}

public sealed class AuthenticationEmailSenderOptions
{
    private AuthenticationEmailSenderOptions(
        AuthenticationEmailSenderProvider provider,
        ResendAuthenticationEmailOptions? resend)
    {
        Provider = provider;
        Resend = resend;
    }

    public AuthenticationEmailSenderProvider Provider { get; }

    public ResendAuthenticationEmailOptions? Resend { get; }

    public static AuthenticationEmailSenderOptions InMemory { get; } = new(
        AuthenticationEmailSenderProvider.InMemory,
        null);

    public static AuthenticationEmailSenderOptions CreateResend(
        ResendAuthenticationEmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new AuthenticationEmailSenderOptions(
            AuthenticationEmailSenderProvider.Resend,
            options);
    }
}

public sealed class ResendAuthenticationEmailOptions
{
    public const int MinimumApiKeyLength = 20;
    public const int MaximumApiKeyLength = 512;
    public const int MaximumSenderDisplayNameLength = 100;

    public ResendAuthenticationEmailOptions(
        string apiKey,
        string senderEmail,
        string senderDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderDisplayName);

        var normalizedApiKey = apiKey.Trim();
        if (!normalizedApiKey.StartsWith("re_", StringComparison.Ordinal) ||
            normalizedApiKey.Length < MinimumApiKeyLength ||
            normalizedApiKey.Length > MaximumApiKeyLength ||
            normalizedApiKey.Any(char.IsControl))
        {
            throw new ArgumentException("The Resend API key is invalid.", nameof(apiKey));
        }

        var normalizedDisplayName = senderDisplayName.Trim();
        if (normalizedDisplayName.Length > MaximumSenderDisplayNameLength ||
            normalizedDisplayName.Any(char.IsControl) ||
            normalizedDisplayName.IndexOfAny(['<', '>']) >= 0)
        {
            throw new ArgumentException(
                "The sender display name is invalid.",
                nameof(senderDisplayName));
        }

        ApiKey = normalizedApiKey;
        SenderEmail = NormalizedEmail.Create(senderEmail).Value;
        SenderDisplayName = normalizedDisplayName;
    }

    public string ApiKey { get; }

    public string SenderEmail { get; }

    public string SenderDisplayName { get; }

    public override string ToString()
    {
        return nameof(ResendAuthenticationEmailOptions);
    }
}
