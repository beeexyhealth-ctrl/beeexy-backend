using Google.Apis.Auth;

namespace Beeexy.Infrastructure.Identity;

internal interface IGoogleIdTokenValidator
{
    Task<GoogleIdTokenPayload> ValidateAsync(
        string credential,
        string clientId,
        CancellationToken cancellationToken = default);
}

internal sealed record GoogleIdTokenPayload(
    string? Subject,
    string? Email,
    bool EmailVerified);

internal sealed class GoogleIdTokenRejectedException : Exception;

internal sealed class GoogleIdTokenProviderUnavailableException : Exception;

internal sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public async Task<GoogleIdTokenPayload> ValidateAsync(
        string credential,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                credential,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [clientId]
                });
            cancellationToken.ThrowIfCancellationRequested();
            return new GoogleIdTokenPayload(
                payload.Subject,
                payload.Email,
                payload.EmailVerified);
        }
        catch (InvalidJwtException)
        {
            throw new GoogleIdTokenRejectedException();
        }
        catch (HttpRequestException)
        {
            throw new GoogleIdTokenProviderUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GoogleIdTokenProviderUnavailableException();
        }
    }
}
