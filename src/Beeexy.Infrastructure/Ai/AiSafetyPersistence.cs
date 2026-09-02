using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Infrastructure.Persistence;

namespace Beeexy.Infrastructure.Ai;

internal sealed class AiSafetyPersistence(BeeexyDbContext dbContext)
    : IAiSafetyPersistence
{
    public void AddApproved(AiResultSnapshot snapshot, AiSafetyValidation validation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(validation);
        if (!validation.DisplayEligible || validation.ResultSnapshotId != snapshot.Id)
        {
            throw new InvalidOperationException(
                "An approved safety decision must reference its displayable snapshot.");
        }

        dbContext.AiResultSnapshots.Add(snapshot);
        dbContext.AiSafetyValidations.Add(validation);
    }

    public void AddRejected(AiSafetyValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.DisplayEligible || validation.ResultSnapshotId is not null)
        {
            throw new InvalidOperationException(
                "A rejected safety decision cannot reference a displayable snapshot.");
        }

        dbContext.AiSafetyValidations.Add(validation);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
