using System.Net;
using System.Text.Json;
using Beeexy.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class OpenApiAndCorsTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task OpenApi_InDevelopment_IncludesHealthAndEmailAuthenticationEndpoints()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        Assert.Equal(32, paths.EnumerateObject().Count());
        Assert.True(paths.GetProperty("/health/live").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/health/ready").TryGetProperty("get", out _));
        Assert.True(paths
            .GetProperty("/api/v1/private-access/login")
            .TryGetProperty("post", out var privateLoginOperation));
        AssertResponseCodes(privateLoginOperation, "200", "204", "400", "401", "429");
        Assert.True(paths
            .GetProperty("/api/v1/private-access/session")
            .TryGetProperty("get", out _));
        Assert.True(paths
            .GetProperty("/api/v1/private-access/logout")
            .TryGetProperty("post", out _));
        var guestSessionOperation = paths
            .GetProperty("/api/v1/private-access/guest-session")
            .GetProperty("post");
        Assert.False(guestSessionOperation.TryGetProperty("requestBody", out _));
        AssertResponseCodes(guestSessionOperation, "200", "400", "401", "500", "503");
        Assert.DoesNotContain(
            paths.EnumerateObject(),
            path => path.Name.Contains("generator", StringComparison.OrdinalIgnoreCase));
        Assert.True(paths
            .GetProperty("/api/v1/auth/email/challenges")
            .TryGetProperty("post", out _));
        Assert.True(paths
            .GetProperty("/api/v1/auth/email/verify")
            .TryGetProperty("post", out _));
        var challengeOperation = paths
            .GetProperty("/api/v1/auth/email/challenges")
            .GetProperty("post");
        var verifyOperation = paths
            .GetProperty("/api/v1/auth/email/verify")
            .GetProperty("post");
        var googleOperation = paths
            .GetProperty("/api/v1/auth/google")
            .GetProperty("post");
        Assert.True(googleOperation
            .GetProperty("responses")
            .TryGetProperty("200", out _));
        Assert.True(googleOperation
            .GetProperty("responses")
            .TryGetProperty("401", out _));
        Assert.True(googleOperation
            .GetProperty("responses")
            .TryGetProperty("422", out _));
        Assert.True(googleOperation
            .GetProperty("responses")
            .TryGetProperty("503", out _));
        var refreshOperation = paths
            .GetProperty("/api/v1/auth/refresh")
            .GetProperty("post");
        Assert.True(paths
            .GetProperty("/api/v1/auth/logout")
            .TryGetProperty("post", out var logoutOperation));
        var accountMeOperation = paths
            .GetProperty("/api/v1/auth/me")
            .GetProperty("get");
        var patientMePath = paths.GetProperty("/api/v1/patients/me");
        var patientGetOperation = patientMePath.GetProperty("get");
        var patientPatchOperation = patientMePath.GetProperty("patch");
        var accessiblePatientsOperation = paths
            .GetProperty("/api/v1/patients")
            .GetProperty("get");
        var patientDetailPath = paths.GetProperty("/api/v1/patients/{patientId}");
        var patientDetailOperation = patientDetailPath.GetProperty("get");
        var managedPatientPatchOperation = patientDetailPath.GetProperty("patch");
        var clinicalHistoryOperation = paths
            .GetProperty("/api/v1/patients/{patientId}/clinical-history")
            .GetProperty("get");
        var clinicalHistoryDetailPath = paths.GetProperty(
            "/api/v1/patients/{patientId}/clinical-history/{eventId}");
        var clinicalHistoryDetailOperation = clinicalHistoryDetailPath
            .GetProperty("get");
        var preTriageAmendmentPath = paths.GetProperty(
            "/api/v1/pre-triage/episodes/{episodeId}/amendments");
        var preTriageAmendmentOperation = preTriageAmendmentPath
            .GetProperty("post");
        var clinicalHistoryParameters = clinicalHistoryOperation
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(
            ["patientId", "cursor", "pageSize", "eventType"],
            clinicalHistoryParameters);
        Assert.Equal(
            ["patientId", "eventId"],
            clinicalHistoryDetailOperation.GetProperty("parameters")
                .EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()));
        Assert.False(clinicalHistoryDetailOperation.TryGetProperty("requestBody", out _));
        Assert.Equal(2, paths.EnumerateObject().Count(path => path.Name.StartsWith(
            "/api/v1/patients/{patientId}/clinical-history",
            StringComparison.Ordinal)));
        Assert.False(clinicalHistoryDetailPath.TryGetProperty("post", out _));
        Assert.Equal(
            ["episodeId"],
            preTriageAmendmentOperation.GetProperty("parameters")
                .EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()));
        Assert.True(preTriageAmendmentOperation.TryGetProperty("requestBody", out _));
        Assert.False(preTriageAmendmentPath.TryGetProperty("get", out _));
        Assert.False(preTriageAmendmentPath.TryGetProperty("put", out _));
        Assert.False(preTriageAmendmentPath.TryGetProperty("patch", out _));
        Assert.False(preTriageAmendmentPath.TryGetProperty("delete", out _));
        var careRelationshipPath = paths.GetProperty("/api/v1/care-relationships");
        var careRelationshipListOperation = careRelationshipPath.GetProperty("get");
        Assert.True(careRelationshipPath.TryGetProperty("post", out _));
        var careRelationshipDeleteOperation = paths
            .GetProperty("/api/v1/care-relationships/{id}")
            .GetProperty("delete");
        var preTriageStartOperation = paths
            .GetProperty("/api/v1/pre-triage/sessions")
            .GetProperty("post");
        var preTriageIntakeOperation = paths
            .GetProperty("/api/v1/pre-triage/intake")
            .GetProperty("post");
        var preTriageInterpretOperation = paths
            .GetProperty("/api/v1/pre-triage/intake/interpret")
            .GetProperty("post");
        var preTriageAnswerOperation = paths
            .GetProperty("/api/v1/pre-triage/sessions/{id}/answers")
            .GetProperty("post");
        var preTriageConversationOperation = paths
            .GetProperty("/api/v1/pre-triage/sessions/{id}/conversation")
            .GetProperty("get");
        var educationalVideoOfferOperation = paths
            .GetProperty(
                "/api/v1/pre-triage/sessions/{id}/educational-video-offer")
            .GetProperty("post");
        var preTriageCompleteOperation = paths
            .GetProperty("/api/v1/pre-triage/sessions/{id}/complete")
            .GetProperty("post");
        var preTriageResultOperation = paths
            .GetProperty("/api/v1/pre-triage/sessions/{id}/result")
            .GetProperty("get");
        var preTriageClaimOperation = paths
            .GetProperty("/api/v1/pre-triage/sessions/{id}/claim")
            .GetProperty("post");
        var fhirCreateOperation = paths
            .GetProperty("/api/v1/patients/{patientId}/fhir-exports")
            .GetProperty("post");
        var fhirMetadataOperation = paths
            .GetProperty("/api/v1/fhir-exports/{id}")
            .GetProperty("get");
        var fhirContentOperation = paths
            .GetProperty("/api/v1/fhir-exports/{id}/content")
            .GetProperty("get");

        AssertResponseCodes(challengeOperation, "202", "400", "422", "429", "500");
        AssertResponseCodes(verifyOperation, "200", "400", "401", "409", "422", "429", "500");
        AssertResponseCodes(googleOperation, "200", "400", "401", "422", "503", "500");
        AssertResponseCodes(refreshOperation, "200", "400", "401", "500");
        AssertResponseCodes(logoutOperation, "204", "401", "500");
        AssertResponseCodes(accountMeOperation, "200", "401", "500");
        AssertResponseCodes(patientGetOperation, "200", "401", "404", "500");
        AssertResponseCodes(accessiblePatientsOperation, "200", "401", "500");
        AssertResponseCodes(patientDetailOperation, "200", "401", "404", "500");
        AssertResponseCodes(
            clinicalHistoryOperation,
            "200",
            "401",
            "404",
            "422",
            "500");
        AssertResponseCodes(
            clinicalHistoryDetailOperation,
            "200",
            "401",
            "404",
            "500");
        AssertResponseCodes(
            preTriageAmendmentOperation,
            "201",
            "401",
            "404",
            "409",
            "422",
            "500");
        AssertResponseCodes(
            managedPatientPatchOperation,
            "200",
            "400",
            "401",
            "404",
            "409",
            "422",
            "500");
        AssertResponseCodes(careRelationshipListOperation, "200", "401", "500");
        AssertResponseCodes(careRelationshipDeleteOperation, "204", "401", "404", "500");
        AssertResponseCodes(
            patientPatchOperation,
            "200",
            "400",
            "401",
            "404",
            "409",
            "422",
            "500");
        AssertResponseCodes(
            preTriageIntakeOperation,
            "200",
            "201",
            "400",
            "401",
            "404",
            "409",
            "422",
            "503",
            "500");
        AssertResponseCodes(
            preTriageInterpretOperation,
            "200",
            "400",
            "401",
            "422",
            "503",
            "500");
        AssertResponseCodes(
            preTriageStartOperation,
            "201",
            "400",
            "401",
            "404",
            "422",
            "500");
        AssertResponseCodes(
            preTriageAnswerOperation,
            "200",
            "400",
            "401",
            "404",
            "409",
            "422",
            "500");
        AssertResponseCodes(
            preTriageConversationOperation,
            "200",
            "401",
            "404",
            "500");
        AssertResponseCodes(
            educationalVideoOfferOperation,
            "200",
            "400",
            "401",
            "404",
            "409",
            "422",
            "500");
        AssertResponseCodes(
            preTriageCompleteOperation,
            "200",
            "201",
            "401",
            "404",
            "409",
            "422",
            "500");
        AssertResponseCodes(
            preTriageResultOperation,
            "200",
            "401",
            "404",
            "409",
            "500");
        AssertResponseCodes(
            preTriageClaimOperation,
            "200",
            "400",
            "401",
            "404",
            "409",
            "500");
        AssertResponseCodes(
            fhirCreateOperation,
            "200",
            "201",
            "400",
            "401",
            "404",
            "409",
            "422",
            "503",
            "500");
        AssertResponseCodes(fhirMetadataOperation, "200", "401", "404", "500");
        AssertResponseCodes(
            fhirContentOperation,
            "200",
            "401",
            "404",
            "409",
            "503",
            "500");
        Assert.True(fhirCreateOperation.TryGetProperty("requestBody", out _));
        Assert.False(fhirMetadataOperation.TryGetProperty("requestBody", out _));
        Assert.False(fhirContentOperation.TryGetProperty("requestBody", out _));
        Assert.True(fhirContentOperation.GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .TryGetProperty("application/fhir+json", out _));

        foreach (var operation in new[]
                 {
                     challengeOperation,
                     verifyOperation,
                     googleOperation,
                     refreshOperation,
                     patientPatchOperation,
                     managedPatientPatchOperation
                 })
        {
            Assert.True(operation.TryGetProperty("requestBody", out _));
        }

        foreach (var operation in new[]
                 {
                     accountMeOperation,
                     patientGetOperation,
                     patientPatchOperation,
                     accessiblePatientsOperation,
                     patientDetailOperation,
                     managedPatientPatchOperation,
                     clinicalHistoryOperation,
                     clinicalHistoryDetailOperation,
                     preTriageAmendmentOperation,
                     careRelationshipListOperation,
                     careRelationshipDeleteOperation,
                     preTriageClaimOperation,
                     fhirCreateOperation,
                     fhirMetadataOperation,
                     fhirContentOperation
                 })
        {
            var security = operation.GetProperty("security");
            Assert.Single(security.EnumerateArray());
            Assert.True(security[0].TryGetProperty("Bearer", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
        }

        var patchResponses = patientPatchOperation.GetProperty("responses");
        Assert.True(patchResponses.TryGetProperty("200", out _));
        Assert.True(patchResponses.TryGetProperty("409", out _));
        Assert.True(patchResponses.TryGetProperty("422", out _));
        Assert.Contains(
            "stale version returns 409",
            patientPatchOperation.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var historyItemProperties = schemas
            .GetProperty("ClinicalHistoryItemResponse")
            .GetProperty("properties");
        Assert.Equal(
            ["eventId", "eventType", "occurredAt", "recordedAt", "source"],
            historyItemProperties.EnumerateObject().Select(property => property.Name));
        var historySourceProperties = schemas
            .GetProperty("ClinicalHistorySourceResponse")
            .GetProperty("properties");
        Assert.Equal(
            ["type", "id", "questionnaireVersionId", "clinicalRuleSetVersionId"],
            historySourceProperties.EnumerateObject().Select(property => property.Name));
        var historyDetailProperties = schemas
            .GetProperty("ClinicalHistoryEventDetailResponse")
            .GetProperty("properties");
        Assert.Equal(
            [
                "eventId",
                "eventType",
                "occurredAt",
                "recordedAt",
                "source",
                "provenance",
                "primarySymptom",
                "duration",
                "intensity",
                "additionalSymptoms",
                "amendments"
            ],
            historyDetailProperties.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["code", "display"],
            schemas.GetProperty("ClinicalHistoryPrimarySymptomResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            ["value", "unit"],
            schemas.GetProperty("ClinicalHistoryDurationResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            ["amendmentId", "reason", "author", "createdAt", "provenance"],
            schemas.GetProperty("ClinicalHistoryAmendmentResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            ["type", "beeexyId"],
            schemas.GetProperty("ClinicalHistoryAmendmentAuthorResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            ["idempotencyKey", "reason"],
            schemas.GetProperty("AmendPreTriageEpisodeRequest")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        foreach (var forbidden in new[]
                 {
                     "accountId", "urgency", "disposition", "redFlags", "diagnosis",
                     "prescription", "treatment", "probability", "provider", "model"
                 })
        {
            Assert.DoesNotContain(
                forbidden,
                historyDetailProperties.EnumerateObject().Select(property => property.Name),
                StringComparer.OrdinalIgnoreCase);
        }
        var updateSchema = schemas.GetProperty("UpdateManagedPatientRequest");
        var updateProperties = updateSchema.GetProperty("properties");
        Assert.Equal("date", updateProperties.GetProperty("dateOfBirth").GetProperty("format").GetString());
        Assert.Equal(
            ["Male", "Female"],
            updateProperties.GetProperty("sexAssignedAtBirth")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains(
            "AL|AK",
            updateProperties.GetProperty("state").GetProperty("pattern").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            updateProperties.GetProperty("version").GetProperty("minimum").GetInt64());

        var creationPatient = schemas.GetProperty("ManagedPatientRequest");
        Assert.Equal(
            ["dateOfBirth", "firstName", "lastName", "sexAssignedAtBirth", "state"],
            creationPatient.GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString()));

        var logoutSecurity = logoutOperation.GetProperty("security");
        Assert.Single(logoutSecurity.EnumerateArray());
        Assert.True(
            logoutSecurity[0].TryGetProperty("Bearer", out _),
            logoutSecurity[0].GetRawText());
        Assert.False(refreshOperation.TryGetProperty("security", out _));

        var preTriageSecurity = preTriageStartOperation.GetProperty("security");
        Assert.Equal(2, preTriageSecurity.GetArrayLength());
        Assert.Empty(preTriageSecurity[0].EnumerateObject());
        Assert.True(preTriageSecurity[1].TryGetProperty("Bearer", out _));
        Assert.True(preTriageStartOperation.TryGetProperty("requestBody", out _));
        var preTriageIntakeSecurity = preTriageIntakeOperation.GetProperty("security");
        Assert.Equal(2, preTriageIntakeSecurity.GetArrayLength());
        Assert.Empty(preTriageIntakeSecurity[0].EnumerateObject());
        Assert.True(preTriageIntakeSecurity[1].TryGetProperty("Bearer", out _));
        Assert.True(preTriageIntakeOperation.TryGetProperty("requestBody", out _));
        var intakeDescription = preTriageIntakeOperation.GetProperty("description").GetString();
        Assert.Contains("only when RESOLVED", intakeDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pinned questionnaire", intakeDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AMBIGUOUS and UNRESOLVED return 200", intakeDescription,
            StringComparison.Ordinal);
        var intakeIdempotencyParameter = preTriageIntakeOperation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => string.Equals(
                parameter.GetProperty("name").GetString(),
                "Idempotency-Key",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("header", intakeIdempotencyParameter.GetProperty("in").GetString());
        Assert.True(intakeIdempotencyParameter.GetProperty("required").GetBoolean());
        Assert.Contains(
            "same scoped key with different text returns 409",
            intakeIdempotencyParameter.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
        var preTriageInterpretSecurity = preTriageInterpretOperation.GetProperty("security");
        Assert.Equal(2, preTriageInterpretSecurity.GetArrayLength());
        Assert.Empty(preTriageInterpretSecurity[0].EnumerateObject());
        Assert.True(preTriageInterpretSecurity[1].TryGetProperty("Bearer", out _));
        Assert.True(preTriageInterpretOperation.TryGetProperty("requestBody", out _));
        var interpretationDescription = preTriageInterpretOperation
            .GetProperty("description")
            .GetString();
        Assert.Contains("pre-session", interpretationDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without creating a session", interpretationDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RESOLVED, AMBIGUOUS, or UNRESOLVED", interpretationDescription,
            StringComparison.Ordinal);
        Assert.Contains("OTHER_SYMPTOMS", interpretationDescription,
            StringComparison.Ordinal);
        var preTriageAnswerSecurity = preTriageAnswerOperation.GetProperty("security");
        Assert.Equal(2, preTriageAnswerSecurity.GetArrayLength());
        Assert.Empty(preTriageAnswerSecurity[0].EnumerateObject());
        Assert.True(preTriageAnswerSecurity[1].TryGetProperty("Bearer", out _));
        Assert.True(preTriageAnswerOperation.TryGetProperty("requestBody", out _));
        foreach (var operation in new[]
                 {
                     preTriageConversationOperation,
                     preTriageCompleteOperation,
                     preTriageResultOperation
                 })
        {
            var security = operation.GetProperty("security");
            Assert.Equal(2, security.GetArrayLength());
            Assert.Empty(security[0].EnumerateObject());
            Assert.True(security[1].TryGetProperty("Bearer", out _));
            Assert.Contains("X-Pre-Triage-Capability",
                operation.GetProperty("description").GetString(),
                StringComparison.Ordinal);
            Assert.False(operation.TryGetProperty("requestBody", out _));
        }
        var educationalVideoOfferSecurity = educationalVideoOfferOperation
            .GetProperty("security");
        Assert.Equal(2, educationalVideoOfferSecurity.GetArrayLength());
        Assert.Empty(educationalVideoOfferSecurity[0].EnumerateObject());
        Assert.True(educationalVideoOfferSecurity[1].TryGetProperty("Bearer", out _));
        Assert.True(educationalVideoOfferOperation.TryGetProperty("requestBody", out _));
        Assert.Contains("X-Pre-Triage-Capability",
            educationalVideoOfferOperation.GetProperty("description").GetString(),
            StringComparison.Ordinal);

        var claimSecurity = preTriageClaimOperation.GetProperty("security");
        Assert.Single(claimSecurity.EnumerateArray());
        Assert.True(claimSecurity[0].TryGetProperty("Bearer", out _));
        Assert.False(preTriageClaimOperation.TryGetProperty("requestBody", out _));
        var claimParameters = preTriageClaimOperation.GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(claimParameters, parameter =>
            parameter.GetProperty("in").GetString() == "header" &&
            parameter.GetProperty("name").GetString() == "X-Pre-Triage-Capability");
        Assert.DoesNotContain(claimParameters, parameter =>
            parameter.GetProperty("name").GetString() is "patientId" or "profileId" or
                "accountId" or "beeexyId");
        var claimDescription = preTriageClaimOperation.GetProperty("description").GetString();
        Assert.Contains("server-derived primary patient", claimDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idempotent", claimDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expiry", claimDescription, StringComparison.OrdinalIgnoreCase);

        var neutralResult = schemas.GetProperty("NeutralPreTriageResultResponse");
        var answerResponse = schemas.GetProperty("PreTriageAnswerResponse");
        Assert.Equal(
            [
                "sessionId",
                "pathway",
                "questionnaireVersion",
                "outcome",
                "acceptedAnswers",
                "acceptedValues",
                "progression",
                "clarification",
                "conversation"
            ],
            answerResponse.GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            ["duration", "intensity", "additionalSymptoms"],
            schemas.GetProperty("PreTriageAcceptedValuesResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            ["text"],
            schemas.GetProperty("InterpretPreTriageIntakeRequest")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            ["text"],
            schemas.GetProperty("InterpretPreTriageIntakeRequest")
                .GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["resolution", "pathway", "candidatePathways", "candidateValues"],
            schemas.GetProperty("PreTriageIntakeInterpretationResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            ["resolution", "candidatePathways", "session", "initialAnswers", "conversation"],
            schemas.GetProperty("StartPreTriageFromIntakeResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            [
                "sessionId",
                "sessionStatus",
                "state",
                "expiresAt",
                "pathway",
                "questionnaire",
                "ruleSet",
                "progress",
                "acceptedValues",
                "nextInteraction"
            ],
            schemas.GetProperty("PreTriageConversationResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            [
                "type",
                "field",
                "questionCode",
                "prompt",
                "inputType",
                "required",
                "constraints",
                "options",
                "video"
            ],
            schemas.GetProperty("ConversationInteractionResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            ["decision"],
            schemas.GetProperty("ResolveEducationalVideoOfferRequest")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        Assert.Equal(
            ["sessionId", "decision", "resolvedAt", "newlyResolved", "conversation"],
            schemas.GetProperty("ResolveEducationalVideoOfferResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name));
        var neutralProperties = neutralResult.GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .ToArray();
        foreach (var forbidden in new[]
                 {
                     "urgency", "disposition", "redFlags", "diagnosis", "prescription",
                     "treatment", "probability", "provider", "model", "confidence"
                 })
        {
            Assert.DoesNotContain(forbidden, neutralProperties,
                StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                forbidden,
                answerResponse.GetProperty("properties")
                    .EnumerateObject()
                    .Select(value => value.Name),
                StringComparer.OrdinalIgnoreCase);
        }
        var claimSchema = schemas.GetProperty("ClaimAnonymousPreTriageResponse");
        Assert.Equal(
            ["claimedAt", "episodeId", "patientId", "sessionId"],
            claimSchema.GetProperty("properties").EnumerateObject()
                .Select(value => value.Name)
                .OrderBy(value => value, StringComparer.Ordinal));
        var answerDescription = preTriageAnswerOperation.GetProperty("description").GetString();
        Assert.Contains("X-Pre-Triage-Capability", answerDescription,
            StringComparison.Ordinal);
        Assert.Contains("intensity is 1-10", answerDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NAUSEA, DIARRHEA, and FEVER", answerDescription,
            StringComparison.Ordinal);
        Assert.Contains("FEVER is excluded", answerDescription,
            StringComparison.Ordinal);
        var conversationDescription = preTriageConversationOperation
            .GetProperty("description")
            .GetString();
        Assert.Contains("IN_PROGRESS", conversationDescription, StringComparison.Ordinal);
        Assert.Contains("READY_FOR_REVIEW", conversationDescription, StringComparison.Ordinal);
        Assert.Contains("COMPLETED", conversationDescription, StringComparison.Ordinal);
        Assert.Contains("pinned questionnaire", conversationDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional fields do not contribute", conversationDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DURATION", conversationDescription, StringComparison.Ordinal);
        Assert.Contains("SCALE", conversationDescription, StringComparison.Ordinal);
        Assert.Contains("MULTI_SELECT", conversationDescription, StringComparison.Ordinal);
        Assert.Contains("EDUCATIONAL_VIDEO_OFFER", conversationDescription,
            StringComparison.Ordinal);
        Assert.Contains("never invokes AI", conversationDescription,
            StringComparison.OrdinalIgnoreCase);
        var educationalDescription = educationalVideoOfferOperation
            .GetProperty("description")
            .GetString();
        Assert.Contains("WATCH or SKIP", educationalDescription, StringComparison.Ordinal);
        Assert.Contains("does not mean playback completed", educationalDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("separately from clinical answers", educationalDescription,
            StringComparison.OrdinalIgnoreCase);

        var securitySchemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");
        var bearer = securitySchemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task OpenApi_InProduction_IsNotExposed()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProductionHttpsResponse_IncludesHsts()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ConfiguredCorsOrigin_IsAllowed()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", BeeexyApiFactory.AllowedCorsOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            BeeexyApiFactory.AllowedCorsOrigin,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task UntrustedCorsOrigin_IsNotAllowed()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "https://untrusted.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void WildcardCorsOrigin_IsRejectedAtStartup()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            allowedCorsOrigin: "*");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("without wildcards", exception.ToString());
        Assert.DoesNotContain(postgres.ConnectionString, exception.ToString());
    }

    private static void AssertResponseCodes(
        JsonElement operation,
        params string[] expectedCodes)
    {
        var responses = operation.GetProperty("responses");
        Assert.Equal(
            expectedCodes.Order(StringComparer.Ordinal),
            responses.EnumerateObject()
                .Select(response => response.Name)
                .Order(StringComparer.Ordinal));
    }
}
