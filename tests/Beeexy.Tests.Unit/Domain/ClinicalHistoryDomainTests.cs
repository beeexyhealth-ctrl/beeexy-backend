using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;

namespace Beeexy.Tests.Unit.Domain;

public sealed class ClinicalHistoryDomainTests
{
    [Fact]
    public void CompletedPreTriageEvent_ReferencesOwnedAuthoritativeSourceAndVersions()
    {
        var episode = CreateOwnedEpisode();

        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(15));

        Assert.NotEqual(Guid.Empty, historyEvent.Id.Value);
        Assert.Equal(episode.PatientProfileId, historyEvent.PatientProfileId);
        Assert.Equal(
            ClinicalHistoryEventType.CompletedPreTriage,
            historyEvent.EventType);
        Assert.Equal(
            AuthoritativeClinicalSourceType.PreTriageEpisode,
            historyEvent.SourceType);
        Assert.Equal(episode.Id, historyEvent.SourceId);
        Assert.Equal(episode.QuestionnaireVersionId,
            historyEvent.SourceQuestionnaireVersionId);
        Assert.Equal(episode.ClinicalRuleSetVersionId,
            historyEvent.SourceClinicalRuleSetVersionId);
        Assert.Equal(episode.CompletedAt, historyEvent.OccurredAt);
        Assert.Equal(Utc(15), historyEvent.RecordedAt);
    }

    [Fact]
    public void EventCreation_RejectsUnownedEpisodeAndTimestampBeforeOccurrence()
    {
        var anonymousEpisode = CreateAnonymousEpisode();
        var ownedEpisode = CreateOwnedEpisode();

        Assert.Throws<InvalidOperationException>(() =>
            ClinicalHistoryEvent.CreateCompletedPreTriage(
                anonymousEpisode,
                Utc(15)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ClinicalHistoryEvent.CreateCompletedPreTriage(
                ownedEpisode,
                ownedEpisode.CompletedAt.AddTicks(-1)));
    }

    [Fact]
    public void EventCreation_RejectsUnsupportedTypeOrMismatchedSourceAndProvenance()
    {
        var episode = CreateOwnedEpisode();
        var otherEpisode = CreateOwnedEpisode();
        var validReference =
            AuthoritativeSourceReference.ForPreTriageEpisode(episode.Id);
        var validProvenance =
            ClinicalSourceProvenance.FromCompletedPreTriage(episode);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ClinicalHistoryEvent.Create(
                episode,
                (ClinicalHistoryEventType)999,
                validReference,
                validProvenance,
                Utc(15)));
        Assert.Throws<ArgumentException>(() =>
            ClinicalHistoryEvent.Create(
                episode,
                ClinicalHistoryEventType.CompletedPreTriage,
                AuthoritativeSourceReference.ForPreTriageEpisode(otherEpisode.Id),
                validProvenance,
                Utc(15)));
        Assert.Throws<ArgumentException>(() =>
            ClinicalHistoryEvent.Create(
                episode,
                ClinicalHistoryEventType.CompletedPreTriage,
                validReference,
                ClinicalSourceProvenance.FromCompletedPreTriage(otherEpisode),
                Utc(15)));
    }

    [Fact]
    public void Amendment_PreservesImmutableEventSourceAuthorReasonAndTime()
    {
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            CreateOwnedEpisode(),
            Utc(15));
        var authorId = EntityId.New();
        var reason = AmendmentReason.Create("  Correct patient-reported duration  ");

        var amendment = ClinicalAmendment.Create(
            historyEvent,
            authorId,
            reason,
            Utc(16));

        Assert.Equal(historyEvent.Id, amendment.ClinicalHistoryEventId);
        Assert.Equal(historyEvent.SourceReference, amendment.SourceReference);
        Assert.Equal(historyEvent.SourceProvenance, amendment.SourceProvenance);
        Assert.Equal(authorId, amendment.AuthorAccountId);
        Assert.Equal("Correct patient-reported duration", amendment.Reason.Value);
        Assert.Equal(Utc(16), amendment.CreatedAt);
        Assert.All(
            typeof(ClinicalAmendment).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.Empty(typeof(ClinicalAmendment)
            .GetMethods()
            .Where(method =>
                method.DeclaringType == typeof(ClinicalAmendment) &&
                !method.IsStatic &&
                !method.IsSpecialName));
    }

    [Fact]
    public void AmendmentCreation_RejectsBlankReasonMismatchedProvenanceAndEarlyTime()
    {
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            CreateOwnedEpisode(),
            Utc(15));
        var otherEpisode = CreateOwnedEpisode();
        var authorId = EntityId.New();
        var reason = AmendmentReason.Create("Correction reason");

        Assert.Throws<ArgumentException>(() => AmendmentReason.Create("  "));
        Assert.Throws<ArgumentException>(() => ClinicalAmendment.Create(
            historyEvent,
            AuthoritativeSourceReference.ForPreTriageEpisode(otherEpisode.Id),
            historyEvent.SourceProvenance,
            authorId,
            reason,
            Utc(16)));
        Assert.Throws<ArgumentException>(() => ClinicalAmendment.Create(
            historyEvent,
            historyEvent.SourceReference,
            ClinicalSourceProvenance.FromCompletedPreTriage(otherEpisode),
            authorId,
            reason,
            Utc(16)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClinicalAmendment.Create(
            historyEvent,
            authorId,
            reason,
            historyEvent.RecordedAt.AddTicks(-1)));
    }

    [Fact]
    public void HistoryEntities_AreReferencesWithoutArbitraryClinicalJsonPayloads()
    {
        var properties = typeof(ClinicalHistoryEvent).GetProperties()
            .Concat(typeof(ClinicalAmendment).GetProperties())
            .ToArray();

        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Json", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Result", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ClinicalHistoryEvent).GetMethods(),
            method =>
                method.DeclaringType == typeof(ClinicalHistoryEvent) &&
                !method.IsStatic &&
                !method.IsSpecialName);
    }

    private static PreTriageEpisode CreateOwnedEpisode()
    {
        var session = PreTriageSession.CreateForPatient(
            EntityId.New(),
            EntityId.New(),
            Utc(20),
            Utc(12));
        return PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14));
    }

    private static PreTriageEpisode CreateAnonymousEpisode()
    {
        var session = PreTriageSession.CreateAnonymous(
            EntityId.New(),
            AnonymousCapabilityHash.FromHash(Guid.NewGuid().ToString("N")),
            Utc(20),
            Utc(12));
        return PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14),
            Utc(20));
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 23, hour, 0, 0, TimeSpan.Zero);
    }
}
