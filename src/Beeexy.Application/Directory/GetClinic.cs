using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

public sealed class GetClinic(IClinicDirectoryReadRepository repository)
{
    public async Task<ClinicDirectoryDetail> ExecuteAsync(
        EntityId clinicId,
        CancellationToken cancellationToken = default)
    {
        var clinic = await repository.GetAsync(clinicId, cancellationToken);
        return clinic ?? throw new ClinicNotFoundException();
    }
}

public sealed class ClinicNotFoundException : Exception;
