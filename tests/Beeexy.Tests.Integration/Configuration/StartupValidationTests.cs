using Beeexy.Tests.Integration.Support;
using Beeexy.Application.Identity;
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
        Assert.Equal(
            "ResendAuthenticationEmailSender",
            factory.Services.GetRequiredService<IAuthenticationEmailSender>().GetType().Name);
    }

    [Fact]
    public void Production_WithMissingEmailApiKey_FailsFastWithoutLeakingSenderSettings()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:EmailSender:Resend:ApiKey"] = ""
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("Authentication:EmailSender:Resend", exception.ToString());
        Assert.DoesNotContain("auth@beeexy.test", exception.ToString());
        Assert.DoesNotContain(postgres.ConnectionString, exception.ToString());
    }

    [Fact]
    public void Production_WithInvalidSenderIdentity_FailsFast()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:EmailSender:Resend:SenderEmail"] = "not-an-email"
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("Authentication:EmailSender:Resend", exception.ToString());
    }

    [Fact]
    public void Production_WithInMemoryEmailSender_FailsFast()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:EmailSender:Provider"] = "InMemory"
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("cannot be used in Production", exception.ToString());
    }

    [Fact]
    public void GoogleEnabledWithoutClientId_FailsFast()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = ""
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("Authentication:Google:ClientId", exception.ToString());
        Assert.DoesNotContain(postgres.ConnectionString, exception.ToString());
    }
}
