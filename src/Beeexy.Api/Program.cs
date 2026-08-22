using Beeexy.Api.Configuration;
using Beeexy.Api.Errors;
using Beeexy.Api.Health;
using Beeexy.Api.Identity;
using Beeexy.Api.Middleware;
using Beeexy.Api.Patients;
using Beeexy.Api.Triage;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Infrastructure;
using Beeexy.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

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
var authenticationTokenPolicy = StartupConfiguration.GetRequiredAuthenticationTokenPolicy(
    builder.Configuration);
var googleAuthenticationSettings = StartupConfiguration.GetGoogleAuthenticationSettings(
    builder.Configuration);
var preTriageCleanupOptions = StartupConfiguration.GetRequiredPreTriageCleanupOptions(
    builder.Configuration);

builder.Services.AddInfrastructure(
    databaseConnectionString,
    emailChallengeSettings.Policy,
    authenticationTokenPolicy,
    new GoogleExternalIdentityOptions(
        googleAuthenticationSettings.Enabled,
    googleAuthenticationSettings.ClientId),
    emailChallengeSettings.OtpHashingKey,
    emailChallengeSettings.EmailSender,
    preTriageCleanupOptions);
builder.Services.AddScoped<RequestEmailChallenge>();
builder.Services.AddScoped<ProvisionAccountAndPrimaryProfile>();
builder.Services.AddScoped<VerifyEmailChallenge>();
builder.Services.AddScoped<AuthenticateWithGoogle>();
builder.Services.AddScoped<IssueAuthenticationTokens>();
builder.Services.AddScoped<RotateRefreshSession>();
builder.Services.AddScoped<LogoutSession>();
builder.Services.AddScoped<CurrentAccountProfileResolver>();
builder.Services.AddScoped<GetCurrentAccount>();
builder.Services.AddScoped<GetPrimaryProfile>();
builder.Services.AddScoped<UpdatePrimaryProfile>();
builder.Services.AddScoped<CreateManagedPatient>();
builder.Services.AddScoped<ListAccessiblePatients>();
builder.Services.AddScoped<ListCareRelationships>();
builder.Services.AddScoped<RevokeCareRelationship>();
builder.Services.AddScoped<AuthorizePatientAccess>();
builder.Services.AddScoped<GetPatientProfile>();
builder.Services.AddScoped<UpdateManagedPatient>();
builder.Services.AddScoped<StartPreTriage>();
builder.Services.AddScoped<SubmitTriageAnswers>();
builder.Services.AddScoped<CheckDemoQuestionnaireCompleteness>();
builder.Services.AddScoped<NeutralClinicalAssessmentFactory>();
builder.Services.AddScoped<CompletePreTriage>();
builder.Services.AddScoped<GetPreTriageResult>();
builder.Services.AddScoped<ClaimAnonymousPreTriage>();
builder.Services.AddScoped<ProjectCompletedPreTriageEpisode>();
builder.Services.AddScoped<IPreTriageHistoryProjector>(provider =>
    provider.GetRequiredService<ProjectCompletedPreTriageEpisode>());
builder.Services.AddScoped<ExpireAnonymousPreTriage>();
builder.Services.AddScoped<PreTriageCleanupService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSessionIdentity, HttpCurrentSessionIdentity>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(authenticationTokenPolicy.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = authenticationTokenPolicy.Issuer,
            ValidateAudience = true,
            ValidAudience = authenticationTokenPolicy.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
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
    options.AddSecurityDefinition(
        JwtBearerDefaults.AuthenticationScheme,
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Signed Beeexy access token."
        });
    options.DocumentFilter<BearerAuthorizationDocumentFilter>();
    options.SchemaFilter<PatientDemographicsSchemaFilter>();
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
app.UseAuthentication();
app.UseAuthorization();

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
app.MapBeeexyPatientEndpoints();
app.MapBeeexyCareRelationshipEndpoints();
app.MapBeeexyPreTriageEndpoints();

app.Run();

public partial class Program
{
}
