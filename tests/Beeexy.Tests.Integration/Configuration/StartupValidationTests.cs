using Beeexy.Tests.Integration.Support;
using Beeexy.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Integration.Configuration;

[Collection(PostgreSqlCollection.Name)]
public sealed class StartupValidationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public void MissingDatabaseConnectionString_FailsFastWithoutLeakingOtherSettings()
    {
        using var factory = new BeeexyApiFactory(string.Empty);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("BeeexyDatabase", exception.ToString());
        Assert.DoesNotContain(BeeexyApiFactory.AllowedCorsOrigin, exception.ToString());
    }

    [Fact]
    public async Task ValidPhaseOneConfiguration_StartsSuccessfully()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/live");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void Production_DoesNotRegisterInMemoryAuthenticationEmailSender()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production);
        using var client = factory.CreateApiClient();

        Assert.Null(factory.Services.GetService<InMemoryAuthenticationEmailSender>());
    }
}
