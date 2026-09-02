using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase102Provider")]
[Trait("Category", "Phase108")]
public sealed class NvidiaAiProviderAdapterTests
{
    private const string ApiKey = "phase-102-secret-key";

    [Fact]
    public async Task ExecuteAsync_TranslatesNeutralRequestAndReturnsNeutralContent()
    {
        string? body = null;
        AuthenticationHeaderValue? authorization = null;
        var requestCount = 0;
        var provider = CreateProvider(
            SuccessfulResponse("{\"schemaVersion\":\"v1\",\"answer\":\"ok\"}"),
            async request =>
            {
                requestCount++;
                body = await request.Content!.ReadAsStringAsync();
                authorization = request.Headers.Authorization;
            });

        var result = await ((IAiProvider)provider).ExecuteAsync(Request());

        Assert.Equal("{\"schemaVersion\":\"v1\",\"answer\":\"ok\"}",
            result.StructuredContent);
        Assert.Equal(ClinicalAiProviderOptions.NvidiaProviderName,
            ((IAiProvider)provider).ProviderIdentifier);
        Assert.Equal(ClinicalAiProviderOptions.DefaultNvidiaModel,
            ((IAiProvider)provider).ModelIdentifier);
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal(ApiKey, authorization.Parameter);
        Assert.Equal(1, requestCount);
        using var requestJson = JsonDocument.Parse(body!);
        Assert.Equal(2048, requestJson.RootElement.GetProperty("max_tokens").GetInt32());
        var messages = requestJson.RootElement.GetProperty("messages");
        Assert.Equal("system contract", messages[0].GetProperty("content").GetString());
        Assert.Equal("private prepared input", messages[1].GetProperty("content").GetString());
        Assert.DoesNotContain("trace-safe", body, StringComparison.Ordinal);
        Assert.DoesNotContain("generic-workload", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, AiProviderFailureCategory.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, AiProviderFailureCategory.Transient)]
    [InlineData(HttpStatusCode.InternalServerError, AiProviderFailureCategory.Transient)]
    [InlineData(HttpStatusCode.BadRequest, AiProviderFailureCategory.Permanent)]
    public async Task ExecuteAsync_NormalizesHttpFailuresWithoutRawBody(
        HttpStatusCode status,
        AiProviderFailureCategory expected)
    {
        var provider = CreateProvider(new HttpResponseMessage(status)
        {
            Content = new StringContent("raw provider body with secret")
        });

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            ((IAiProvider)provider).ExecuteAsync(Request()));

        Assert.Equal(expected, exception.Category);
        Assert.DoesNotContain("raw provider", exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    public async Task ExecuteAsync_MapsMalformedTransportEnvelope(string response)
    {
        var provider = CreateProvider(response);

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            ((IAiProvider)provider).ExecuteAsync(Request()));

        Assert.Equal(AiProviderFailureCategory.MalformedResponse, exception.Category);
    }

    [Fact]
    public async Task ExecuteAsync_MapsTimeoutAndPropagatesCallerCancellation()
    {
        var timeoutProvider = CreateProvider(
            _ => throw new OperationCanceledException(),
            timeout: TimeSpan.FromMilliseconds(10));
        var timeout = await Assert.ThrowsAsync<AiProviderException>(() =>
            ((IAiProvider)timeoutProvider).ExecuteAsync(Request()));
        Assert.Equal(AiProviderFailureCategory.Timeout, timeout.Category);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledProvider = CreateProvider(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ((IAiProvider)cancelledProvider).ExecuteAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task UnavailableProvider_PreservesCredentialFreeFallback()
    {
        IAiProvider provider = new UnavailableClinicalAiProvider();

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            provider.ExecuteAsync(Request()));

        Assert.Equal(AiProviderFailureCategory.ConfigurationUnavailable, exception.Category);
        Assert.Equal("unconfigured", provider.ProviderIdentifier);
        Assert.Equal("unconfigured", provider.ModelIdentifier);
    }

    private static AiProviderRequest Request() => new(
        "generic-workload",
        new AiPromptIdentity("generic-contract", "v1"),
        "system contract",
        "private prepared input",
        new AiStructuredResultIdentity("generic-result", "v1"),
        "trace-safe");

    private static NvidiaClinicalAiProvider CreateProvider(
        HttpResponseMessage response,
        Func<HttpRequestMessage, Task>? inspect = null) =>
        CreateProvider(_ => response, inspect);

    private static NvidiaClinicalAiProvider CreateProvider(
        string response,
        Func<HttpRequestMessage, Task>? inspect = null) =>
        CreateProvider(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        }, inspect);

    private static NvidiaClinicalAiProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> response,
        Func<HttpRequestMessage, Task>? inspect = null,
        TimeSpan? timeout = null) =>
        new(
            new HttpClient(new StubHandler(async request =>
            {
                if (inspect is not null)
                {
                    await inspect(request);
                }

                return response(request);
            }))
            {
                BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/")
            },
            new NvidiaClinicalAiOptions(
                ApiKey,
                ClinicalAiProviderOptions.DefaultNvidiaModel,
                new Uri("https://integrate.api.nvidia.com/v1/"),
                timeout ?? TimeSpan.FromSeconds(1)));

    private static HttpResponseMessage SuccessfulResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content } } }
                }),
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
