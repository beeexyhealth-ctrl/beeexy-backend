using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beeexy.Tests.Integration.Support;

internal sealed class BeeexyApiFactory : WebApplicationFactory<Program>
{
    public const string AllowedCorsOrigin = "https://frontend.beeexy.test";

    private readonly string _connectionString;
    private readonly string _environment;
    private readonly string _allowedCorsOrigin;
    private readonly ILoggerProvider? _loggerProvider;

    public BeeexyApiFactory(
        string connectionString,
        string environment = "Development",
        string allowedCorsOrigin = AllowedCorsOrigin,
        ILoggerProvider? loggerProvider = null)
    {
        _connectionString = connectionString;
        _environment = environment;
        _allowedCorsOrigin = allowedCorsOrigin;
        _loggerProvider = loggerProvider;
    }

    public HttpClient CreateApiClient()
    {
        const string connectionStringVariable = "ConnectionStrings__BeeexyDatabase";
        const string corsOriginVariable = "Cors__AllowedOrigins__0";
        const string environmentVariable = "ASPNETCORE_ENVIRONMENT";

        var previousConnectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
        var previousCorsOrigin = Environment.GetEnvironmentVariable(corsOriginVariable);
        var previousEnvironment = Environment.GetEnvironmentVariable(environmentVariable);

        Environment.SetEnvironmentVariable(connectionStringVariable, _connectionString);
        Environment.SetEnvironmentVariable(corsOriginVariable, _allowedCorsOrigin);
        Environment.SetEnvironmentVariable(environmentVariable, _environment);

        try
        {
            return CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://beeexy.test")
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionStringVariable, previousConnectionString);
            Environment.SetEnvironmentVariable(corsOriginVariable, previousCorsOrigin);
            Environment.SetEnvironmentVariable(environmentVariable, previousEnvironment);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        if (_loggerProvider is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(_loggerProvider));
        }
    }
}
