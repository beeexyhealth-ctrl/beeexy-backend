using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase101")]
[Trait("Category", "Phase108")]
public sealed class AiDomainFoundationTests
{
    [Fact]
    public void Conversation_IsAccountOwnedAndMayReferencePatient()
    {
        var accountId = EntityId.New();
        var patientId = EntityId.New();

        var conversation = AiConversation.Create(accountId, Utc(10), patientId);
        var accountOnly = AiConversation.Create(accountId, Utc(10));

        Assert.Equal(accountId, conversation.AccountId);
        Assert.Equal(patientId, conversation.PatientProfileId);
        Assert.Null(accountOnly.PatientProfileId);
        Assert.False(conversation.IsDeleted);
    }

    [Fact]
    public void Conversation_LogicalDeletionIsIdempotentAndPreservesIdentity()
    {
        var conversation = AiConversation.Create(EntityId.New(), Utc(10));
        var id = conversation.Id;

        Assert.True(conversation.Delete(Utc(11)));
        Assert.False(conversation.Delete(Utc(12)));
        Assert.True(conversation.IsDeleted);
        Assert.Equal(Utc(11), conversation.DeletedAt);
        Assert.Equal(id, conversation.Id);
    }

    [Fact]
    public void Message_RequiresDeterministicPositiveSequenceAndSupportedRole()
    {
        var conversationId = EntityId.New();
        var message = AiMessage.Create(
            conversationId,
            AiMessageRole.User,
            "I have a health question.",
            1,
            Utc(10));

        Assert.Equal(conversationId, message.ConversationId);
        Assert.Equal(AiMessageRole.User, message.Role);
        Assert.Equal(1, message.Sequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => AiMessage.Create(
            conversationId,
            AiMessageRole.User,
            "content",
            0,
            Utc(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiMessage.Create(
            conversationId,
            (AiMessageRole)999,
            "content",
            1,
            Utc(10)));
        Assert.Throws<ArgumentException>(() => AiMessage.Create(
            conversationId,
            AiMessageRole.Assistant,
            " ",
            1,
            Utc(10)));
    }

    [Fact]
    public void AnalysisRequest_PreservesProviderNeutralOriginalInputSnapshot()
    {
        var accountId = EntityId.New();
        var patientId = EntityId.New();
        var conversationId = EntityId.New();

        var request = AiAnalysisRequest.Create(
            accountId,
            AiAnalysisPurpose.SecondOpinion,
            "analysis-input-v1",
            "{\"userText\":\"normalized input\",\"sources\":[]}",
            Utc(10),
            patientId,
            conversationId);

        Assert.Equal(accountId, request.AccountId);
        Assert.Equal(patientId, request.PatientProfileId);
        Assert.Equal(conversationId, request.ConversationId);
        Assert.Equal(AiAnalysisPurpose.SecondOpinion, request.Purpose);
        Assert.Equal("analysis-input-v1", request.OriginalInputSchemaVersion);
        Assert.Contains("normalized input", request.OriginalInputSnapshotJson);
    }

    [Fact]
    public void AnalysisRequest_AllowsNoPatientOrConversationButRejectsInvalidSnapshot()
    {
        var request = AiAnalysisRequest.Create(
            EntityId.New(),
            AiAnalysisPurpose.Conversation,
            "conversation-input-v1",
            "{}",
            Utc(10));

        Assert.Null(request.PatientProfileId);
        Assert.Null(request.ConversationId);
        Assert.Throws<ArgumentException>(() => AiAnalysisRequest.Create(
            EntityId.New(),
            AiAnalysisPurpose.Conversation,
            "input-v1",
            "[]",
            Utc(10)));
        Assert.Throws<ArgumentException>(() => AiAnalysisRequest.Create(
            EntityId.New(),
            AiAnalysisPurpose.Conversation,
            "input-v1",
            "not-json",
            Utc(10)));
    }

    [Theory]
    [InlineData(AiExecutionStatus.Succeeded)]
    [InlineData(AiExecutionStatus.Failed)]
    [InlineData(AiExecutionStatus.Rejected)]
    public void Execution_UsesApprovedLifecycleAndCompleteTraceMetadata(
        AiExecutionStatus terminalStatus)
    {
        var execution = AiExecution.CreatePending(EntityId.New(), Utc(10));
        execution.Start("provider", "model", "prompt-v1", Utc(11));

        if (terminalStatus == AiExecutionStatus.Succeeded)
        {
            execution.MarkSucceeded(Utc(12));
        }
        else if (terminalStatus == AiExecutionStatus.Failed)
        {
            execution.MarkFailed("transient_failure", Utc(12));
        }
        else
        {
            execution.MarkRejected(Utc(12));
        }

        Assert.Equal(terminalStatus, execution.Status);
        Assert.Equal("provider", execution.ProviderIdentifier);
        Assert.Equal("model", execution.ModelIdentifier);
        Assert.Equal("prompt-v1", execution.PromptVersion);
        Assert.Equal(3_600_000, execution.LatencyMilliseconds);
        Assert.Equal(
            terminalStatus == AiExecutionStatus.Failed ? "transient_failure" : null,
            execution.SanitizedFailureCategory);
    }

    [Fact]
    public void Execution_RejectsInvalidMetadataAndStateTransitions()
    {
        var execution = AiExecution.CreatePending(EntityId.New(), Utc(10));

        Assert.Throws<InvalidOperationException>(() => execution.MarkSucceeded(Utc(11)));
        Assert.Throws<ArgumentException>(() => execution.Start(
            " ",
            "model",
            "prompt-v1",
            Utc(11)));

        execution.Start("provider", "model", "prompt-v1", Utc(11));
        Assert.Throws<InvalidOperationException>(() => execution.Start(
            "provider",
            "model",
            "prompt-v1",
            Utc(12)));
        execution.MarkSucceeded(Utc(12));
        Assert.Throws<InvalidOperationException>(() => execution.MarkRejected(Utc(13)));
    }

    [Fact]
    public void ResultSnapshot_HasImmutableAnalysisExecutionSequenceAndContent()
    {
        var analysisId = EntityId.New();
        var executionId = EntityId.New();
        var snapshot = AiResultSnapshot.Create(
            analysisId,
            executionId,
            2,
            "second-opinion-result-v1",
            "{\"summary\":\"informational\"}",
            Utc(12));

        Assert.Equal(analysisId, snapshot.AnalysisRequestId);
        Assert.Equal(executionId, snapshot.ExecutionId);
        Assert.Equal(2, snapshot.Sequence);
        Assert.All(
            typeof(AiResultSnapshot).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiResultSnapshot.Create(
            analysisId,
            executionId,
            0,
            "result-v1",
            "{}",
            Utc(12)));
    }

    [Fact]
    public void DocumentMetadata_SupportsOptionalPatientAndAnalysisAssociation()
    {
        var accountId = EntityId.New();
        var patientId = EntityId.New();
        var analysisId = EntityId.New();
        var document = AiUploadedDocument.Create(
            accountId,
            "private/opaque-key",
            "application/pdf",
            512,
            Utc(10),
            Utc(20),
            patientId);

        Assert.Null(document.AnalysisRequestId);
        Assert.True(document.AssociateWithAnalysis(analysisId));
        Assert.False(document.AssociateWithAnalysis(analysisId));
        Assert.Equal(analysisId, document.AnalysisRequestId);
        Assert.Equal(AiDocumentStatus.Active, document.Status);
        Assert.Throws<InvalidOperationException>(() =>
            document.AssociateWithAnalysis(EntityId.New()));
    }

    [Fact]
    public void DocumentMetadata_TracksManualDeletionAndExpiryWithoutPayloadBehavior()
    {
        var manuallyDeleted = CreateDocument();
        var expired = CreateDocument();

        Assert.True(manuallyDeleted.MarkDeleted(Utc(11)));
        Assert.False(manuallyDeleted.MarkDeleted(Utc(12)));
        Assert.Equal(AiDocumentStatus.Deleted, manuallyDeleted.Status);
        Assert.Equal(Utc(11), manuallyDeleted.DeletedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => expired.MarkExpired(Utc(19)));
        Assert.True(expired.MarkExpired(Utc(20)));
        Assert.Equal(AiDocumentStatus.Expired, expired.Status);
        Assert.Equal(Utc(20), expired.DeletedAt);
    }

    [Theory]
    [InlineData(AiSafetyCategory.UnsafeMedicalAdvice)]
    [InlineData(AiSafetyCategory.Diagnosis)]
    [InlineData(AiSafetyCategory.Prescription)]
    [InlineData(AiSafetyCategory.Unsupported)]
    [InlineData(AiSafetyCategory.Malformed)]
    public void RejectedSafetyValidation_IsNonDisplayableRestrictedAudit(
        AiSafetyCategory category)
    {
        var validation = AiSafetyValidation.CreateRejected(
            EntityId.New(),
            category,
            "safety-v1",
            "restricted provider output",
            Utc(12),
            "fallback-v1");

        Assert.Equal(category, validation.Category);
        Assert.False(validation.DisplayEligible);
        Assert.Null(validation.ResultSnapshotId);
        Assert.Equal("restricted provider output", validation.RestrictedAuditOutput);
        Assert.Equal("fallback-v1", validation.ProductContentVersion);
    }

    [Fact]
    public void ApprovedSafetyValidation_RequiresAndExposesResultReference()
    {
        var resultId = EntityId.New();
        var validation = AiSafetyValidation.CreateApproved(
            EntityId.New(),
            resultId,
            "safety-v1",
            Utc(12),
            "disclaimer-v1");

        Assert.Equal(AiSafetyCategory.Approved, validation.Category);
        Assert.True(validation.DisplayEligible);
        Assert.Equal(resultId, validation.ResultSnapshotId);
        Assert.Null(validation.RestrictedAuditOutput);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AiSafetyValidation.CreateRejected(
                EntityId.New(),
                AiSafetyCategory.Approved,
                "safety-v1",
                "output",
                Utc(12)));
    }

    [Fact]
    public void RequiredIdentifiersAndUtcTimestampsAreEnforced()
    {
        var empty = default(EntityId);
        Assert.Throws<ArgumentException>(() => AiConversation.Create(empty, Utc(10)));
        Assert.Throws<ArgumentException>(() => AiExecution.CreatePending(empty, Utc(10)));
        Assert.Throws<ArgumentException>(() => AiConversation.Create(
            EntityId.New(),
            Utc(10),
            id: empty));
        Assert.Throws<ArgumentException>(() => AiConversation.Create(
            EntityId.New(),
            Utc(10).ToOffset(TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void AiDomain_HasNoProviderFhirOrClinicalHistoryCoupling()
    {
        var aiTypes = typeof(AiConversation).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AiConversation).Namespace)
            .ToArray();
        var forbiddenNames = new[]
        {
            "Nvidia", "OpenAI", "Anthropic", "FHIR", "ClinicalHistoryEvent"
        };

        Assert.DoesNotContain(aiTypes, type => forbiddenNames.Any(name =>
            type.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            typeof(AiExecution).GetProperties(),
            property => property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("PromptText", StringComparison.OrdinalIgnoreCase));
    }

    private static AiUploadedDocument CreateDocument()
    {
        return AiUploadedDocument.Create(
            EntityId.New(),
            $"private/{Guid.NewGuid():N}",
            "text/plain",
            128,
            Utc(10),
            Utc(20));
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 9, 1, hour, 0, 0, TimeSpan.Zero);
    }
}
