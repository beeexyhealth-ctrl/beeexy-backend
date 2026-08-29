using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

public sealed class GetDoctor(IDoctorDirectoryReadRepository repository)
{
    public async Task<DoctorDirectoryProfile> ExecuteAsync(
        EntityId doctorId,
        CancellationToken cancellationToken = default)
    {
        var doctor = await repository.GetAsync(doctorId, cancellationToken);
        return doctor ?? throw new DoctorNotFoundException();
    }
}

public sealed class DoctorNotFoundException : Exception;
