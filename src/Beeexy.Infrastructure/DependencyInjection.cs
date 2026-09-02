using Beeexy.Application.Ai;
using Beeexy.Application.Directory;
using Beeexy.Application.Identity;
using Beeexy.Application.History;
using Beeexy.Application.Interoperability;
using Beeexy.Application.Patients;
using Beeexy.Application.Scheduling;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Ai;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.History;
using Beeexy.Infrastructure.Interoperability;
using Beeexy.Infrastructure.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Scheduling;
using Beeexy.Infrastructure.Time;
using Beeexy.Infrastructure.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAppointmentOperationsInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<PublicDirectoryQueryBoundary>();
        services.AddScoped<IAppointmentTransitionTransaction, AppointmentTransitionTransaction>();
        services.AddScoped<
            IAppointmentOperationsReadRepository,
            AppointmentOperationsReadRepository>();
        services.AddDbContext<BeeexyDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BeeexyDbContext).Assembly.FullName)));
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        EmailChallengePolicy emailChallengePolicy,
        AuthenticationTokenPolicy authenticationTokenPolicy,
        GoogleExternalIdentityOptions googleOptions,
        string otpHashingKey,
        AuthenticationEmailSenderOptions authenticationEmailSenderOptions,
        PreTriageCleanupOptions preTriageCleanupOptions,
        ClinicalAiProviderOptions clinicalAiProviderOptions,
        PreTriageEducationalVideoOptions preTriageEducationalVideoOptions,
        string? privateFhirArtifactRoot = null,
        AiDocumentOptions? aiDocumentOptions = null,
        string? privateAiDocumentRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(emailChallengePolicy);
        ArgumentNullException.ThrowIfNull(authenticationTokenPolicy);
        ArgumentNullException.ThrowIfNull(googleOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(otpHashingKey);
        ArgumentNullException.ThrowIfNull(authenticationEmailSenderOptions);
        ArgumentNullException.ThrowIfNull(preTriageCleanupOptions);
        ArgumentNullException.ThrowIfNull(clinicalAiProviderOptions);
        ArgumentNullException.ThrowIfNull(preTriageEducationalVideoOptions);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<DirectoryImportPackageValidator>();
        services.AddScoped<IDirectoryImporter, DirectoryImporter>();
        services.AddSingleton<DoctorMatchRulePackageValidator>();
        services.AddScoped<IDoctorMatchRuleImporter, DoctorMatchRuleImporter>();
        services.AddScoped<PublicDirectoryQueryBoundary>();
        services.AddScoped<IClinicDirectoryReadRepository, ClinicDirectoryReadRepository>();
        services.AddScoped<IDoctorDirectoryReadRepository, DoctorDirectoryReadRepository>();
        services.AddScoped<IDoctorMatchingRepository, DoctorMatchingRepository>();
        services.AddSingleton<AvailabilityImportPackageValidator>();
        services.AddScoped<IAvailabilityImporter, AvailabilityImporter>();
        services.AddScoped<IAvailabilitySlotReadRepository, AvailabilitySlotReadRepository>();
        services.AddScoped<IAppointmentRequestTransaction, AppointmentRequestTransaction>();
        services.AddScoped<IAppointmentTransitionTransaction, AppointmentTransitionTransaction>();
        services.AddScoped<IAppointmentRescheduleTransaction, AppointmentTransitionTransaction>();
        services.AddScoped<IAppointmentReadRepository, AppointmentReadRepository>();
        services.AddScoped<
            IAppointmentOperationsReadRepository,
            AppointmentOperationsReadRepository>();
        services.AddSingleton(emailChallengePolicy);
        services.AddSingleton(authenticationTokenPolicy);
        services.AddSingleton<IOneTimePasswordGenerator, CryptographicOneTimePasswordGenerator>();
        services.AddSingleton<IOneTimePasswordHasher>(
            _ => new HmacOneTimePasswordHasher(otpHashingKey));
        services.AddSingleton<IEmailChallengeRateLimiter, InMemoryEmailChallengeRateLimiter>();
        services.AddScoped<
            IEmailAuthenticationChallengeRepository,
            EmailAuthenticationChallengeRepository>();
        services.AddScoped<IAccountProvisioningRepository, AccountProvisioningRepository>();
        services.AddScoped<IDemoGuestAccountRepository, DemoGuestAccountRepository>();
        services.AddSingleton<IPrivateAccessSecretHasher, Pbkdf2PrivateAccessSecretHasher>();
        services.AddSingleton<IPrivateAccessTokenService, CryptographicPrivateAccessTokenService>();
        services.AddSingleton<IPrivateAccessAuditLogger, PrivateAccessAuditLogger>();
        services.AddScoped<IPrivateAccessRepository, PrivateAccessRepository>();
        services.AddScoped<IIdentityVerificationTransaction, IdentityVerificationTransaction>();
        services.AddSingleton<IRefreshTokenService, CryptographicRefreshTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IAuthenticationSecurityLogger, AuthenticationSecurityLogger>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<
            IExternalIdentityAuthenticationRepository,
            ExternalIdentityAuthenticationRepository>();
        services.AddScoped<ICurrentAccountProfileRepository, CurrentAccountProfileRepository>();
        services.AddSingleton<IAccountProfileAuditLogger, AccountProfileAuditLogger>();
        services.AddScoped<IManagedPatientCreationRepository, ManagedPatientCreationRepository>();
        services.AddScoped<
            ICareRelationshipRevocationRepository,
            CareRelationshipRevocationRepository>();
        services.AddSingleton<ICareRelationshipAuditLogger, CareRelationshipAuditLogger>();
        services.AddScoped<IMyCircleReadRepository, MyCircleReadRepository>();
        services.AddSingleton<IMyCircleAuditLogger, MyCircleAuditLogger>();
        services.AddScoped<
            IPatientAccessAuthorizationRepository,
            PatientAccessAuthorizationRepository>();
        services.AddScoped<IPatientProfileReadRepository, PatientProfileReadRepository>();
        services.AddScoped<IPatientProfileUpdateRepository, PatientProfileUpdateRepository>();
        services.AddScoped<IClinicalHistoryReadRepository, ClinicalHistoryReadRepository>();
        services.AddScoped<
            IClinicalHistoryEventReadRepository,
            ClinicalHistoryEventReadRepository>();
        services.AddScoped<IPreTriageAmendmentRepository, PreTriageAmendmentRepository>();
        services.AddSingleton<IClinicalAmendmentAuditLogger, ClinicalAmendmentAuditLogger>();
        services.AddSingleton<IPatientProfileAuditLogger, PatientProfileAuditLogger>();
        services.AddSingleton<ClinicalDefinitionPackageValidator>();
        services.AddScoped<IClinicalDefinitionImporter, ClinicalDefinitionImporter>();
        services.AddScoped<IClinicalDefinitionProvider, ClinicalDefinitionProvider>();
        services.AddScoped<IClinicalPathwayRegistry, ClinicalPathwayRegistry>();
        if (clinicalAiProviderOptions.TryCreateNvidia(out var nvidiaOptions))
        {
            var configuredNvidiaOptions = nvidiaOptions!;
            services.AddSingleton(configuredNvidiaOptions);
            services.AddHttpClient(NvidiaClinicalAiProvider.HttpClientName, client =>
            {
                client.BaseAddress = configuredNvidiaOptions.BaseUri;
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            });
            services.AddSingleton(provider => new NvidiaClinicalAiProvider(
                provider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(NvidiaClinicalAiProvider.HttpClientName),
                configuredNvidiaOptions));
            services.AddSingleton<IClinicalAiProvider>(provider =>
                provider.GetRequiredService<NvidiaClinicalAiProvider>());
            services.AddSingleton<IAiProvider>(provider =>
                provider.GetRequiredService<NvidiaClinicalAiProvider>());
        }
        else
        {
            services.AddSingleton<UnavailableClinicalAiProvider>();
            services.AddSingleton<IClinicalAiProvider>(provider =>
                provider.GetRequiredService<UnavailableClinicalAiProvider>());
            services.AddSingleton<IAiProvider>(provider =>
                provider.GetRequiredService<UnavailableClinicalAiProvider>());
        }
        services.AddSingleton<IAiPromptResolver, AiPromptResolver>();
        services.AddSingleton<IAiStructuredResultValidator, AiStructuredResultValidator>();
        services.AddSingleton<IAiPromptContract, AiConversationPromptV1>();
        services.AddSingleton<IAiPromptContract, SecondOpinionPromptV1>();
        services.AddSingleton<IAiStructuredResultSchema, AiConversationResultSchemaV1>();
        services.AddSingleton<IAiStructuredResultSchema, SecondOpinionResultSchemaV1>();
        services.AddScoped<IAiConversationRepository, AiConversationRepository>();
        services.AddScoped<ISecondOpinionRepository, SecondOpinionRepository>();
        services.AddScoped<IAiExecutionRepository, AiExecutionRepository>();
        services.AddSingleton<IAiExecutionTelemetry, AiExecutionTelemetry>();
        services.AddScoped<ExecuteAiAnalysis>();
        services.AddSingleton(AiSafetyProductContent.Current);
        services.AddSingleton(SecondOpinionProductContent.Current);
        services.AddSingleton<IAiSafetyValidator, BeeexyAiSafetyValidator>();
        services.AddScoped<IAiSafetyPersistence, AiSafetyPersistence>();
        services.AddSingleton<IAiSafetyTelemetry, AiSafetyTelemetry>();
        services.AddScoped<ExecuteSafeAiAnalysis>();
        var documentOptions = aiDocumentOptions ?? new AiDocumentOptions(
            AiDocumentOptions.MaximumAllowedBytes,
            TimeSpan.FromMinutes(1),
            100);
        services.AddSingleton(documentOptions);
        services.AddSingleton<IAiDocumentSafetyScanner, BaselineAiDocumentSafetyScanner>();
        services.AddSingleton<IAiDocumentTextExtractor, PdfTxtAiDocumentTextExtractor>();
        services.AddSingleton<IAiDocumentBlobStore>(_ => new FileSystemAiDocumentBlobStore(
            string.IsNullOrWhiteSpace(privateAiDocumentRoot)
                ? Path.Combine(AppContext.BaseDirectory, "private-ai-documents")
                : privateAiDocumentRoot));
        services.AddScoped<IAiDocumentRepository, AiDocumentRepository>();
        services.AddHostedService<AiDocumentExpiryWorker>();
        services.AddSingleton<IClinicalSafetyPolicy, ClinicalSafetyPolicy>();
        services.AddSingleton(preTriageEducationalVideoOptions);
        services.AddSingleton<IPreTriageEducationalVideoCatalog,
            PreTriageEducationalVideoCatalog>();
        services.AddScoped<IClinicalAiOutputValidator, ClinicalAiOutputValidator>();
        services.AddScoped<InterpretClinicalInput>();
        services.AddSingleton<
            IPreTriageInterpretationAuditLogger,
            PreTriageInterpretationAuditLogger>();
        services.AddSingleton<
            IAnonymousPreTriageCapabilityService,
            CryptographicAnonymousPreTriageCapabilityService>();
        services.AddScoped<IPreTriageSessionRepository, PreTriageSessionRepository>();
        services.AddScoped<IPreTriageEducationalVideoOfferRepository,
            PreTriageEducationalVideoOfferRepository>();
        services.AddSingleton<IPreTriageSessionAuditLogger, PreTriageSessionAuditLogger>();
        services.AddScoped<IPreTriageAnswerRepository, PreTriageAnswerRepository>();
        services.AddScoped<
            IPreTriageIntakeReplayRepository,
            PreTriageIntakeReplayRepository>();
        services.AddScoped<
            IPreTriageIntakeOrchestrationTransaction,
            PreTriageIntakeOrchestrationTransaction>();
        services.AddSingleton<IPreTriageIntakeAuditLogger, PreTriageIntakeAuditLogger>();
        services.AddScoped<IPreTriageCompletionRepository, PreTriageCompletionRepository>();
        services.AddSingleton<IPreTriageCompletionAuditLogger, PreTriageCompletionAuditLogger>();
        services.AddScoped<IPreTriageClaimRepository, PreTriageClaimRepository>();
        services.AddSingleton<IPreTriageClaimAuditLogger, PreTriageClaimAuditLogger>();
        services.AddScoped<
            IPreTriageHistoryProjectionRepository,
            PreTriageHistoryProjectionRepository>();
        services.AddSingleton(preTriageCleanupOptions);
        services.AddSingleton(preTriageCleanupOptions.Policy);
        services.AddScoped<IPreTriageCleanupRepository, PreTriageCleanupRepository>();
        services.AddSingleton<IPreTriageCleanupTelemetry, PreTriageCleanupTelemetry>();
        services.AddHostedService<PreTriageCleanupWorker>();
        services.AddSingleton<FhirSnapshotSerializer>();
        services.AddSingleton<IFhirR4BundleSerializer, FirelyFhirR4BundleSerializer>();
        services.AddSingleton<FhirArtifactChecksumCalculator>();
        services.AddScoped<IFhirExportGenerationTransaction, FhirExportGenerationTransaction>();
        services.AddScoped<GenerateFhirExport>();
        services.AddSingleton<IFhirValidationPrerequisiteEvaluator,
            CurrentFhirValidationPrerequisiteEvaluator>();
        services.AddSingleton<FhirValidationDiagnosticSanitizer>();
        services.AddSingleton<IFhirValidator, FirelyFhirR4Validator>();
        services.AddScoped<IFhirExportValidationTransaction,
            FhirExportValidationTransaction>();
        services.AddScoped<ValidateFhirExport>();
        services.AddScoped<IFhirExportReadRepository, FhirExportReadRepository>();
        services.AddSingleton<IFhirExportRuntimeVersionProvider,
            FhirExportRuntimeVersionProvider>();
        services.AddSingleton<IFhirExportAuditLogger, FhirExportAuditLogger>();
        services.AddSingleton<IFhirArtifactStore>(_ => new FileSystemFhirArtifactStore(
            privateFhirArtifactRoot ??
            Path.Combine(AppContext.BaseDirectory, "private-fhir-artifacts")));

        services.AddSingleton(googleOptions);
        if (googleOptions.Enabled)
        {
            services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
            services.AddSingleton<IExternalIdentityProvider, GoogleExternalIdentityProvider>();
        }
        else
        {
            services.AddSingleton<IExternalIdentityProvider, DisabledExternalIdentityProvider>();
        }

        if (authenticationEmailSenderOptions.Provider ==
            AuthenticationEmailSenderProvider.InMemory)
        {
            services.AddSingleton<InMemoryAuthenticationEmailSender>();
            services.AddSingleton<IAuthenticationEmailSender>(provider =>
                provider.GetRequiredService<InMemoryAuthenticationEmailSender>());
        }
        else
        {
            var resendOptions = authenticationEmailSenderOptions.Resend ??
                throw new InvalidOperationException(
                    "Resend authentication email options are required.");
            services.AddSingleton<IAuthenticationEmailSender>(_ =>
                new ResendAuthenticationEmailSender(new HttpClient(), resendOptions));
        }

        services.AddDbContext<BeeexyDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(BeeexyDbContext).Assembly.FullName)));

        return services;
    }
}
