using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Api.Identity;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Beeexy.Api.Triage;

internal static class PreTriageEndpointExtensions
{
    internal const string AnonymousCapabilityHeader = "X-Pre-Triage-Capability";
    internal const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string AnonymousIntakeScopeCookie = "Beeexy.PreTriage.IntakeScope";
    private const string AnonymousIntakeScopePrefix = "pti1.";
    private const int AnonymousIntakeScopeEncodedLength = 43;
    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapBeeexyPreTriageEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/pre-triage/intake",
                StartFromIntakeAsync)
            .WithName("StartPreTriageFromIntake")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Interprets the first natural-language message and, only when RESOLVED, " +
                "atomically creates a normal Pre-Triage session and persists initial values " +
                "that remain valid against that session's pinned questionnaire. RESOLVED " +
                "returns 201 with canonical session and answer/progression contracts; " +
                "AMBIGUOUS and UNRESOLVED return 200 and create no clinical state. Only the " +
                "five authoritative demo pathways are supported. Anonymous and authenticated " +
                "callers preserve normal Pre-Triage ownership semantics, and invalid supplied " +
                "Bearer credentials are never downgraded to anonymous. A required bounded " +
                "Idempotency-Key header durably identifies one logical submission within the " +
                "authenticated account or anonymous browser scope. Repeating the same key and " +
                "text replays the committed session without interpretation; reusing the key " +
                "with different text returns 409. Anonymous replay also requires the original " +
                $"{AnonymousCapabilityHeader} because that one-time secret is stored only as a " +
                "hash. RESOLVED also returns the canonical conversation projection so accepted " +
                "first-message values automatically skip completed interactions.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<InterpretPreTriageIntakeRequest>("application/json")
            .Produces<StartPreTriageFromIntakeResponse>(StatusCodes.Status200OK)
            .Produces<StartPreTriageFromIntakeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/intake/interpret",
                InterpretIntakeAsync)
            .WithName("InterpretPreTriageIntake")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Interprets one pre-session patient message without creating a session or " +
                "persisting clinical state. The response contains only backend-validated " +
                "candidate values and a RESOLVED, AMBIGUOUS, or UNRESOLVED outcome. Only " +
                "HEADACHE, ABDOMINAL_PAIN, CHEST_PAIN, FEVER, and OTHER_SYMPTOMS can be " +
                "authoritative pathway outcomes. The endpoint supports anonymous and " +
                "authenticated callers; an invalid supplied Bearer credential is never " +
                "downgraded to anonymous.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<InterpretPreTriageIntakeRequest>("application/json")
            .Produces<PreTriageIntakeInterpretationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/sessions",
                StartSessionAsync)
            .WithName("StartPreTriageSession")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Starts a temporary session for the explicit supported pathway. With no " +
                "Authorization header the session is anonymous and a capability is returned " +
                "once. With a valid Bearer token, patientId selects an authorized primary or " +
                "actively managed patient; omitting patientId selects the caller's primary " +
                "patient. An invalid supplied credential is never downgraded to anonymous. " +
                "The successful response includes the initial canonical conversation projection.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<StartPreTriageSessionRequest>("application/json")
            .Produces<PreTriageSessionStartResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/sessions/{id:guid}/answers",
                SubmitAnswersAsync)
            .WithName("SubmitPreTriageAnswers")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Submits either explicit structured demo answers or one natural-language " +
                "message (never both) and returns backend-authoritative questionnaire " +
                "progression together with the exact validated values accepted into the " +
                "session. " +
                $"Anonymous sessions require {AnonymousCapabilityHeader}; authenticated " +
                "sessions require an authorized Bearer identity. Structured duration units " +
                "are MINUTES, HOURS, DAYS, WEEKS, or MONTHS; intensity is 1-10; additional " +
                "symptoms are NAUSEA, DIARRHEA, and FEVER when allowed by the pinned package; " +
                "FEVER is excluded when FEVER is the primary pathway. Natural-language " +
                "interpretation may " +
                "return a safe clarification or provider-unavailable outcome without writes. " +
                "Every successful response includes the canonical projection of all accepted " +
                "session values and the next deterministic interaction or Review readiness.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<SubmitPreTriageAnswersRequest>("application/json")
            .Produces<PreTriageAnswerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/sessions/{id:guid}/educational-video-offer",
                ResolveEducationalVideoOfferAsync)
            .WithName("ResolvePreTriageEducationalVideoOffer")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Resolves the optional non-clinical educational video offer with exactly " +
                "WATCH or SKIP. WATCH means the frontend should display the configured public " +
                "video; it does not mean playback completed, understanding was confirmed, or " +
                "consent was given. Both decisions immediately expose the same next unanswered " +
                "clinical interaction. The first accepted decision is persisted separately " +
                "from clinical answers and repeated requests are idempotent. " +
                $"Anonymous sessions require {AnonymousCapabilityHeader}; authenticated " +
                "sessions require current patient authorization.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<ResolveEducationalVideoOfferRequest>("application/json")
            .Produces<ResolveEducationalVideoOfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet(
                "/api/v1/pre-triage/sessions/{id:guid}/conversation",
                GetConversationAsync)
            .WithName("GetPreTriageConversation")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Returns a read-only deterministic conversation projection derived only " +
                "from the session, its accepted answers, and its exact pinned questionnaire " +
                "and rule-set. States are IN_PROGRESS, READY_FOR_REVIEW, and COMPLETED. " +
                "Progress counts accepted required fields only; optional fields do not " +
                "contribute. Before the first unanswered clinical question, configured " +
                "pathways expose an EDUCATIONAL_VIDEO_OFFER with WATCH and SKIP options and " +
                "public delivery metadata. Otherwise IN_PROGRESS returns exactly one clinical " +
                "nextInteraction with a DURATION, SCALE, or MULTI_SELECT input, versioned " +
                "prompt, constraints, and controlled options. READY_FOR_REVIEW remains active " +
                "and requires the existing explicit " +
                "completion flow. Completed sessions are read-only. Expired sessions preserve " +
                "the existing concealed not-found behavior. The projection never invokes AI. " +
                $"Anonymous sessions require {AnonymousCapabilityHeader}; authenticated " +
                "sessions require current patient authorization.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Produces<PreTriageConversationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/sessions/{id:guid}/complete",
                CompleteSessionAsync)
            .WithName("CompletePreTriageSession")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Atomically completes an exact pinned simplified demo questionnaire and " +
                "returns its canonical neutral symptom summary. The first completion returns " +
                $"201; authorized repeats return the same immutable result with 200. Anonymous " +
                $"sessions require {AnonymousCapabilityHeader}; patient sessions require a " +
                "currently authorized Bearer identity. No clinical rules, urgency, disposition, " +
                "diagnosis, recommendation, or probability are produced.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Produces<NeutralPreTriageResultResponse>(StatusCodes.Status201Created)
            .Produces<NeutralPreTriageResultResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet(
                "/api/v1/pre-triage/sessions/{id:guid}/result",
                GetResultAsync)
            .WithName("GetPreTriageResult")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Returns the canonical immutable neutral symptom summary for a completed " +
                $"session. Anonymous sessions require {AnonymousCapabilityHeader}; patient " +
                "sessions require a currently authorized Bearer identity. The response freezes " +
                "the exact questionnaire/package provenance and intentionally omits all clinical " +
                "authority and AI/provider fields.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Produces<NeutralPreTriageResultResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/sessions/{id:guid}/claim",
                ClaimSessionAsync)
            .WithName("ClaimAnonymousPreTriageSession")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Claims an existing completed anonymous episode into the authenticated " +
                $"account's server-derived primary patient. Both Bearer authentication and " +
                $"the original {AnonymousCapabilityHeader} header are required; no patient " +
                "selector is accepted. A same-primary-patient repeat is idempotent, another " +
                "patient receives a privacy-safe conflict, and a first claim is unavailable " +
                "at or after the persisted anonymous expiry boundary.")
            .RequireAuthorization()
            .Produces<ClaimAnonymousPreTriageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> StartFromIntakeAsync(
        InterpretPreTriageIntakeRequest request,
        HttpContext httpContext,
        StartPreTriageFromIntake useCase,
        CurrentAccountProfileResolver currentAccountProfileResolver,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        var callerMode = ResolveCallerMode(httpContext);
        idempotencyKey = GetRequiredIdempotencyKey(httpContext);
        var anonymousScope = callerMode == PreTriageCallerMode.Anonymous
            ? ResolveAnonymousIntakeScope(httpContext)
            : default;
        var callerScope = callerMode == PreTriageCallerMode.Authenticated
            ? $"account:{(await currentAccountProfileResolver.ResolveAsync(cancellationToken)).Account.Id.Value:D}"
            : $"anonymous:{anonymousScope.Value}";
        var result = await useCase.ExecuteAsync(
            new StartPreTriageFromIntakeCommand(
                request.Text,
                callerMode,
                request.UnsupportedFields?.Keys.ToArray() ?? [],
                idempotencyKey,
                callerScope,
                anonymousCapability,
                anonymousScope.WasCreated),
            cancellationToken);
        var response = new StartPreTriageFromIntakeResponse(
            ToApiEnum(result.Resolution),
            result.CandidatePathways.Count == 0
                ? null
                : result.CandidatePathways.Select(value => value.Value).ToArray(),
            result.Session is null ? null : ToResponse(result.Session, includeConversation: false),
            result.InitialAnswers is null
                ? null
                : ToResponse(result.InitialAnswers, includeConversation: false),
            result.InitialAnswers?.Conversation is not null
                ? ToResponse(result.InitialAnswers.Conversation)
                : result.Session?.Conversation is not null
                    ? ToResponse(result.Session.Conversation)
                    : null);
        return result.Resolution == PreTriageIntakeResolution.Resolved
            ? Results.Json(response, statusCode: StatusCodes.Status201Created)
            : Results.Ok(response);
    }

    private static async Task<IResult> InterpretIntakeAsync(
        InterpretPreTriageIntakeRequest request,
        HttpContext httpContext,
        InterpretPreTriageIntake useCase,
        CancellationToken cancellationToken)
    {
        _ = ResolveCallerMode(httpContext);
        var result = await useCase.ExecuteAsync(
            new InterpretPreTriageIntakeCommand(
                request.Text,
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);

        return Results.Ok(new PreTriageIntakeInterpretationResponse(
            ToApiEnum(result.Resolution),
            result.Pathway?.Value,
            result.CandidatePathways.Count == 0
                ? null
                : result.CandidatePathways.Select(value => value.Value).ToArray(),
            ToAcceptedValuesResponse(result.CandidateValues)));
    }

    private static async Task<IResult> ClaimSessionAsync(
        Guid id,
        HttpContext httpContext,
        ClaimAnonymousPreTriage useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        if (httpContext.Request.Query.Count != 0 ||
            httpContext.Request.ContentLength is > 0 ||
            httpContext.Request.Headers.TransferEncoding.Count != 0)
        {
            throw new BadHttpRequestException(
                "Anonymous pre-triage claim does not accept a request body or query parameters.");
        }

        var result = await useCase.ExecuteAsync(
            new ClaimAnonymousPreTriageCommand(
                EntityId.From(id),
                anonymousCapability),
            cancellationToken);
        return Results.Ok(new ClaimAnonymousPreTriageResponse(
            result.SessionId.Value,
            result.EpisodeId.Value,
            result.PatientProfileId.Value,
            result.ClaimedAt));
    }

    private static async Task<IResult> CompleteSessionAsync(
        Guid id,
        HttpContext httpContext,
        CompletePreTriage useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        var callerMode = ResolveCallerMode(httpContext);
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new CompletePreTriageCommand(EntityId.From(id), callerMode, anonymousCapability),
            cancellationToken);
        return Results.Json(
            ToResponse(result.Result),
            statusCode: result.IsNewlyCompleted
                ? StatusCodes.Status201Created
                : StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetResultAsync(
        Guid id,
        HttpContext httpContext,
        GetPreTriageResult useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        var callerMode = ResolveCallerMode(httpContext);
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new GetPreTriageResultQuery(EntityId.From(id), callerMode, anonymousCapability),
            cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> GetConversationAsync(
        Guid id,
        HttpContext httpContext,
        GetPreTriageConversationState useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        var callerMode = ResolveCallerMode(httpContext);
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new GetPreTriageConversationStateQuery(
                EntityId.From(id),
                callerMode,
                anonymousCapability),
            cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> SubmitAnswersAsync(
        Guid id,
        SubmitPreTriageAnswersRequest request,
        HttpContext httpContext,
        SubmitTriageAnswers useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var callerMode = ResolveCallerMode(httpContext);

        var result = await useCase.ExecuteAsync(
            new SubmitTriageAnswersCommand(
                EntityId.From(id),
                callerMode,
                anonymousCapability,
                request.QuestionnaireVersion,
                request.Structured is null
                    ? null
                    : new StructuredTriageAnswerInput(
                        request.Structured.Duration is null
                            ? null
                            : new DurationTriageAnswerInput(
                                request.Structured.Duration.Value,
                                request.Structured.Duration.Unit,
                                request.Structured.Duration.UnsupportedFields?.Keys.ToArray() ?? []),
                        request.Structured.Intensity,
                        request.Structured.AdditionalSymptoms,
                        request.Structured.UnsupportedFields?.Keys.ToArray() ?? []),
                request.NaturalLanguage,
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> ResolveEducationalVideoOfferAsync(
        Guid id,
        ResolveEducationalVideoOfferRequest request,
        HttpContext httpContext,
        ResolvePreTriageEducationalVideoOffer useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new ResolvePreTriageEducationalVideoOfferCommand(
                EntityId.From(id),
                ResolveCallerMode(httpContext),
                anonymousCapability,
                request.Decision,
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);
        return Results.Ok(new ResolveEducationalVideoOfferResponse(
            result.SessionId.Value,
            ToApiEnum(result.Decision),
            result.ResolvedAt,
            result.NewlyResolved,
            ToResponse(result.Conversation)));
    }

    private static async Task<IResult> StartSessionAsync(
        StartPreTriageSessionRequest request,
        HttpContext httpContext,
        StartPreTriage useCase,
        CancellationToken cancellationToken)
    {
        var callerMode = ResolveCallerMode(httpContext);

        var result = await useCase.ExecuteAsync(
            new StartPreTriageCommand(
                request.Pathway,
                request.PatientId.HasValue
                    ? ParsePatientId(request.PatientId.Value)
                    : null,
                callerMode,
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);

        return Results.Json(
            ToResponse(result),
            statusCode: StatusCodes.Status201Created);
    }

    private static EntityId ParsePatientId(Guid patientId)
    {
        if (patientId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        return EntityId.From(patientId);
    }

    private static PreTriageCallerMode ResolveCallerMode(HttpContext httpContext)
    {
        var authorizationSupplied = httpContext.Request.Headers.ContainsKey(
            HeaderNames.Authorization);
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        if (authorizationSupplied && !authenticated)
        {
            throw new SessionAuthenticationException();
        }

        return authenticated
            ? PreTriageCallerMode.Authenticated
            : PreTriageCallerMode.Anonymous;
    }

    private static string GetRequiredIdempotencyKey(HttpContext httpContext)
    {
        var values = httpContext.Request.Headers[IdempotencyKeyHeader];
        if (values.Count != 1)
        {
            throw InvalidIdempotencyKey();
        }

        var value = values[0];
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumIdempotencyKeyLength ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not '~'))
        {
            throw InvalidIdempotencyKey();
        }

        return value;
    }

    private static RequestValidationException InvalidIdempotencyKey() => new(
        "pre_triage.idempotency_key_invalid",
        "A single non-empty Idempotency-Key of at most 128 URL-safe characters is required.");

    private static (string Value, bool WasCreated) ResolveAnonymousIntakeScope(
        HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(
                AnonymousIntakeScopeCookie,
                out var existing) &&
            HasValidAnonymousIntakeScope(existing))
        {
            return (existing, false);
        }

        var generated = AnonymousIntakeScopePrefix + Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        httpContext.Response.Cookies.Append(
            AnonymousIntakeScopeCookie,
            generated,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                IsEssential = true,
                Path = "/api/v1/pre-triage/intake",
                MaxAge = TimeSpan.FromDays(30)
            });
        return (generated, true);
    }

    private static bool HasValidAnonymousIntakeScope(string value) =>
        value.Length == AnonymousIntakeScopePrefix.Length +
            AnonymousIntakeScopeEncodedLength &&
        value.StartsWith(AnonymousIntakeScopePrefix, StringComparison.Ordinal) &&
        value.AsSpan(AnonymousIntakeScopePrefix.Length).IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static PreTriageSessionStartResponse ToResponse(
        StartPreTriageResult result,
        bool includeConversation = true) =>
        new(
            result.SessionId.Value,
            result.PatientProfileId?.Value,
            result.Pathway.Value,
            result.Status.ToString(),
            result.ExpiresAt,
            new ClinicalDefinitionReferenceResponse(
                result.QuestionnaireCode.Value,
                result.QuestionnaireVersion.Value),
            new ClinicalDefinitionReferenceResponse(
                result.RuleSetCode.Value,
                result.RuleSetVersion.Value),
            new ClinicalContentStatusResponse(
                ToApiValue(result.ClinicalContentStatus.Source),
                ToApiValue(result.ClinicalContentStatus.ReviewStatus),
                ToApiValue(result.ClinicalContentStatus.ApprovalStatus)),
            result.AnonymousCapability,
            includeConversation && result.Conversation is not null
                ? ToResponse(result.Conversation)
                : null);

    private static PreTriageAnswerResponse ToResponse(
        SubmitTriageAnswersResult result,
        bool includeConversation = true) => new(
        result.SessionId.Value,
        result.Pathway.Value,
        result.QuestionnaireVersion.Value,
        ToApiEnum(result.Outcome),
        result.AcceptedAnswerCodes.Select(value => value.Value).ToArray(),
        ToAcceptedValuesResponse(result.AcceptedValues),
        new QuestionnaireProgressResponse(
            ToApiEnum(result.Progression.State),
            result.Progression.AnsweredRequiredFields.Select(value => value.Value).ToArray(),
            result.Progression.MissingRequiredFields.Select(value => value.Value).ToArray(),
            result.Progression.NextQuestion is null
                ? null
                : new NextQuestionResponse(
                    result.Progression.NextQuestion.Code.Value,
                    result.Progression.NextQuestion.Prompt,
                    ToApiEnum(result.Progression.NextQuestion.AnswerType),
                    result.Progression.NextQuestion.AllowedValues,
                    result.Progression.NextQuestion.AllowedUnits,
                    result.Progression.NextQuestion.Minimum,
                    result.Progression.NextQuestion.Maximum),
            result.Progression.ReadyToComplete),
        result.ClarificationCode is null
            ? null
            : new IntakeClarificationResponse(
                result.ClarificationCode,
                result.ClarificationClassification.HasValue
                    ? ToApiEnum(result.ClarificationClassification.Value)
                    : null),
        includeConversation && result.Conversation is not null
            ? ToResponse(result.Conversation)
            : null);

    private static PreTriageConversationResponse ToResponse(
        PreTriageConversationProjection projection) => new(
            projection.SessionId.Value,
            ToApiEnum(projection.SessionStatus),
            ToApiEnum(projection.State),
            projection.ExpiresAt,
            new ConversationPathwayResponse(
                projection.Pathway.Code.Value,
                projection.Pathway.Label),
            new ClinicalDefinitionReferenceResponse(
                projection.Questionnaire.Code,
                projection.Questionnaire.Version.Value),
            new ClinicalDefinitionReferenceResponse(
                projection.RuleSet.Code,
                projection.RuleSet.Version.Value),
            new ConversationProgressResponse(
                projection.Progress.Completed,
                projection.Progress.Total,
                projection.Progress.Percentage),
            ToAcceptedValuesResponse(projection.AcceptedValues),
            projection.NextInteraction is null
                ? null
                : new ConversationInteractionResponse(
                    ToApiEnum(projection.NextInteraction.Type),
                    projection.NextInteraction.Field,
                    projection.NextInteraction.QuestionCode?.Value,
                    projection.NextInteraction.Prompt,
                    ToApiEnum(projection.NextInteraction.InputType),
                    projection.NextInteraction.Required,
                    new ConversationConstraintsResponse(
                        projection.NextInteraction.Constraints.Minimum,
                        projection.NextInteraction.Constraints.Maximum,
                        projection.NextInteraction.Constraints.Step,
                        projection.NextInteraction.Constraints.ExclusiveMinimum,
                        projection.NextInteraction.Constraints.AllowedUnits,
                        projection.NextInteraction.Constraints.MinimumSelections,
                        projection.NextInteraction.Constraints.MaximumSelections,
                        projection.NextInteraction.Constraints.AllowsEmptySelection),
                    projection.NextInteraction.Options.Select(option =>
                        new ConversationOptionResponse(option.Value, option.Label)).ToArray(),
                    projection.NextInteraction.Video is null
                        ? null
                        : new ConversationVideoResponse(
                            projection.NextInteraction.Video.Id,
                            projection.NextInteraction.Video.Title,
                            projection.NextInteraction.Video.Url)));

    private static PreTriageAcceptedValuesResponse ToAcceptedValuesResponse(
        IReadOnlyList<AcceptedTriageAnswerValue> acceptedValues)
    {
        DurationResultResponse? duration = null;
        int? intensity = null;
        IReadOnlyList<string>? additionalSymptoms = null;
        foreach (var accepted in acceptedValues)
        {
            switch (accepted.Value)
            {
                case ClinicalAiDurationValue value:
                    duration = new DurationResultResponse(
                        value.Value,
                        ToApiValue(value.Unit));
                    break;
                case ClinicalAiIntegerValue value:
                    intensity = value.Value;
                    break;
                case ClinicalAiMultipleChoiceValue value:
                    additionalSymptoms = value.Values;
                    break;
                default:
                    throw new InvalidOperationException(
                        "An accepted Pre-Triage answer has an unsupported response value.");
            }
        }

        return new PreTriageAcceptedValuesResponse(
            duration,
            intensity,
            additionalSymptoms);
    }

    private static NeutralPreTriageResultResponse ToResponse(NeutralPreTriageResult result) =>
        new(
            result.SessionId.Value,
            result.EpisodeId.Value,
            new PrimarySymptomResponse(
                result.PrimarySymptom.Value,
                result.PrimarySymptomDisplay),
            new DurationResultResponse(result.DurationValue, result.DurationUnit),
            result.Intensity,
            result.AdditionalSymptoms,
            result.CompletedAt,
            new ClinicalDefinitionReferenceResponse(
                result.QuestionnaireCode.Value,
                result.QuestionnaireVersion.Value),
            new ClinicalDefinitionReferenceResponse(
                result.PackageCode.Value,
                result.PackageVersion.Value),
            new ClinicalContentStatusResponse(
                ToApiValue(result.ContentStatus.Source),
                ToApiValue(result.ContentStatus.ReviewStatus),
                ToApiValue(result.ContentStatus.ApprovalStatus)));

    private static string ToApiEnum<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());

    private static string ToApiValue(ClinicalDurationUnit value) => value switch
    {
        ClinicalDurationUnit.Minutes => "MINUTES",
        ClinicalDurationUnit.Hours => "HOURS",
        ClinicalDurationUnit.Days => "DAYS",
        ClinicalDurationUnit.Weeks => "WEEKS",
        ClinicalDurationUnit.Months => "MONTHS",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToApiValue(ClinicalContentSource value) => value switch
    {
        ClinicalContentSource.ReferencePlatformDerived => "REFERENCE_PLATFORM_DERIVED",
        ClinicalContentSource.LegacyUnspecified => "LEGACY_UNSPECIFIED",
        ClinicalContentSource.ProductDemoDefined => "PRODUCT_DEMO_DEFINED",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(ClinicalReviewStatus value) => value switch
    {
        ClinicalReviewStatus.Provisional => "PROVISIONAL",
        ClinicalReviewStatus.Reviewed => "REVIEWED",
        ClinicalReviewStatus.NotApplicable => "NOT_APPLICABLE",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(ClinicalApprovalStatus value) => value switch
    {
        ClinicalApprovalStatus.PendingFormalReview => "PENDING_FORMAL_REVIEW",
        ClinicalApprovalStatus.Approved => "APPROVED",
        ClinicalApprovalStatus.NotClinicallyApproved => "NOT_CLINICALLY_APPROVED",
        _ => value.ToString().ToUpperInvariant()
    };
}

internal sealed class StartPreTriageSessionRequest
{
    [StringLength(ClinicalPathwayCode.MaximumLength, MinimumLength = 1)]
    public string? Pathway { get; init; }

    public Guid? PatientId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class InterpretPreTriageIntakeRequest
{
    [Required]
    [StringLength(InterpretPreTriageIntake.MaximumTextLength, MinimumLength = 1)]
    public string? Text { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class SubmitPreTriageAnswersRequest
{
    [StringLength(DefinitionVersion.MaximumLength, MinimumLength = 1)]
    public string? QuestionnaireVersion { get; init; }

    public StructuredPreTriageAnswersRequest? Structured { get; init; }

    [StringLength(SubmitTriageAnswers.MaximumNaturalLanguageLength, MinimumLength = 1)]
    public string? NaturalLanguage { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class ResolveEducationalVideoOfferRequest
{
    [Required]
    [StringLength(5, MinimumLength = 4)]
    public string? Decision { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class StructuredPreTriageAnswersRequest
{
    public DurationAnswerRequest? Duration { get; init; }

    public int? Intensity { get; init; }

    public IReadOnlyList<string>? AdditionalSymptoms { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class DurationAnswerRequest
{
    public decimal Value { get; init; }

    public string? Unit { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record PreTriageSessionStartResponse(
    Guid SessionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PatientId,
    string Pathway,
    string Status,
    DateTimeOffset ExpiresAt,
    ClinicalDefinitionReferenceResponse Questionnaire,
    ClinicalDefinitionReferenceResponse RuleSet,
    ClinicalContentStatusResponse ClinicalContent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AnonymousCapability,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PreTriageConversationResponse? Conversation);

internal sealed record PreTriageIntakeInterpretationResponse(
    string Resolution,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Pathway,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CandidatePathways,
    PreTriageAcceptedValuesResponse CandidateValues);

internal sealed record StartPreTriageFromIntakeResponse(
    string Resolution,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CandidatePathways,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PreTriageSessionStartResponse? Session,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PreTriageAnswerResponse? InitialAnswers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PreTriageConversationResponse? Conversation);

internal sealed record ClinicalDefinitionReferenceResponse(
    string Code,
    string Version);

internal sealed record ClinicalContentStatusResponse(
    string Source,
    string ReviewStatus,
    string ClinicalApproval);

internal sealed record PreTriageAnswerResponse(
    Guid SessionId,
    string Pathway,
    string QuestionnaireVersion,
    string Outcome,
    IReadOnlyList<string> AcceptedAnswers,
    PreTriageAcceptedValuesResponse AcceptedValues,
    QuestionnaireProgressResponse Progression,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IntakeClarificationResponse? Clarification,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PreTriageConversationResponse? Conversation);

internal sealed record PreTriageConversationResponse(
    Guid SessionId,
    string SessionStatus,
    string State,
    DateTimeOffset ExpiresAt,
    ConversationPathwayResponse Pathway,
    ClinicalDefinitionReferenceResponse Questionnaire,
    ClinicalDefinitionReferenceResponse RuleSet,
    ConversationProgressResponse Progress,
    PreTriageAcceptedValuesResponse AcceptedValues,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ConversationInteractionResponse? NextInteraction);

internal sealed record ResolveEducationalVideoOfferResponse(
    Guid SessionId,
    string Decision,
    DateTimeOffset ResolvedAt,
    bool NewlyResolved,
    PreTriageConversationResponse Conversation);

internal sealed record ConversationPathwayResponse(
    string Code,
    string Label);

internal sealed record ConversationProgressResponse(
    int Completed,
    int Total,
    int Percentage);

internal sealed record ConversationInteractionResponse(
    string Type,
    string Field,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? QuestionCode,
    string Prompt,
    string InputType,
    bool Required,
    ConversationConstraintsResponse Constraints,
    IReadOnlyList<ConversationOptionResponse> Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ConversationVideoResponse? Video);

internal sealed record ConversationVideoResponse(
    string Id,
    string Title,
    string Url);

internal sealed record ConversationConstraintsResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? Minimum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? Maximum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? Step,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? ExclusiveMinimum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? AllowedUnits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MinimumSelections,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaximumSelections,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? AllowsEmptySelection);

internal sealed record ConversationOptionResponse(
    string Value,
    string Label);

internal sealed record PreTriageAcceptedValuesResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DurationResultResponse? Duration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Intensity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? AdditionalSymptoms);

internal sealed record QuestionnaireProgressResponse(
    string State,
    IReadOnlyList<string> AnsweredRequiredFields,
    IReadOnlyList<string> MissingRequiredFields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NextQuestionResponse? NextQuestion,
    bool ReadyToComplete);

internal sealed record NextQuestionResponse(
    string Code,
    string Prompt,
    string AnswerType,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<string> AllowedUnits,
    decimal? Minimum,
    decimal? Maximum);

internal sealed record IntakeClarificationResponse(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Classification);

internal sealed record NeutralPreTriageResultResponse(
    Guid SessionId,
    Guid EpisodeId,
    PrimarySymptomResponse PrimarySymptom,
    DurationResultResponse Duration,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms,
    DateTimeOffset CompletedAt,
    ClinicalDefinitionReferenceResponse Questionnaire,
    ClinicalDefinitionReferenceResponse Package,
    ClinicalContentStatusResponse ClinicalContent);

internal sealed record PrimarySymptomResponse(string Code, string Display);

internal sealed record DurationResultResponse(decimal Value, string Unit);

internal sealed record ClaimAnonymousPreTriageResponse(
    Guid SessionId,
    Guid EpisodeId,
    Guid PatientId,
    DateTimeOffset ClaimedAt);
