using Testcontainers.PostgreSql;

namespace Beeexy.Tests.Integration.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "Phase 1 PostgreSQL";
}

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("beeexy_phase1_tests")
        .WithUsername("beeexy")
        .WithPassword("phase1-test-only-password")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
