using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class NvidiaClinicalAiProviderTests
{
    private const string ApiKey = "nvidia-test-key-must-never-be-logged";

    [Fact]
    public async Task InterpretAsync_MapsStrictNvidiaJsonAndSendsExtractionRequest()
    {
        string? requestBody = null;
        AuthenticationHeaderValue? authorization = null;
        var provider = CreateProvider(
            SuccessfulResponse("""
            {
              "schemaVersion":"clinical-interpretation-v1",
              "intent":"PRE_TRIAGE_INPUT",
              "pathwayCandidate":"HEADACHE",
              "facts":[
                {"code":"DURATION","value":{"value":1,"unit":"DAYS"},"confidence":"SUFFICIENT"},
                {"code":"INTENSITY","value":{"value":7},"confidence":"SUFFICIENT"},
                {"code":"ADDITIONAL_SYMPTOMS","value":{"values":["NAUSEA"]},"confidence":"SUFFICIENT"}
              ],
              "symptoms":[],
              "ambiguities":[],
              "requiresClarification":false
            }
            """),
            async request =>
            {
                requestBody = await request.Content!.ReadAsStringAsync();
                authorization = request.Headers.Authorization;
            });

        var output = await provider.InterpretAsync(Request());

        Assert.Equal(ClinicalAiProviderOutput.CurrentSchemaVersion, output.SchemaVersion);
        Assert.Equal(ClinicalIntentClassification.PreTriageInput, output.Intent);
        Assert.Equal("HEADACHE", output.PathwayCandidate);
        Assert.Collection(
            output.Facts!,
            fact => Assert.Equal(
                new ClinicalAiDurationValue(1, ClinicalDurationUnit.Days),
                fact.Value),
            fact => Assert.Equal(new ClinicalAiIntegerValue(7), fact.Value),
            fact => Assert.Equal(
                ["NAUSEA"],
                Assert.IsType<ClinicalAiMultipleChoiceValue>(fact.Value).Values));
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal(ApiKey, authorization.Parameter);
        Assert.NotNull(requestBody);
        using var requestJson = JsonDocument.Parse(requestBody);
        Assert.Equal("nvidia/nemotron-3.5-lightning-30b-a3b",
            requestJson.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_object", requestJson.RootElement
            .GetProperty("response_format").GetProperty("type").GetString());
        Assert.False(requestJson.RootElement
            .GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(0, requestJson.RootElement.GetProperty("temperature").GetDouble());
        Assert.Contains("pre-triage-structured-extraction-v2", requestBody,
            StringComparison.Ordinal);
        Assert.Contains("HEADACHE", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterpretAsync_ConstrainsPreSessionClassificationToFivePathways()
    {
        string? requestBody = null;
        var provider = CreateProvider(
            _ => SuccessfulResponse("""
            {
              "schemaVersion":"clinical-interpretation-v1",
              "intent":"PRE_TRIAGE_INPUT",
              "pathwayCandidate":"OTHER_SYMPTOMS",
              "facts":[],
              "symptoms":[],
              "ambiguities":[],
              "requiresClarification":false
            }
            """),
            async request => requestBody = await request.Content!.ReadAsStringAsync());

        await provider.InterpretAsync(new ClinicalAiInterpretationRequest("My knee hurts"));

        Assert.NotNull(requestBody);
        Assert.Contains("PRE_SESSION", requestBody, StringComparison.Ordinal);
        Assert.Contains("HEADACHE", requestBody, StringComparison.Ordinal);
        Assert.Contains("ABDOMINAL_PAIN", requestBody, StringComparison.Ordinal);
        Assert.Contains("CHEST_PAIN", requestBody, StringComparison.Ordinal);
        Assert.Contains("FEVER", requestBody, StringComparison.Ordinal);
        Assert.Contains("OTHER_SYMPTOMS", requestBody, StringComparison.Ordinal);
        Assert.Contains("ADDITIONAL_SYMPTOMS", requestBody, StringComparison.Ordinal);
        Assert.Contains("untrusted patient data", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterpretAsync_OmitsJsonObjectResponseFormatWhenDisabledByConfiguration()
    {
        string? requestBody = null;
        var provider = CreateProvider(
            _ => SuccessfulResponse("""
            {
              "schemaVersion":"clinical-interpretation-v1",
              "intent":"PRE_TRIAGE_INPUT",
              "pathwayCandidate":"HEADACHE",
              "facts":[],
              "symptoms":[],
              "ambiguities":[],
              "requiresClarification":false
            }
            """),
            async request => requestBody = await request.Content!.ReadAsStringAsync(),
            useJsonObjectResponseFormat: false);

        await provider.InterpretAsync(Request());

        Assert.NotNull(requestBody);
        using var requestJson = JsonDocument.Parse(requestBody);
        Assert.False(requestJson.RootElement.TryGetProperty("response_format", out _));
        Assert.False(requestJson.RootElement
            .GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(0, requestJson.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(512, requestJson.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(requestJson.RootElement.GetProperty("stream").GetBoolean());
    }

    [Theory]
    [InlineData("""{"schemaVersion":"clinical-interpretation-v1","intent":"PRE_TRIAGE_INPUT","pathwayCandidate":"HEADACHE","facts":[{"code":"UNKNOWN","value":{"value":7},"confidence":"SUFFICIENT"}],"symptoms":[],"ambiguities":[],"requiresClarification":false}""")]
    [InlineData("""{"schemaVersion":"clinical-interpretation-v1","intent":"PRE_TRIAGE_INPUT","pathwayCandidate":"HEADACHE","facts":[{"code":"INTENSITY","value":{"value":7},"confidence":"UNKNOWN"}],"symptoms":[],"ambiguities":[],"requiresClarification":false}""")]
    [InlineData("""{"schemaVersion":"clinical-interpretation-v1","intent":"PRE_TRIAGE_INPUT","pathwayCandidate":"HEADACHE","facts":[],"symptoms":[],"ambiguities":[],"requiresClarification":false,"urgency":"HIGH"}""")]
    public async Task InterpretAsync_RejectsUnknownOrForbiddenStructuredMembers(string output)
    {
        var provider = CreateProvider(SuccessfulResponse(output));

        var exception = await Assert.ThrowsAsync<ClinicalAiProviderException>(() =>
            provider.InterpretAsync(Request()));

        Assert.Equal(ClinicalAiProviderFailureCategory.InvalidStructuredResponse,
            exception.Category);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterpretAsync_MapsAmbiguityWithoutInventingFacts()
    {
        var provider = CreateProvider(SuccessfulResponse("""
            {
              "schemaVersion":"clinical-interpretation-v1",
              "intent":"AMBIGUOUS",
              "pathwayCandidate":"HEADACHE",
              "facts":[],
              "symptoms":[],
              "ambiguities":[{"kind":"FACT_VALUE","factCode":"INTENSITY"}],
              "requiresClarification":true
            }
            """));

        var output = await provider.InterpretAsync(Request());

        Assert.Equal(ClinicalIntentClassification.Ambiguous, output.Intent);
        Assert.Empty(output.Facts!);
        Assert.Equal(ClinicalAiAmbiguityKind.FactValue, Assert.Single(output.Ambiguities!).Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ClinicalAiProviderFailureCategory.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, ClinicalAiProviderFailureCategory.RejectedOutput)]
    public async Task InterpretAsync_MapsNvidiaHttpFailuresSafely(
        HttpStatusCode status,
        ClinicalAiProviderFailureCategory expected)
    {
        var provider = CreateProvider(new HttpResponseMessage(status)
        {
            Content = new StringContent("provider details that must not escape")
        });

        var exception = await Assert.ThrowsAsync<ClinicalAiProviderException>(() =>
            provider.InterpretAsync(Request()));

        Assert.Equal(expected, exception.Category);
        Assert.DoesNotContain("provider details", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterpretAsync_MapsTimeoutAndPropagatesCallerCancellation()
    {
        var timeoutProvider = CreateProvider(
            _ => throw new OperationCanceledException(),
            timeout: TimeSpan.FromMilliseconds(10));
        var timeout = await Assert.ThrowsAsync<ClinicalAiProviderException>(() =>
            timeoutProvider.InterpretAsync(Request()));
        Assert.Equal(ClinicalAiProviderFailureCategory.Timeout, timeout.Category);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledProvider = CreateProvider(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledProvider.InterpretAsync(Request(), cancellation.Token));
    }

    [Fact]
    public void Options_EnableOnlyValidNvidiaConfiguration()
    {
        Assert.False(new ClinicalAiProviderOptions(
            "NVIDIA", null, null, null, null).TryCreateNvidia(out _));
        Assert.False(new ClinicalAiProviderOptions(
            "other", ApiKey, null, null, null).TryCreateNvidia(out _));
        Assert.False(new ClinicalAiProviderOptions(
            "NVIDIA", ApiKey, null, "http://localhost:8000/v1", 20)
            .TryCreateNvidia(out _));

        Assert.True(new ClinicalAiProviderOptions(
            "NVIDIA", ApiKey, null, null, null).TryCreateNvidia(out var options));
        Assert.NotNull(options);
        Assert.Equal(ClinicalAiProviderOptions.DefaultNvidiaModel, options.Model);
        Assert.Equal(new Uri("https://integrate.api.nvidia.com/v1/"), options.BaseUri);
        Assert.True(options.UseJsonObjectResponseFormat);

        Assert.True(new ClinicalAiProviderOptions(
            "NVIDIA", ApiKey, "google/diffusiongemma-26b-a4b-it", null, null, false)
            .TryCreateNvidia(out var diffusionGemmaOptions));
        Assert.NotNull(diffusionGemmaOptions);
        Assert.False(diffusionGemmaOptions.UseJsonObjectResponseFormat);
    }

    private static NvidiaClinicalAiProvider CreateProvider(
        HttpResponseMessage response,
        Func<HttpRequestMessage, Task>? inspect = null) =>
        CreateProvider(_ => response, inspect);

    private static NvidiaClinicalAiProvider CreateProvider(
        string response,
        Func<HttpRequestMessage, Task>? inspect = null) =>
        CreateProvider(SuccessfulResponse(response), inspect);

    private static NvidiaClinicalAiProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> response,
        Func<HttpRequestMessage, Task>? inspect = null,
        TimeSpan? timeout = null,
        bool useJsonObjectResponseFormat = true) =>
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
                timeout ?? TimeSpan.FromSeconds(1),
                useJsonObjectResponseFormat));

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

    private static ClinicalAiInterpretationRequest Request() =>
        new(
            "I've had a headache since yesterday, intensity around 7/10, and I also feel nauseous.",
            ClinicalPathways.Headache,
            allowedFactCodes:
            [
                QuestionCode.Create("DURATION"),
                QuestionCode.Create("INTENSITY"),
                QuestionCode.Create("ADDITIONAL_SYMPTOMS")
            ]);

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => await handler(request);
    }
}
