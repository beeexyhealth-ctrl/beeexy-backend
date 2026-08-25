using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FhirExportReadRepository(BeeexyDbContext dbContext)
    : IFhirExportReadRepository
{
    public async Task<FhirExportReadState?> FindAsync(
        EntityId fhirExportId,
        CancellationToken cancellationToken = default)
    {
        var export = await dbContext.FhirExports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == fhirExportId,
                cancellationToken);
        if (export is null)
        {
            return null;
        }

        var validation = await dbContext.FhirValidationResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.FhirExportId == fhirExportId,
                cancellationToken);
        return new FhirExportReadState(export, validation);
    }
}
