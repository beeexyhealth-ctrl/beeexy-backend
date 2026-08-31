using System.Data;
using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Scheduling;

public sealed class AvailabilityImporter(
    BeeexyDbContext dbContext,
    AvailabilityImportPackageValidator validator,
    IClock clock,
    ILogger<AvailabilityImporter> logger) : IAvailabilityImporter
{
    public async Task<AvailabilityImportResult> ImportAsync(
        AvailabilityImportPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        validator.Validate(package);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await AcquirePackageLockAsync(package, cancellationToken);

        var existing = await dbContext.Set<AvailabilityImportRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.PackageCode == package.PackageCode &&
                value.Version == package.Version &&
                value.ReferenceDate == package.ReferenceDate,
                cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, package.ContentHash, StringComparison.Ordinal))
            {
                throw new AvailabilityImportConflictException(
                    "An immutable demo availability package for this reference date already " +
                    "exists with different content. Use a new package version.");
            }

            await transaction.CommitAsync(cancellationToken);
            return Result(AvailabilityImportOutcome.AlreadyImported, package);
        }

        await ValidateDirectoryReferencesAsync(package, cancellationToken);
        dbContext.AvailabilitySlots.AddRange(package.Slots);
        dbContext.Set<AvailabilityImportRecord>().Add(AvailabilityImportRecord.Create(
            package,
            clock.UtcNow.ToUniversalTime(),
            CreateDeterministicId(Identity(package))));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw new AvailabilityImportConflictException(
                "The demo availability package conflicts with existing scheduling data. " +
                "No package records were imported.",
                exception);
        }

        logger.LogInformation(
            "Imported synthetic demo availability package {PackageCode}@{Version} for " +
            "reference date {ReferenceDate} with content hash {ContentHash}.",
            package.PackageCode.Value,
            package.Version.Value,
            package.ReferenceDate,
            package.ContentHash);
        return Result(AvailabilityImportOutcome.Imported, package);
    }

    private async Task ValidateDirectoryReferencesAsync(
        AvailabilityImportPackage package,
        CancellationToken cancellationToken)
    {
        var doctorIds = package.Slots.Select(value => value.DoctorId).Distinct().ToArray();
        var clinicIds = package.Slots.Select(value => value.ClinicId).Distinct().ToArray();
        var locationIds = package.Slots.Select(value => value.ClinicLocationId).Distinct().ToArray();

        var existingDoctors = await dbContext.Doctors.AsNoTracking()
            .Where(value => doctorIds.Contains(value.Id))
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var existingClinics = await dbContext.Clinics.AsNoTracking()
            .Where(value => clinicIds.Contains(value.Id))
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var existingLocations = await dbContext.ClinicLocations.AsNoTracking()
            .Where(value => locationIds.Contains(value.Id))
            .Select(value => new { value.Id, value.ClinicId, TimeZone = value.TimeZone.Value })
            .ToArrayAsync(cancellationToken);
        var existingAffiliations = await dbContext.DoctorAffiliations.AsNoTracking()
            .Where(value => doctorIds.Contains(value.DoctorId) &&
                clinicIds.Contains(value.ClinicId) &&
                value.ClinicLocationId.HasValue)
            .Select(value => new
            {
                value.DoctorId,
                value.ClinicId,
                LocationId = value.ClinicLocationId!.Value
            })
            .ToArrayAsync(cancellationToken);

        if (existingDoctors.Length != doctorIds.Length ||
            existingClinics.Length != clinicIds.Length ||
            existingLocations.Length != locationIds.Length ||
            package.Slots.Any(slot =>
                !existingLocations.Any(location =>
                    location.Id == slot.ClinicLocationId &&
                    location.ClinicId == slot.ClinicId &&
                    string.Equals(location.TimeZone, slot.ClinicTimeZone.Value, StringComparison.Ordinal)) ||
                !existingAffiliations.Any(affiliation =>
                    affiliation.DoctorId == slot.DoctorId &&
                    affiliation.ClinicId == slot.ClinicId &&
                    affiliation.LocationId == slot.ClinicLocationId)))
        {
            throw new AvailabilityImportValidationException(
                "Availability slots must reference existing matching doctors, clinics, " +
                "locations, timezones, and doctor affiliations.");
        }
    }

    private async Task AcquirePackageLockAsync(
        AvailabilityImportPackage package,
        CancellationToken cancellationToken)
    {
        var identity = Identity(package);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0));",
            cancellationToken);
    }

    private static string Identity(AvailabilityImportPackage package) =>
        $"{package.PackageCode.Value}@{package.Version.Value}:{package.ReferenceDate:yyyy-MM-dd}";

    internal static EntityId CreateDeterministicId(string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return EntityId.From(new Guid(guidBytes));
    }

    private static AvailabilityImportResult Result(
        AvailabilityImportOutcome outcome,
        AvailabilityImportPackage package) =>
        new(
            outcome,
            package.PackageCode,
            package.Version,
            package.ReferenceDate,
            package.ContentHash);
}
