using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly Action<IServiceCollection>? _configureServices;

    public BeeexyApiFactory(
        string connectionString,
        string environment = "Development",
        string allowedCorsOrigin = AllowedCorsOrigin,
        ILoggerProvider? loggerProvider = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _connectionString = connectionString;
        _environment = environment;
        _allowedCorsOrigin = allowedCorsOrigin;
        _loggerProvider = loggerProvider;
        var overrides = new Dictionary<string, string?>
        {
            ["Authentication:EmailChallenge:OtpHashingKey"] =
                "integration-test-only-hmac-key-with-at-least-32-bytes",
            ["Authentication:Tokens:SigningKey"] =
                "integration-test-only-jwt-signing-key-with-at-least-32-bytes",
            ["Authentication:EmailSender:Provider"] =
                string.Equals(environment, Environments.Production, StringComparison.OrdinalIgnoreCase)
                    ? "Unavailable"
                    : "InMemory"
        };
        if (configurationOverrides is not null)
        {
            foreach (var (key, value) in configurationOverrides)
            {
                overrides[key] = value;
            }
        }

        _configurationOverrides = overrides;
        _configureServices = configureServices;
    }

    public HttpClient CreateApiClient()
    {
        const string connectionStringVariable = "ConnectionStrings__BeeexyDatabase";
        const string corsOriginVariable = "Cors__AllowedOrigins__0";
        const string environmentVariable = "ASPNETCORE_ENVIRONMENT";

        var previousConnectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
        var previousCorsOrigin = Environment.GetEnvironmentVariable(corsOriginVariable);
        var previousEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        var previousOverrides = _configurationOverrides.ToDictionary(
            setting => setting.Key.Replace(":", "__", StringComparison.Ordinal),
            setting => Environment.GetEnvironmentVariable(
                setting.Key.Replace(":", "__", StringComparison.Ordinal)),
            StringComparer.OrdinalIgnoreCase);

        Environment.SetEnvironmentVariable(connectionStringVariable, _connectionString);
        Environment.SetEnvironmentVariable(corsOriginVariable, _allowedCorsOrigin);
        Environment.SetEnvironmentVariable(environmentVariable, _environment);
        foreach (var (key, value) in _configurationOverrides)
        {
            Environment.SetEnvironmentVariable(
                key.Replace(":", "__", StringComparison.Ordinal),
                value);
        }

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
            foreach (var (key, value) in previousOverrides)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(_configurationOverrides));

        if (_loggerProvider is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(_loggerProvider));
        }

        if (_configureServices is not null)
        {
            builder.ConfigureServices(_configureServices);
        }
    }
}
