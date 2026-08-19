using Beeexy.Api.Configuration;
using Beeexy.Api.Errors;
using Beeexy.Api.Health;
using Beeexy.Api.Identity;
using Beeexy.Api.Middleware;
using Beeexy.Application.Identity;
using Beeexy.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

var databaseConnectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
    builder.Configuration);
var corsAllowedOrigins = StartupConfiguration.GetRequiredCorsAllowedOrigins(
    builder.Configuration);
var emailChallengeSettings = StartupConfiguration.GetRequiredEmailChallengeSettings(
    builder.Configuration,
    builder.Environment);

builder.Services.AddInfrastructure(
    databaseConnectionString,
    emailChallengeSettings.Policy,
    emailChallengeSettings.OtpHashingKey,
    emailChallengeSettings.UseInMemoryEmailSender);
builder.Services.AddScoped<RequestEmailChallenge>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        StartupConfiguration.CorsPolicyName,
        policy => policy
            .WithOrigins(corsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [HealthCheckTags.Live])
    .AddCheck<PostgreSqlHealthCheck>(
        "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: [HealthCheckTags.Ready],
        timeout: TimeSpan.FromSeconds(5));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Beeexy API",
        Version = "v1"
    });
});


var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();
app.UseCors(StartupConfiguration.CorsPolicyName);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Beeexy API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Beeexy API v1");
    });
}

app.MapBeeexyHealthEndpoints();
app.MapBeeexyAuthenticationEndpoints();

app.Run();

public partial class Program
{
}
