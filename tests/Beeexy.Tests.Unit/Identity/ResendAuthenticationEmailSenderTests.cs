using System.Net;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class ResendAuthenticationEmailSenderTests
{
    private const string ApiKey = "re_unit_test_key_that_is_not_a_real_secret";
    private const string OneTimeCode = "583104";

    [Fact]
    public async Task SendAsync_UsesAuthenticatedResendRequestWithMinimalOtpContent()
    {
        CapturedRequest? captured = null;
        using var handler = new RecordingHttpMessageHandler(async request =>
        {
            captured = new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Headers.UserAgent.ToString(),
                await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"email_test\"}")
            };
        });
        using var sender = CreateSender(handler);
        var expiresAt = new DateTimeOffset(2026, 8, 20, 3, 15, 0, TimeSpan.Zero);

        await sender.SendAsync(new AuthenticationEmailMessage(
            NormalizedEmail.Create("person@example.com"),
            OneTimeCode,
            expiresAt));

        var request = Assert.IsType<CapturedRequest>(captured);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.resend.com/emails", request.Uri?.AbsoluteUri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(ApiKey, request.AuthorizationParameter);
        Assert.Contains("Beeexy-Backend/1.0", request.UserAgent, StringComparison.Ordinal);

        using var payload = JsonDocument.Parse(request.Body);
        var root = payload.RootElement;
        Assert.Equal("Beeexy <auth@beeexy.test>", root.GetProperty("from").GetString());
        Assert.Equal(
            "person@example.com",
            root.GetProperty("to")[0].GetString());
        Assert.Equal("Your Beeexy sign-in code", root.GetProperty("subject").GetString());
        var text = root.GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains(OneTimeCode, text, StringComparison.Ordinal);
        Assert.Contains("Beeexy", text, StringComparison.Ordinal);
        Assert.Contains("expires", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-08-20 03:15:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("did not request", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task ProviderFailure_ReturnsSafeDeliveryExceptionWithoutProviderOrSecretData()
    {
        const string providerDetail = "provider response must not escape";
        using var handler = new RecordingHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(providerDetail)
            }));
        using var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
            sender.SendAsync(new AuthenticationEmailMessage(
                NormalizedEmail.Create("person@example.com"),
                OneTimeCode,
                DateTimeOffset.UtcNow.AddMinutes(10))));

        Assert.DoesNotContain(OneTimeCode, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(providerDetail, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.com", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_StringRepresentationNeverContainsApiKey()
    {
        var options = CreateOptions();

        Assert.DoesNotContain(ApiKey, options.ToString(), StringComparison.Ordinal);
    }

    private static ResendAuthenticationEmailSender CreateSender(HttpMessageHandler handler)
    {
        return new ResendAuthenticationEmailSender(
            new HttpClient(handler),
            CreateOptions());
    }

    private static ResendAuthenticationEmailOptions CreateOptions()
    {
        return new ResendAuthenticationEmailOptions(
            ApiKey,
            "auth@beeexy.test",
            "Beeexy");
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string UserAgent,
        string Body);

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return responseFactory(request);
        }
    }
}
