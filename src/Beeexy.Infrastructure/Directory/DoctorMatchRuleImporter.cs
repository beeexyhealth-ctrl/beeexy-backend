using System.Data;
using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.DirectoryServices;

public sealed class DoctorMatchRuleImporter(
    BeeexyDbContext dbContext,
    DoctorMatchRulePackageValidator validator,
    ILogger<DoctorMatchRuleImporter> logger) : IDoctorMatchRuleImporter
{
    public async Task<DoctorMatchRuleImportResult> ImportAsync(
        DoctorMatchRulePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        validator.Validate(package);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await AcquirePackageLockAsync(package, cancellationToken);

        var existingVersion = await dbContext.DoctorMatchRuleVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Version == package.Version, cancellationToken);
        if (existingVersion is not null)
        {
            var existingConfiguration = await dbContext.DoctorMatchRuleConfigurations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.RuleVersionId == existingVersion.Id,
                    cancellationToken);
            EnsureExistingMatches(package, existingVersion, existingConfiguration);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Demo doctor matching package {PackageCode}@{Version} with content hash " +
                "{ContentHash} is already imported.",
                package.PackageCode.Value,
                package.Version.Value,
                package.ContentHash);
            return Result(DoctorMatchRuleImportOutcome.AlreadyImported, package);
        }

        var version = DoctorMatchRuleVersion.Create(
            package.Version,
            package.CreatedAt,
            CreateRuleVersionId(package));
        var configuration = DoctorMatchRuleConfiguration.Create(
            version.Id,
            package.PackageCode,
            package.ContentHash,
            Weight(package, DoctorMatchFactorCodes.Specialty),
            Weight(package, DoctorMatchFactorCodes.Language),
            Weight(package, DoctorMatchFactorCodes.Location),
            Weight(package, DoctorMatchFactorCodes.StoredInsurance));
        dbContext.DoctorMatchRuleVersions.Add(version);
        dbContext.DoctorMatchRuleConfigurations.Add(configuration);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw new DoctorMatchRuleImportConflictException(
                "The demo doctor matching package conflicts with existing immutable rule data.",
                exception);
        }

        logger.LogInformation(
            "Imported demo-only doctor matching package {PackageCode}@{Version} with content hash " +
            "{ContentHash}. Its equal factor weights are not clinically validated or production " +
            "recommendation logic.",
            package.PackageCode.Value,
            package.Version.Value,
            package.ContentHash);
        return Result(DoctorMatchRuleImportOutcome.Imported, package);
    }

    private async Task AcquirePackageLockAsync(
        DoctorMatchRulePackage package,
        CancellationToken cancellationToken)
    {
        var identity = $"{package.PackageCode.Value}@{package.Version.Value}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 75));",
            cancellationToken);
    }

    private static void EnsureExistingMatches(
        DoctorMatchRulePackage package,
        DoctorMatchRuleVersion version,
        DoctorMatchRuleConfiguration? configuration)
    {
        if (configuration is null ||
            version.CreatedAt != package.CreatedAt ||
            configuration.PackageCode != package.PackageCode ||
            !string.Equals(configuration.ContentHash, package.ContentHash, StringComparison.Ordinal) ||
            configuration.SpecialtyWeightPoints != Weight(package, DoctorMatchFactorCodes.Specialty) ||
            configuration.LanguageWeightPoints != Weight(package, DoctorMatchFactorCodes.Language) ||
            configuration.LocationWeightPoints != Weight(package, DoctorMatchFactorCodes.Location) ||
            configuration.StoredInsuranceWeightPoints !=
                Weight(package, DoctorMatchFactorCodes.StoredInsurance))
        {
            throw new DoctorMatchRuleImportConflictException(
                "An immutable demo doctor matching version already exists with different or " +
                "incomplete content. Import a new version instead of mutating it.");
        }
    }

    private static int Weight(DoctorMatchRulePackage package, string factorCode) =>
        package.Factors.Single(factor => factor.Code == factorCode).WeightPoints;

    private static EntityId CreateRuleVersionId(DoctorMatchRulePackage package)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{package.PackageCode.Value}@{package.Version.Value}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return EntityId.From(new Guid(guidBytes));
    }

    private static DoctorMatchRuleImportResult Result(
        DoctorMatchRuleImportOutcome outcome,
        DoctorMatchRulePackage package) =>
        new(outcome, package.PackageCode, package.Version, package.ContentHash);
}
