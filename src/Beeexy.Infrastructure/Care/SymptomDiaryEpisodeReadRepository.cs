using Beeexy.Application.Care;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Care;

internal sealed class SymptomDiaryEpisodeReadRepository(BeeexyDbContext dbContext)
    : ISymptomDiaryEpisodeReadRepository
{
    public async Task<EligibleSymptomDiaryEpisode?> GetEligibleAsync(
        EntityId episodeId,
        CancellationToken cancellationToken = default)
    {
        var source = await (
                from episode in dbContext.PreTriageEpisodes.AsNoTracking()
                join questionnaire in dbContext.QuestionnaireVersions.AsNoTracking()
                    on episode.QuestionnaireVersionId equals questionnaire.Id
                where episode.Id == episodeId
                select new
                {
                    EpisodeId = episode.Id,
                    episode.PatientProfileId,
                    questionnaire.Pathway
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (source?.PatientProfileId is not { } patientProfileId)
        {
            return null;
        }

        return new EligibleSymptomDiaryEpisode(
            source.EpisodeId,
            patientProfileId,
            source.Pathway);
    }
}
