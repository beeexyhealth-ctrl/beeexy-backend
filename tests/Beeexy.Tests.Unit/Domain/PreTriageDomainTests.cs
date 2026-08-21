using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Tests.Unit.Domain;

public sealed class PreTriageDomainTests
{
    [Fact]
    public void CreateAnonymous_PreservesHashedCapabilityAndTemporaryLifecycle()
    {
        var questionnaire = CreateQuestionnaire();
        var hash = AnonymousCapabilityHash.FromHash(new string('a', 64));

        var session = PreTriageSession.CreateAnonymous(
            questionnaire.Id,
            hash,
            Utc(12).AddHours(24),
            Utc(12));

        Assert.NotEqual(Guid.Empty, session.Id.Value);
        Assert.True(session.IsAnonymous);
        Assert.Null(session.PatientProfileId);
        Assert.Same(hash, session.AnonymousCapabilityHash);
        Assert.Equal(PreTriageSessionStatus.Active, session.Status);
        Assert.Null(session.CompletedAt);
        Assert.Empty(session.Answers);
        Assert.Empty(session.ReportedSymptoms);
    }

    [Fact]
    public void CreateForPatient_HasPatientOwnershipAndNoAnonymousCapabilityHash()
    {
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            CreateQuestionnaire().Id,
            Utc(12).AddHours(8),
            Utc(12));

        Assert.False(session.IsAnonymous);
        Assert.Equal(patientId, session.PatientProfileId);
        Assert.Null(session.AnonymousCapabilityHash);
    }

    [Fact]
    public void SessionCreation_RejectsNonFutureExpiration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreTriageSession.CreateAnonymous(
                EntityId.New(),
                AnonymousCapabilityHash.FromHash(new string('b', 64)),
                Utc(12),
                Utc(12)));
    }

    [Fact]
    public void Completion_PromotesTemporaryAnswersAndSymptomsToPermanentEpisodeOwnership()
    {
        var questionnaire = CreateQuestionnaire(includeQuestion: true);
        var question = Assert.Single(questionnaire.Questions);
        var session = CreateAnonymousSession(questionnaire.Id);
        var answer = session.RecordAnswer(question, "{\"test\":true}", 1, Utc(13));
        var symptom = session.ReportSymptom(
            SymptomText.Create("User-provided test symptom"),
            1,
            Utc(13));

        var episode = PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14),
            Utc(14).AddHours(24));

        Assert.Equal(PreTriageSessionStatus.Completed, session.Status);
        Assert.Equal(Utc(14), session.CompletedAt);
        Assert.Null(answer.SessionId);
        Assert.Equal(episode.Id, answer.EpisodeId);
        Assert.Null(symptom.SessionId);
        Assert.Equal(episode.Id, symptom.EpisodeId);
        Assert.Empty(session.Answers);
        Assert.Empty(session.ReportedSymptoms);
        Assert.Same(answer, Assert.Single(episode.Answers));
        Assert.Same(symptom, Assert.Single(episode.ReportedSymptoms));
        Assert.Equal(session.Id, episode.SourceSessionId);
        Assert.Equal(questionnaire.Id, episode.QuestionnaireVersionId);
        Assert.Null(episode.PatientProfileId);
    }

    [Fact]
    public void CompletedSession_CannotRevertOrAcceptAdditionalWorkflowData()
    {
        var questionnaire = CreateQuestionnaire(includeQuestion: true);
        var question = Assert.Single(questionnaire.Questions);
        var session = CreateAnonymousSession(questionnaire.Id);
        _ = PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14),
            Utc(14).AddHours(24));

        Assert.Throws<InvalidOperationException>(() =>
            session.RecordAnswer(question, "true", 1, Utc(15)));
        Assert.Throws<InvalidOperationException>(() =>
            session.ReportSymptom(SymptomText.Create("Another symptom"), 1, Utc(15)));
        Assert.Throws<InvalidOperationException>(() =>
            PreTriageEpisode.CreateFrom(
                session,
                EntityId.New(),
                Utc(15),
                Utc(15).AddHours(24)));
        Assert.Equal(PreTriageSessionStatus.Completed, session.Status);
    }

    [Fact]
    public void Session_RejectsQuestionFromAnotherQuestionnaireVersion()
    {
        var firstVersion = CreateQuestionnaire("test-flow-a");
        var otherQuestion = Assert.Single(
            CreateQuestionnaire("test-flow-b", includeQuestion: true).Questions);
        var session = CreateAnonymousSession(firstVersion.Id);

        Assert.Throws<ArgumentException>(() =>
            session.RecordAnswer(otherQuestion, "true", 1, Utc(13)));
    }

    [Fact]
    public void ReportedSymptoms_SupportMultipleFreeTextAndOptionalNormalizationMetadata()
    {
        var session = CreateAnonymousSession(CreateQuestionnaire().Id);

        var uncoded = session.ReportSymptom(
            SymptomText.Create(" First user phrase "),
            1,
            Utc(13));
        var coded = session.ReportSymptom(
            SymptomText.Create("Second user phrase"),
            2,
            Utc(13),
            "https://test.example/terminology",
            "test-code",
            "Test display",
            "test-normalizer-version",
            Utc(14));

        Assert.Equal(2, session.ReportedSymptoms.Count);
        Assert.Equal("First user phrase", uncoded.OriginalText.Value);
        Assert.Null(uncoded.TerminologyCode);
        Assert.Equal("Second user phrase", coded.OriginalText.Value);
        Assert.Equal("test-code", coded.TerminologyCode);
        Assert.Equal("test-normalizer-version", coded.NormalizationSource);
        Assert.Equal(Utc(14), coded.NormalizedAt);
    }

    [Fact]
    public void ReportedSymptom_RejectsPartialNormalizationMetadata()
    {
        var session = CreateAnonymousSession(CreateQuestionnaire().Id);

        Assert.Throws<ArgumentException>(() => session.ReportSymptom(
            SymptomText.Create("User phrase"),
            1,
            Utc(13),
            terminologySystem: "https://test.example/terminology"));
    }

    [Fact]
    public void AnonymousEpisodeClaim_IsOneTimeIdempotentForSamePatientAndConflictsForAnother()
    {
        var episode = CreateAnonymousEpisode();
        var patientId = EntityId.New();
        var claimedAt = Utc(15);

        Assert.True(episode.Claim(patientId, claimedAt));
        Assert.False(episode.Claim(patientId, Utc(16)));
        Assert.Equal(patientId, episode.PatientProfileId);
        Assert.Equal(claimedAt, episode.ClaimedAt);
        Assert.Throws<InvalidOperationException>(() =>
            episode.Claim(EntityId.New(), Utc(16)));
        Assert.Equal(patientId, episode.PatientProfileId);
    }

    [Fact]
    public void AnonymousEpisodeClaim_RejectsExpiredEpisode()
    {
        var episode = CreateAnonymousEpisode(Utc(15));

        Assert.Throws<InvalidOperationException>(() =>
            episode.Claim(EntityId.New(), Utc(15)));
        Assert.Null(episode.PatientProfileId);
        Assert.Null(episode.ClaimedAt);
    }

    [Fact]
    public void AuthenticatedEpisode_IsPermanentlyOwnedAndCannotBeClaimed()
    {
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            CreateQuestionnaire().Id,
            Utc(20),
            Utc(12));
        var episode = PreTriageEpisode.CreateFrom(session, EntityId.New(), Utc(14));

        Assert.Equal(patientId, episode.PatientProfileId);
        Assert.Null(episode.AnonymousExpiresAt);
        Assert.Throws<InvalidOperationException>(() => episode.Claim(patientId, Utc(15)));
    }

    [Fact]
    public void AssessmentAndFindings_RecordRuleProvenanceWithoutProbabilityFields()
    {
        var episode = CreateAnonymousEpisode();
        var assessment = ClinicalAssessment.Create(
            episode,
            UrgencyCode.Create("test-only-urgency"),
            episode.CompletedAt,
            [new ClinicalFindingInput("test-finding", "test-source-rule", "message/test")],
            "result/test");

        Assert.Equal(episode.Id, assessment.EpisodeId);
        Assert.Equal(episode.ClinicalRuleSetVersionId, assessment.ClinicalRuleSetVersionId);
        Assert.Equal("test-only-urgency", assessment.UrgencyCode.Value);
        var finding = Assert.Single(assessment.Findings);
        Assert.Equal("test-finding", finding.FindingCode);
        Assert.Equal("test-source-rule", finding.SourceRuleCode);
        Assert.DoesNotContain(
            typeof(ClinicalAssessment).GetProperties(),
            property => property.Name.Contains("Probability", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ClinicalFinding).GetProperties(),
            property => property.Name.Contains("Probability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PermanentClinicalRecords_ExposeNoPublicPropertySettersOrMutationMethods()
    {
        Assert.All(
            typeof(ClinicalAssessment).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.All(
            typeof(ClinicalFinding).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));

        var episodeMutationMethods = typeof(PreTriageEpisode)
            .GetMethods()
            .Where(method =>
                method.DeclaringType == typeof(PreTriageEpisode) &&
                !method.IsStatic &&
                !method.IsSpecialName)
            .Select(method => method.Name);
        Assert.Equal(["Claim"], episodeMutationMethods);
    }

    [Fact]
    public void PersistedSessionShape_ContainsHashButNoRawCapabilityOrToken()
    {
        var properties = typeof(PreTriageSession).GetProperties();

        Assert.Contains(properties, property =>
            property.Name == nameof(PreTriageSession.AnonymousCapabilityHash) &&
            property.PropertyType == typeof(AnonymousCapabilityHash));
        Assert.DoesNotContain(properties, property =>
            property.PropertyType == typeof(string) &&
            (property.Name.Contains("Capability", StringComparison.OrdinalIgnoreCase) ||
             property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ApprovedDefinitionVersions_PreserveStableIdentityAndProvenance()
    {
        var questionnaire = CreateQuestionnaire();
        var rules = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create("test-rule-package"),
            DefinitionVersion.Create("test-version-1"),
            DefinitionHash.FromHash(new string('d', 64)),
            Utc(12),
            Utc(11),
            Utc(13),
            "test/import/source");

        Assert.Equal("test-questionnaire", questionnaire.QuestionnaireCode.Value);
        Assert.Equal("test-version-1", questionnaire.Version.Value);
        Assert.Equal(new string('c', 64), questionnaire.ContentHash.Value);
        Assert.Equal("test-rule-package", rules.RuleSetCode.Value);
        Assert.Equal("test-version-1", rules.Version.Value);
        Assert.Equal(Utc(13), rules.ActivatedAt);
        Assert.Empty(typeof(QuestionnaireDefinitionVersion)
            .GetMethods()
            .Where(method =>
                method.DeclaringType == typeof(QuestionnaireDefinitionVersion) &&
                !method.IsStatic &&
                !method.IsSpecialName));
        Assert.Empty(typeof(ClinicalRuleSetVersion)
            .GetMethods()
            .Where(method =>
                method.DeclaringType == typeof(ClinicalRuleSetVersion) &&
                !method.IsStatic &&
                !method.IsSpecialName));
    }

    private static PreTriageSession CreateAnonymousSession(EntityId questionnaireVersionId)
    {
        return PreTriageSession.CreateAnonymous(
            questionnaireVersionId,
            AnonymousCapabilityHash.FromHash(Guid.NewGuid().ToString("N")),
            Utc(12).AddHours(24),
            Utc(12));
    }

    private static PreTriageEpisode CreateAnonymousEpisode(DateTimeOffset? expiresAt = null)
    {
        var session = CreateAnonymousSession(CreateQuestionnaire().Id);
        return PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14),
            expiresAt ?? Utc(14).AddHours(24));
    }

    private static QuestionnaireDefinitionVersion CreateQuestionnaire(
        string code = "test-questionnaire",
        bool includeQuestion = false)
    {
        return QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create(code),
            DefinitionVersion.Create("test-version-1"),
            DefinitionHash.FromHash(new string('c', 64)),
            Utc(12),
            Utc(11),
            Utc(13),
            "test/import/source",
            questions: includeQuestion
                ? [new TriageQuestionInput(
                    QuestionCode.Create("test-question"),
                    "Test-only question text",
                    1)]
                : null);
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 21, hour, 0, 0, TimeSpan.Zero);
    }
}
