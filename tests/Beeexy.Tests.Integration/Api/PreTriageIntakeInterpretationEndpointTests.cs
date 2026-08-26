using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriageIntakeInterpretationEndpointTests(
    PostgreSqlContainerFixture postgres)
{
    private const string Endpoint = "/api/v1/pre-triage/intake/interpret";
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";
    private const string Issuer = "https://api.beeexy.com";
    private const string Audience = "beeexy-client";

    public static TheoryData<string, string> DeterministicInputs => new()
    {
        { "Headache", "HEADACHE" },
        { "Stomach pain", "ABDOMINAL_PAIN" },
        { "Chest pain", "CHEST_PAIN" },
        { "Fever", "FEVER" },
        { "Other", "OTHER_SYMPTOMS" }
    };

    [Theory]
    [MemberData(nameof(DeterministicInputs))]
    public async Task ExactAliases_ReturnResolvedWithoutInvokingProvider(
        string text,
        string expectedPathway)
    {
        var provider = new ThrowingProvider(new InvalidOperationException(
            "The deterministic fast path must not invoke AI."));
        using var factory = CreateFactory(provider);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { text });
        var result = await response.Content.ReadFromJsonAsync<InterpretationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RESOLVED", result!.Resolution);
        Assert.Equal(expectedPathway, result.Pathway);
        Assert.Null(result.CandidatePathways);
        AssertEmpty(result.CandidateValues);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task NaturalLanguage_ReturnsOnlyValidatedCandidatesWithoutClinicalWrites()
    {
        const string text =
            "My stomach has hurt for two days and it's a 6 out of 10 with nausea.";
        var provider = new FixedProvider(Output(
            "ABDOMINAL_PAIN",
            facts:
            [
                Fact("DURATION", new ClinicalAiDurationValue(2, ClinicalDurationUnit.Days)),
                Fact("INTENSITY", new ClinicalAiIntegerValue(6)),
                Fact("ADDITIONAL_SYMPTOMS",
                    new ClinicalAiMultipleChoiceValue(["NAUSEA"]))
            ]));
        using var logger = new InMemoryLoggerProvider();
        using var factory = CreateFactory(provider, logger);
        using var client = factory.CreateApiClient();
        var before = await LoadClinicalCountsAsync();

        using var response = await client.PostAsJsonAsync(Endpoint, new { text });
        var result = await response.Content.ReadFromJsonAsync<InterpretationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RESOLVED", result!.Resolution);
        Assert.Equal("ABDOMINAL_PAIN", result.Pathway);
        Assert.Equal(new DurationResponse(2, "DAYS"), result.CandidateValues.Duration);
        Assert.Equal(6, result.CandidateValues.Intensity);
        Assert.Equal(["NAUSEA"], result.CandidateValues.AdditionalSymptoms);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(before, await LoadClinicalCountsAsync());
        Assert.DoesNotContain(text, string.Join('\n', logger.Messages),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompetingSymptoms_ReturnAmbiguousAuthoritativeCandidates()
    {
        var provider = new FixedProvider(Output(
            "CHEST_PAIN",
            symptoms:
            [
                Symptom("chest hurts", "CHEST_PAIN"),
                Symptom("head hurts", "HEADACHE"),
                Symptom("unsupported", "BACK_PAIN")
            ],
            ambiguities: [new ClinicalAiAmbiguity(ClinicalAiAmbiguityKind.Pathway)],
            intent: ClinicalIntentClassification.Ambiguous,
            requiresClarification: true));
        using var factory = CreateFactory(provider);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "My chest hurts and my head hurts" });
        var result = await response.Content.ReadFromJsonAsync<InterpretationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AMBIGUOUS", result!.Resolution);
        Assert.Null(result.Pathway);
        Assert.Equal(["HEADACHE", "CHEST_PAIN"], result.CandidatePathways);
        AssertEmpty(result.CandidateValues);
    }

    [Fact]
    public async Task InsufficientContext_ReturnsUnresolvedInsteadOfOtherSymptoms()
    {
        var provider = new FixedProvider(Output(
            "OTHER_SYMPTOMS",
            ambiguities:
            [new ClinicalAiAmbiguity(ClinicalAiAmbiguityKind.InsufficientContext)],
            intent: ClinicalIntentClassification.Ambiguous,
            requiresClarification: true));
        using var factory = CreateFactory(provider);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "I don't know" });
        var result = await response.Content.ReadFromJsonAsync<InterpretationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("UNRESOLVED", result!.Resolution);
        Assert.Null(result.Pathway);
        Assert.Null(result.CandidatePathways);
        AssertEmpty(result.CandidateValues);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsSafeProblemDetailsAndDoesNotLogText()
    {
        const string text = "Sensitive symptom text that must not appear in logs";
        using var logger = new InMemoryLoggerProvider();
        using var factory = CreateFactory(
            new ThrowingProvider(new ClinicalAiProviderException(
                ClinicalAiProviderFailureCategory.Timeout)),
            logger);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { text });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            "pre_triage.interpretation_unavailable",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("timeout", problem.RootElement.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(text, string.Join('\n', logger.Messages),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestAndOptionalAuthenticationBoundariesAreEnforced()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var missing = await client.PostAsJsonAsync(Endpoint, new { });
        using var blank = await client.PostAsJsonAsync(Endpoint, new { text = "   " });
        using var callerSelected = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Headache", pathway = "CHEST_PAIN" });
        using var invalidBearerRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { text = "Headache" })
        };
        invalidBearerRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-valid-token");
        using var invalidBearer = await client.SendAsync(invalidBearerRequest);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, callerSelected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt());
        using var authenticated = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Headache" });
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    private BeeexyApiFactory CreateFactory(
        IClinicalAiProvider provider,
        InMemoryLoggerProvider? logger = null) => new(
            postgres.ConnectionString,
            loggerProvider: logger,
            configureServices: services =>
            {
                services.RemoveAll<IClinicalAiProvider>();
                services.AddSingleton(provider);
            });

    private async Task<ClinicalCounts> LoadClinicalCountsAsync()
    {
        await using var db = new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
        return new ClinicalCounts(
            await db.PreTriageSessions.CountAsync(),
            await db.TriageAnswers.CountAsync(),
            await db.PreTriageEpisodes.CountAsync(),
            await db.ReportedSymptoms.CountAsync(),
            await db.ClinicalAssessments.CountAsync(),
            await db.ClinicalHistoryEvents.CountAsync(),
            await db.FhirExports.CountAsync(),
            await db.ClinicalAmendments.CountAsync());
    }

    private static void AssertEmpty(CandidateValuesResponse values)
    {
        Assert.Null(values.Duration);
        Assert.Null(values.Intensity);
        Assert.Null(values.AdditionalSymptoms);
    }

    private static ClinicalAiFactCandidate Fact(
        string code,
        ClinicalAiCandidateValue value) => new(
            QuestionCode.Create(code),
            value,
            ClinicalAiConfidenceSignal.Sufficient);

    private static ClinicalAiSymptomCandidate Symptom(string text, string pathway) => new(
        text,
        pathway,
        ClinicalAiConfidenceSignal.Sufficient);

    private static ClinicalAiProviderOutput Output(
        string pathway,
        IReadOnlyList<ClinicalAiFactCandidate>? facts = null,
        IReadOnlyList<ClinicalAiSymptomCandidate>? symptoms = null,
        IReadOnlyList<ClinicalAiAmbiguity>? ambiguities = null,
        ClinicalIntentClassification intent = ClinicalIntentClassification.PreTriageInput,
        bool requiresClarification = false) => new(
            ClinicalAiProviderOutput.CurrentSchemaVersion,
            intent,
            pathway,
            facts ?? [],
            symptoms ?? [],
            ambiguities ?? [],
            requiresClarification,
            []);

    private static string CreateJwt()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString("D")),
                new Claim("sid", Guid.NewGuid().ToString("D"))
            ],
            now.AddMinutes(-1).UtcDateTime,
            now.AddMinutes(10).UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FixedProvider(ClinicalAiProviderOutput output) : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(output);
        }
    }

    private sealed class ThrowingProvider(Exception exception) : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<ClinicalAiProviderOutput>(exception);
        }
    }

    private sealed record ClinicalCounts(
        int Sessions,
        int Answers,
        int Episodes,
        int Symptoms,
        int Assessments,
        int History,
        int FhirExports,
        int Amendments);

    private sealed record InterpretationResponse(
        string Resolution,
        string? Pathway,
        IReadOnlyList<string>? CandidatePathways,
        CandidateValuesResponse CandidateValues);

    private sealed record CandidateValuesResponse(
        DurationResponse? Duration,
        int? Intensity,
        IReadOnlyList<string>? AdditionalSymptoms);

    private sealed record DurationResponse(decimal Value, string Unit);
}
