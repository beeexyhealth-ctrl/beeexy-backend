namespace Beeexy.Infrastructure.Identity;

public sealed record GoogleExternalIdentityOptions
{
    public GoogleExternalIdentityOptions(bool enabled, string? clientId)
    {
        if (enabled && string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException(
                "A Google client ID is required when Google authentication is enabled.",
                nameof(clientId));
        }

        Enabled = enabled;
        ClientId = enabled ? clientId!.Trim() : null;
    }

    public bool Enabled { get; }

    public string? ClientId { get; }
}
