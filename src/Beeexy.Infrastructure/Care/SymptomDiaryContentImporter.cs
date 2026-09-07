using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Beeexy.Infrastructure.Care;

public sealed class SymptomDiaryContentImporter(
    BeeexyDbContext dbContext,
    SymptomDiaryPackageValidator validator,
    SymptomDiaryPackageCanonicalSerializer serializer,
    SymptomDiaryPackageHashCalculator hashCalculator,
    ILogger<SymptomDiaryContentImporter> logger) : ISymptomDiaryContentImporter
{
    private const string ImmutableIdentityConstraint =
        "ux_symptom_diary_packages_code_version";

    public async Task<SymptomDiaryPackageImportResult> ImportAsync(
        SymptomDiaryPackageDefinition package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        validator.Validate(package);
        var canonical = serializer.Serialize(package);
        var calculatedHash = hashCalculator.Calculate(package);
        if (package.ExpectedContentHash is not null &&
            package.ExpectedContentHash != calculatedHash)
        {
            throw new SymptomDiaryPackageValidationException(
                "The supplied content hash does not match the canonical package content.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var existing = await FindExistingAsync(package, cancellationToken);
        if (existing is not null)
        {
            var result = EnsureExistingMatches(existing, package, canonical, calculatedHash);
            await transaction.CommitAsync(cancellationToken);
            LogAlreadyImported(package, existing.Id);
            return result;
        }

        var entity = SymptomDiaryPackageMapper.ToEntity(package, calculatedHash);
        dbContext.SymptomDiaryPackageVersions.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConcurrentIdentityConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var concurrentWinner = await FindExistingAsync(package, cancellationToken) ??
                throw new SymptomDiaryPackageConflictException(
                    "A concurrent package import conflicted without producing a complete version.");
            var result = EnsureExistingMatches(
                concurrentWinner,
                package,
                canonical,
                calculatedHash);
            LogAlreadyImported(package, concurrentWinner.Id);
            return result;
        }

        logger.LogInformation(
            "Imported symptom-diary package {PackageCode}/{PackageVersion} for {Pathway} " +
            "with source {Source}, review status {ReviewStatus}, and approval status {ApprovalStatus}",
            package.PackageCode.Value,
            package.PackageVersion.Value,
            package.Pathway.Value,
            package.ContentStatus.Source,
            package.ContentStatus.ReviewStatus,
            package.ContentStatus.ApprovalStatus);
        return Result(SymptomDiaryPackageImportOutcome.Imported, entity, calculatedHash);
    }

    private async Task<SymptomDiaryPackageVersion?> FindExistingAsync(
        SymptomDiaryPackageDefinition package,
        CancellationToken cancellationToken)
    {
        return await dbContext.SymptomDiaryPackageVersions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .Include(value => value.WarningSigns)
            .SingleOrDefaultAsync(
                value => value.PackageCode == package.PackageCode &&
                    value.PackageVersion == package.PackageVersion,
                cancellationToken);
    }

    private SymptomDiaryPackageImportResult EnsureExistingMatches(
        SymptomDiaryPackageVersion existing,
        SymptomDiaryPackageDefinition incoming,
        string incomingCanonical,
        SymptomDiarySha256 incomingHash)
    {
        var persisted = SymptomDiaryPackageMapper.ToContent(existing);
        try
        {
            validator.Validate(persisted.Definition);
        }
        catch (SymptomDiaryPackageValidationException exception)
        {
            throw new SymptomDiaryPackageIntegrityException(
                "The existing immutable symptom-diary package is structurally invalid: " +
                exception.Message);
        }

        var persistedCanonical = serializer.Serialize(persisted.Definition);
        var persistedHash = hashCalculator.Calculate(persisted.Definition);
        if (persisted.CanonicalContentHash != persistedHash)
        {
            throw new SymptomDiaryPackageIntegrityException(
                "The existing immutable symptom-diary package failed content-hash validation.");
        }

        if (persistedHash != incomingHash ||
            !string.Equals(persistedCanonical, incomingCanonical, StringComparison.Ordinal))
        {
            throw new SymptomDiaryPackageConflictException(
                $"Package '{incoming.PackageCode}/{incoming.PackageVersion}' already exists " +
                "with different immutable content. Import a new version instead.");
        }

        return Result(
            SymptomDiaryPackageImportOutcome.AlreadyImported,
            existing,
            persisted.CanonicalContentHash);
    }

    private static bool IsConcurrentIdentityConflict(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ImmutableIdentityConstraint
        };

    private static SymptomDiaryPackageImportResult Result(
        SymptomDiaryPackageImportOutcome outcome,
        SymptomDiaryPackageVersion entity,
        SymptomDiarySha256 hash) => new(
            outcome,
            entity.Id,
            entity.PackageCode,
            entity.PackageVersion,
            hash);

    private void LogAlreadyImported(
        SymptomDiaryPackageDefinition package,
        Beeexy.Domain.Common.EntityId packageVersionId)
    {
        logger.LogInformation(
            "Symptom-diary package {PackageCode}/{PackageVersion} for {Pathway} already " +
            "exists as {PackageVersionId}; immutable content was retained",
            package.PackageCode.Value,
            package.PackageVersion.Value,
            package.Pathway.Value,
            packageVersionId.Value);
    }
}
