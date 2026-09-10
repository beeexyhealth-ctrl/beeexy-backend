using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Care;

public sealed class GetSymptomDiaryContent(
    AuthorizePatientAccess authorizePatientAccess,
    ISymptomDiaryEpisodeReadRepository episodeRepository,
    ISymptomDiaryContentProvider contentProvider)
{
    public async Task<SymptomDiaryContentForEpisode> ExecuteAsync(
        EntityId episodeId,
        CancellationToken cancellationToken = default)
    {
        if (episodeId.Value == Guid.Empty)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var episode = await episodeRepository.GetEligibleAsync(
            episodeId,
            cancellationToken);
        if (episode is null)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            episode.PatientProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        SymptomDiaryPackageContent? content;
        try
        {
            content = await contentProvider.GetActivePackageAsync(
                episode.Pathway,
                cancellationToken);
        }
        catch (SymptomDiaryPackageIntegrityException)
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        if (content is null || content.Definition.Pathway != episode.Pathway)
        {
            throw new SymptomDiaryContentUnavailableException();
        }

        return new SymptomDiaryContentForEpisode(
            episode.EpisodeId,
            episode.Pathway,
            content);
    }
}

public interface ISymptomDiaryEpisodeReadRepository
{
    Task<EligibleSymptomDiaryEpisode?> GetEligibleAsync(
        EntityId episodeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EligibleSymptomDiaryEpisode>> ListEligibleAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EligibleSymptomDiaryEpisode>>([]);
}

public sealed record EligibleSymptomDiaryEpisode(
    EntityId EpisodeId,
    EntityId PatientProfileId,
    ClinicalPathwayCode Pathway);

public sealed record SymptomDiaryContentForEpisode(
    EntityId EpisodeId,
    ClinicalPathwayCode Pathway,
    SymptomDiaryPackageContent Package);

public sealed class SymptomDiaryEpisodeNotFoundException : Exception;

public sealed class SymptomDiaryContentUnavailableException : Exception;
