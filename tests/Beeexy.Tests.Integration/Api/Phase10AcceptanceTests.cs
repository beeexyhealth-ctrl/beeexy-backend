using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase108")]
public sealed class Phase10AcceptanceTests(PostgreSqlContainerFixture postgres)
{
    private static readonly Guid UnknownId =
        Guid.Parse("10000000-0000-0000-0000-000000000008");

    [Fact]
    public async Task CompleteEndpointMatrix_RequiresBearerBeforeLookupValidationOrProviderCall()
    {
        await EnsureMigratedAsync();
        var provider = new CountingProvider();
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        using var upload = new MultipartFormDataContent
        {
            { new ByteArrayContent("private notes"u8.ToArray()), "file", "notes.txt" }
        };
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/conversations")
            {
                Content = JsonContent.Create(new { purpose = "GENERAL_HEALTH" })
            },
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/ai/conversations"),
            new HttpRequestMessage(HttpMethod.Get, ConversationEndpoint(UnknownId)),
            new HttpRequestMessage(HttpMethod.Post, MessageEndpoint(UnknownId))
            {
                Content = JsonContent.Create(new { content = "Explain a health term." })
            },
            new HttpRequestMessage(HttpMethod.Delete, ConversationEndpoint(UnknownId)),
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/documents")
            {
                Content = upload
            },
            new HttpRequestMessage(HttpMethod.Delete, DocumentEndpoint(UnknownId)),
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/second-opinions")
            {
                Content = JsonContent.Create(new
                {
                    patientId = UnknownId,
                    text = "Explain this health information."
                })
            },
            new HttpRequestMessage(HttpMethod.Get, SecondOpinionEndpoint(UnknownId)),
            new HttpRequestMessage(HttpMethod.Post, RegenerationEndpoint(UnknownId))
        };

        foreach (var request in requests)
        {
            using (request)
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task OpenApi_ContainsExactlyTheApprovedBearerSecuredPhase10Matrix()
    {
        await EnsureMigratedAsync();
        using var factory = Factory(new CountingProvider());
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["/api/v1/ai/conversations"] = ["get", "post"],
            ["/api/v1/ai/conversations/{id}"] = ["get", "delete"],
            ["/api/v1/ai/conversations/{id}/messages"] = ["post"],
            ["/api/v1/ai/documents"] = ["post"],
            ["/api/v1/ai/documents/{id}"] = ["delete"],
            ["/api/v1/ai/second-opinions"] = ["post"],
            ["/api/v1/ai/second-opinions/{id}"] = ["get"],
            ["/api/v1/ai/second-opinions/{id}/regenerate"] = ["post"]
        };
        var actualAiPaths = paths.EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/v1/ai/", StringComparison.Ordinal))
            .ToDictionary(
                path => path.Name,
                path => path.Value.EnumerateObject().Select(operation => operation.Name).ToArray(),
                StringComparer.Ordinal);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(51, paths.EnumerateObject().Count());
        Assert.Equal(expected.Keys.Order(), actualAiPaths.Keys.Order());
        foreach (var (path, methods) in expected)
        {
            Assert.Equal(methods.Order(), actualAiPaths[path].Order());
            foreach (var method in methods)
            {
                Assert.NotEmpty(paths.GetProperty(path).GetProperty(method)
                    .GetProperty("security").EnumerateArray());
            }
        }

        var aiContract = string.Join('\n', expected.SelectMany(pair => pair.Value.Select(method =>
            paths.GetProperty(pair.Key).GetProperty(method).GetRawText())));
        Assert.DoesNotContain("restrictedAuditOutput", aiContract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", aiContract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemInstructions", aiContract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerPayload", aiContract, StringComparison.OrdinalIgnoreCase);
    }

    private BeeexyApiFactory Factory(CountingProvider provider) => new(
        postgres.ConnectionString,
        configureServices: services =>
        {
            services.RemoveAll<IAiProvider>();
            services.AddSingleton<IAiProvider>(provider);
        });

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
        await dbContext.Database.MigrateAsync();
    }

    private static string ConversationEndpoint(Guid id) =>
        $"/api/v1/ai/conversations/{id:D}";

    private static string MessageEndpoint(Guid id) =>
        $"/api/v1/ai/conversations/{id:D}/messages";

    private static string DocumentEndpoint(Guid id) =>
        $"/api/v1/ai/documents/{id:D}";

    private static string SecondOpinionEndpoint(Guid id) =>
        $"/api/v1/ai/second-opinions/{id:D}";

    private static string RegenerationEndpoint(Guid id) =>
        $"/api/v1/ai/second-opinions/{id:D}/regenerate";

    private sealed class CountingProvider : IAiProvider
    {
        public int CallCount { get; private set; }

        public string ProviderIdentifier => "phase-10-acceptance-provider";

        public string ModelIdentifier => "phase-10-acceptance-model";

        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Authentication must precede execution.");
        }
    }
}
