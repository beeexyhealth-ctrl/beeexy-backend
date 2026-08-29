using System.Data;
using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.DirectoryServices;

public sealed class DirectoryImporter(
    BeeexyDbContext dbContext,
    DirectoryImportPackageValidator validator,
    ILogger<DirectoryImporter> logger) : IDirectoryImporter
{
    public async Task<DirectoryImportResult> ImportAsync(
        DirectoryImportPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        validator.Validate(package);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await AcquirePackageLockAsync(package, cancellationToken);

        var existing = await dbContext.Set<DirectoryImportRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.PackageCode == package.PackageCode &&
                value.Version == package.Version,
                cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, package.ContentHash, StringComparison.Ordinal))
            {
                throw new DirectoryImportConflictException(
                    "An immutable demo directory package version already exists with different " +
                    "content. Import a new version instead of mutating it.");
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Synthetic demo directory package {PackageCode}@{Version} with content hash " +
                "{ContentHash} is already imported.",
                package.PackageCode.Value,
                package.Version.Value,
                package.ContentHash);
            return Result(DirectoryImportOutcome.AlreadyImported, package);
        }

        AddPackageGraph(package);
        dbContext.Set<DirectoryImportRecord>().Add(DirectoryImportRecord.Create(
            package,
            DateTimeOffset.UtcNow,
            CreateImportId(package)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw new DirectoryImportConflictException(
                "The synthetic demo directory package conflicts with existing directory data. " +
                "No package records were imported.",
                exception);
        }

        logger.LogInformation(
            "Imported synthetic demo directory package {PackageCode}@{Version} with content hash " +
            "{ContentHash}. Published and Verified values apply only within the approved demo dataset.",
            package.PackageCode.Value,
            package.Version.Value,
            package.ContentHash);
        return Result(DirectoryImportOutcome.Imported, package);
    }

    private void AddPackageGraph(DirectoryImportPackage package)
    {
        dbContext.AddRange(package.Clinics);
        dbContext.AddRange(package.ClinicLocations);
        dbContext.AddRange(package.Doctors);
        dbContext.AddRange(package.Specialties);
        dbContext.AddRange(package.Languages);
        dbContext.AddRange(package.InsurancePlans);
        dbContext.AddRange(package.DoctorAffiliations);
        dbContext.AddRange(package.DoctorCredentials);
        dbContext.AddRange(package.DoctorSpecialties);
        dbContext.AddRange(package.DoctorLanguages);
        dbContext.AddRange(package.DoctorInsuranceParticipations);
    }

    private async Task AcquirePackageLockAsync(
        DirectoryImportPackage package,
        CancellationToken cancellationToken)
    {
        var identity = $"{package.PackageCode.Value}@{package.Version.Value}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0));",
            cancellationToken);
    }

    private static EntityId CreateImportId(DirectoryImportPackage package)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{package.PackageCode.Value}@{package.Version.Value}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return EntityId.From(new Guid(guidBytes));
    }

    private static DirectoryImportResult Result(
        DirectoryImportOutcome outcome,
        DirectoryImportPackage package) =>
        new(outcome, package.PackageCode, package.Version, package.ContentHash);
}
