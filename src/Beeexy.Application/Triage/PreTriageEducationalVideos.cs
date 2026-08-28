using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed record PreTriageEducationalVideo(
    string Id,
    string Title,
    string Url);

public interface IPreTriageEducationalVideoCatalog
{
    PreTriageEducationalVideo? Find(ClinicalPathwayCode pathway);
}
