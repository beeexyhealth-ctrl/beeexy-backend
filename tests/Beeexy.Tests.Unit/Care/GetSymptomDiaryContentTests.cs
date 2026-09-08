using Beeexy.Application.Care;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase94")]
public sealed class GetSymptomDiaryContentTests
{
    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    [InlineData("CHEST_PAIN")]
    public async Task AuthorizedEpisodeReturnsOnlyItsPersistedPathwayPackage(string pathwayValue)
    {
        var fixture = new Fixture();
        var pathway = ClinicalPathwayCode.Create(pathwayValue);
        fixture.SetEpisode(pathway);
        fixture.ContentProvider.Result = Content(pathway);

        var result = await fixture.ExecuteAsync();

        Assert.Equal(fixture.EpisodeRepository.Result!.EpisodeId, result.EpisodeId);
        Assert.Equal(pathway, result.Pathway);
        Assert.Same(fixture.ContentProvider.Result, result.Package);
        Assert.Equal(pathway, fixture.ContentProvider.RequestedPathway);
    }

    [Fact]
    public async Task ResultPreservesImmutableIdentityAndEverySourceOrder()
    {
        var fixture = new Fixture();
        var content = Content(ClinicalPathways.ChestPain);
        fixture.SetEpisode(ClinicalPathways.ChestPain);
        fixture.ContentProvider.Result = content;

        var result = await fixture.ExecuteAsync();

        Assert.Equal(content.PackageVersionId, result.Package.PackageVersionId);
        Assert.Equal(content.CanonicalContentHash, result.Package.CanonicalContentHash);
        Assert.Equal(
            content.Definition.Questions.Select(value => value.SourceOrder),
            result.Package.Definition.Questions.Select(value => value.SourceOrder));
        Assert.Equal(
            content.Definition.Questions.SelectMany(question =>
                question.Options.Select(option => option.SourceOrder)),
            result.Package.Definition.Questions.SelectMany(question =>
                question.Options.Select(option => option.SourceOrder)));
        Assert.Equal(
            content.Definition.WarningSigns.Select(value => value.SourceOrder),
            result.Package.Definition.WarningSigns.Select(value => value.SourceOrder));
    }

    [Fact]
    public async Task OtherSymptomsAndProviderFilteredInactiveOrUnapprovedContentFailClosed()
    {
        var fixture = new Fixture();
        fixture.SetEpisode(ClinicalPathways.OtherSymptoms);
        fixture.ContentProvider.Result = null;

        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(
            fixture.ExecuteAsync);

        Assert.Equal(ClinicalPathways.OtherSymptoms, fixture.ContentProvider.RequestedPathway);
    }

    [Fact]
    public async Task CrossPathwayOrIntegrityFailedContentFailsClosed()
    {
        var fixture = new Fixture();
        fixture.SetEpisode(ClinicalPathways.Headache);
        fixture.ContentProvider.Result = Content(ClinicalPathways.Fever);

        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(
            fixture.ExecuteAsync);

        fixture.ContentProvider.Result = null;
        fixture.ContentProvider.Error = new SymptomDiaryPackageIntegrityException(
            "Sensitive internal package detail");
        var failure = await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(
            fixture.ExecuteAsync);
        Assert.DoesNotContain("Sensitive", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IneligibleOrEmptyEpisodeIsConcealedBeforeAuthorizationAndContentLookup()
    {
        var fixture = new Fixture();
        fixture.EpisodeRepository.Result = null;

        await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(
            () => fixture.UseCase.ExecuteAsync(EntityId.New()));
        await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(() =>
            fixture.UseCase.ExecuteAsync(default));

        Assert.Equal(0, fixture.AuthorizationRepository.CallCount);
        Assert.Equal(0, fixture.ContentProvider.CallCount);
    }

    [Fact]
    public async Task UnauthorizedOwnershipUsesTheSameConcealedFailureAndSkipsContent()
    {
        var fixture = new Fixture();
        var inaccessiblePatient = EntityId.New();
        fixture.SetEpisode(ClinicalPathways.Headache, inaccessiblePatient);
        fixture.AuthorizationRepository.Set(inaccessiblePatient, targetExists: true);

        var failure = await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(
            fixture.ExecuteAsync);

        Assert.IsType<SymptomDiaryEpisodeNotFoundException>(failure);
        Assert.Equal(1, fixture.AuthorizationRepository.CallCount);
        Assert.Equal(0, fixture.ContentProvider.CallCount);
    }

    private static SymptomDiaryPackageContent Content(ClinicalPathwayCode pathway)
    {
        var definition = AndreaSymptomDiaryPackages.Create(pathway);
        return new SymptomDiaryPackageContent(
            EntityId.New(),
            definition.ExpectedContentHash!,
            definition);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            SetEpisode(ClinicalPathways.Headache);
            ContentProvider.Result = Content(ClinicalPathways.Headache);
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                Profiles.Resolver,
                AuthorizationRepository,
                Profiles.MyCircleAudit);
            UseCase = new GetSymptomDiaryContent(
                authorizer,
                EpisodeRepository,
                ContentProvider);
        }

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public FakeEpisodeRepository EpisodeRepository { get; } = new();

        public FakeContentProvider ContentProvider { get; } = new();

        public GetSymptomDiaryContent UseCase { get; }

        public Task<SymptomDiaryContentForEpisode> ExecuteAsync() =>
            UseCase.ExecuteAsync(EpisodeRepository.Result!.EpisodeId);

        public void SetEpisode(
            ClinicalPathwayCode pathway,
            EntityId? patientProfileId = null) =>
            EpisodeRepository.Result = new EligibleSymptomDiaryEpisode(
                EntityId.New(),
                patientProfileId ?? Profiles.PrimaryProfile.Id,
                pathway);
    }

    private sealed class FakeEpisodeRepository : ISymptomDiaryEpisodeReadRepository
    {
        public EligibleSymptomDiaryEpisode? Result { get; set; }

        public Task<EligibleSymptomDiaryEpisode?> GetEligibleAsync(
            EntityId episodeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result is not null && Result.EpisodeId == episodeId
                ? Result
                : null);
    }

    private sealed class FakeContentProvider : ISymptomDiaryContentProvider
    {
        public SymptomDiaryPackageContent? Result { get; set; }

        public Exception? Error { get; set; }

        public int CallCount { get; private set; }

        public ClinicalPathwayCode? RequestedPathway { get; private set; }

        public Task<SymptomDiaryPackageContent?> GetActivePackageAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedPathway = pathway;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(Result);
        }

        public Task<SymptomDiaryPackageContent?> GetExactPackageAsync(
            EntityId packageVersionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuthorizationRepository
        : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> lookups = [];

        public int CallCount { get; private set; }

        public void Set(
            EntityId targetProfileId,
            bool targetExists,
            EntityId? relationshipId = null) =>
            lookups[targetProfileId] = new PatientAccessAuthorizationLookup(
                targetExists,
                relationshipId);

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(lookups[targetProfileId]);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    }
}
