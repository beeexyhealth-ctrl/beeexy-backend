using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Npgsql;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class DevelopmentDemoDefinitionBootstrapTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private readonly string _databaseName = $"beeexy_phase4_bootstrap_{Guid.NewGuid():N}";
    private string ConnectionString => new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
    {
        Database = _databaseName,
        Pooling = false
    }.ConnectionString;

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    public async Task FreshDevelopmentDatabase_StartsSessionsForEveryDemoPathway(string pathway)
    {
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            SimplifiedDemoDefinitionPackages.VersionIdentifier,
            body.RootElement.GetProperty("questionnaire").GetProperty("version").GetString());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
        await command.ExecuteNonQueryAsync();
    }
}
