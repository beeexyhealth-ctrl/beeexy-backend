using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Identity;

namespace Beeexy.Infrastructure.Identity;

internal sealed class ResendAuthenticationEmailSender : IAuthenticationEmailSender, IDisposable
{
    private static readonly Uri ApiBaseAddress = new("https://api.resend.com/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly ResendAuthenticationEmailOptions _options;

    public ResendAuthenticationEmailSender(
        HttpClient httpClient,
        ResendAuthenticationEmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _httpClient.BaseAddress = ApiBaseAddress;
        _httpClient.Timeout = RequestTimeout;
        _options = options;
    }

    public async Task SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.ApiKey);
        request.Headers.UserAgent.ParseAdd("Beeexy-Backend/1.0");
        request.Content = JsonContent.Create(new ResendEmailRequest(
            $"{_options.SenderDisplayName} <{_options.SenderEmail}>",
            [message.Recipient.Value],
            "Your Beeexy sign-in code",
            CreatePlainTextBody(message)));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AuthenticationEmailDeliveryException();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationEmailDeliveryException();
        }
        catch (HttpRequestException)
        {
            throw new AuthenticationEmailDeliveryException();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string CreatePlainTextBody(AuthenticationEmailMessage message)
    {
        return $"""
            Your Beeexy sign-in code is: {message.OneTimeCode}

            This code expires at {message.ExpiresAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.

            Use this code only to finish signing in to Beeexy. If you did not request it, you can ignore this email.
            """;
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text);
}
