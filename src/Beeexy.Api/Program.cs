using Beeexy.Api.Configuration;
using Beeexy.Api.ClinicDirectory;
using Beeexy.Api.DoctorDirectory;
using Beeexy.Api.Operations;
using Beeexy.Api.Errors;
using Beeexy.Api.Health;
using Beeexy.Api.History;
using Beeexy.Api.Identity;
using Beeexy.Api.Interoperability;
using Beeexy.Api.Middleware;
using Beeexy.Api.Patients;
using Beeexy.Api.PrivateAccess;
using Beeexy.Api.Scheduling;
using Beeexy.Api.Triage;
using Beeexy.Application.Identity;
using Beeexy.Application.Directory;
using Beeexy.Application.History;
using Beeexy.Application.Interoperability;
using Beeexy.Application.Patients;
using Beeexy.Application.Scheduling;
using Beeexy.Application.Triage;
using Beeexy.Infrastructure;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var databasePrivateAccessCommand = PrivateAccessCli.IsDatabaseCommand(args);
var provisionDemoGuestCommand = PrivateAccessCli.IsProvisionDemoGuestCommand(args);
var phase7DemoDirectoryCommand = Phase7DemoDirectoryCli.IsCommand(args);
var phase8DemoAvailabilityCommand = Phase8DemoAvailabilityCli.IsCommand(args);
if (PrivateAccessCli.TryRun(args))
{
    return;
}

if (phase7DemoDirectoryCommand)
{
    var commandConfiguration = new ConfigurationManager();
    commandConfiguration.AddEnvironmentVariables();
    await Phase7DemoDirectoryCli.ExecuteAsync(
        commandConfiguration,
        commandConfiguration["ASPNETCORE_ENVIRONMENT"],
        cancellationToken: CancellationToken.None);
    return;
}

if (phase8DemoAvailabilityCommand)
{
    var commandConfiguration = new ConfigurationManager();
    commandConfiguration.AddEnvironmentVariables();
    await Phase8DemoAvailabilityCli.ExecuteAsync(
        args,
        commandConfiguration,
        commandConfiguration["ASPNETCORE_ENVIRONMENT"],
        cancellationToken: CancellationToken.None);
    return;
}

var builder = WebApplication.CreateBuilder(
    databasePrivateAccessCommand || provisionDemoGuestCommand ? [] : args);

if (databasePrivateAccessCommand)
{
    var commandConnectionString = StartupConfiguration.GetRequiredDatabaseConnectionString(
        builder.Configuration);
    builder.Services.AddDbContext<BeeexyDbContext>(options => options.UseNpgsql(
        commandConnectionString,
        npgsql => npgsql.MigrationsAssembly(typeof(BeeexyDbContext).Assembly.FullName)));
    builder.Services.AddSingleton<IPrivateAccessSecretHasher, Pbkdf2PrivateAccessSecretHasher>();
    builder.Services.AddSingleton<IPrivateAccessAuditLogger, PrivateAccessAuditLogger>();
    await using var commandApp = builder.Build();
    PrivateAccessSettings? legacySettings = args[1] == "migrate-demo-guest"
        ? StartupConfiguration.GetPrivateAccessSettings(builder.Configuration, builder.Environment)
        : null;
    await PrivateAccessCli.RunDatabaseCommandAsync(
        args,
        commandApp.Services,
        legacySettings,
        CancellationToken.None);
    return;
}

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
var clinicalAiProviderOptions = StartupConfiguration.GetClinicalAiProviderOptions(
    builder.Configuration);
var preTriageEducationalVideoOptions =
    StartupConfiguration.GetRequiredPreTriageEducationalVideoOptions(builder.Configuration);
var privateAccessSettings = StartupConfiguration.GetPrivateAccessSettings(
    builder.Configuration,
    builder.Environment);

builder.Services.AddInfrastructure(
    databaseConnectionString,
    emailChallengeSettings.Policy,
    authenticationTokenPolicy,
    new GoogleExternalIdentityOptions(
        googleAuthenticationSettings.Enabled,
    googleAuthenticationSettings.ClientId),
    emailChallengeSettings.OtpHashingKey,
    emailChallengeSettings.EmailSender,
    preTriageCleanupOptions,
    clinicalAiProviderOptions,
    preTriageEducationalVideoOptions);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<DevelopmentDemoDefinitionsBootstrapper>();
}
builder.Services.AddScoped<RequestEmailChallenge>();
builder.Services.AddScoped<ListClinics>();
builder.Services.AddScoped<GetClinic>();
builder.Services.AddScoped<SearchDoctors>();
builder.Services.AddScoped<GetDoctor>();
builder.Services.AddScoped<ListAvailableSlots>();
builder.Services.AddScoped<RequestAppointment>();
builder.Services.AddSingleton<DeterministicDoctorMatchEngine>();
builder.Services.AddScoped<CalculateDoctorMatch>();
builder.Services.AddScoped<ProvisionAccountAndPrimaryProfile>();
builder.Services.AddScoped<ProvisionDemoGuest>();
builder.Services.AddScoped<VerifyEmailChallenge>();
builder.Services.AddScoped<AuthenticateWithGoogle>();
builder.Services.AddScoped<IssueAuthenticationTokens>();
builder.Services.AddScoped<IssueDemoGuestSession>();
builder.Services.AddScoped<AuthenticatePrivateAccess>();
builder.Services.AddScoped<ResolvePrivateAccessSession>();
builder.Services.AddScoped<LogoutPrivateAccessSession>();
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
builder.Services.AddScoped<InterpretPreTriageIntake>();
builder.Services.AddScoped<StartPreTriageFromIntake>();
builder.Services.AddScoped<ReplayPreTriageIntake>();
builder.Services.AddScoped<SubmitTriageAnswers>();
builder.Services.AddScoped<GetPreTriageConversationState>();
builder.Services.AddScoped<ResolvePreTriageEducationalVideoOffer>();
builder.Services.AddScoped<CheckDemoQuestionnaireCompleteness>();
builder.Services.AddScoped<NeutralClinicalAssessmentFactory>();
builder.Services.AddScoped<CompletePreTriage>();
builder.Services.AddScoped<GetPreTriageResult>();
builder.Services.AddScoped<ClaimAnonymousPreTriage>();
builder.Services.AddScoped<ProjectCompletedPreTriageEpisode>();
builder.Services.AddScoped<ListClinicalHistory>();
builder.Services.AddScoped<GetClinicalHistoryEvent>();
builder.Services.AddScoped<AmendPreTriageEpisode>();
builder.Services.AddScoped<CreateFhirExport>();
builder.Services.AddScoped<GetFhirExport>();
builder.Services.AddScoped<DownloadFhirExport>();
builder.Services.AddScoped<IPreTriageHistoryProjector>(provider =>
    provider.GetRequiredService<ProjectCompletedPreTriageEpisode>());
builder.Services.AddScoped<ExpireAnonymousPreTriage>();
builder.Services.AddScoped<PreTriageCleanupService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSessionIdentity, HttpCurrentSessionIdentity>();
builder.Services.AddSingleton(privateAccessSettings);
builder.Services.AddSingleton<PrivateAccessCredentialValidator>();
builder.Services.AddSingleton<PrivateAccessSessionTokenService>();
builder.Services.AddSingleton<InMemoryPrivateAccessRateLimiter>();
builder.Services.AddSingleton(new PrivateAccessRateLimitPolicy(
    privateAccessSettings.LoginPermitLimit,
    privateAccessSettings.LoginRateLimitWindow));
builder.Services.AddScoped<IPrivateAccessRateLimiter, PostgreSqlPrivateAccessRateLimiter>();
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
            .AllowAnyMethod()
            .AllowCredentials());
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
    options.DocumentFilter<PreTriageIntakeOpenApiDocumentFilter>();
    options.SchemaFilter<PatientDemographicsSchemaFilter>();
});


var app = builder.Build();

if (provisionDemoGuestCommand)
{
    await PrivateAccessCli.ProvisionDemoGuestAsync(
        app.Services,
        privateAccessSettings,
        CancellationToken.None);
    return;
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();
app.UseCors(StartupConfiguration.CorsPolicyName);
app.UseMiddleware<PrivateAccessGateMiddleware>();
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
app.MapBeeexyClinicDirectoryEndpoints();
app.MapBeeexyDoctorDirectoryEndpoints();
app.MapBeeexyAvailabilityEndpoints();
app.MapBeeexyAppointmentEndpoints();
app.MapBeeexyPrivateAccessEndpoints();
app.MapBeeexyAuthenticationEndpoints();
app.MapBeeexyPatientEndpoints();
app.MapBeeexyClinicalHistoryEndpoints();
app.MapBeeexyFhirExportEndpoints();
app.MapBeeexyCareRelationshipEndpoints();
app.MapBeeexyPreTriageEndpoints();

app.Run();

public partial class Program
{
}
