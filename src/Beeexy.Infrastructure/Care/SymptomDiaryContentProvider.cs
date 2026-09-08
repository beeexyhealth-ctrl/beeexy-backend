using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Care;

public sealed class SymptomDiaryContentProvider(
    BeeexyDbContext dbContext,
    SymptomDiaryPackageValidator validator,
    SymptomDiaryPackageHashCalculator hashCalculator) :
    ISymptomDiaryContentProvider,
    ISymptomDiaryExactContentBatchProvider
{
    public async Task<SymptomDiaryPackageContent?> GetActivePackageAsync(
        ClinicalPathwayCode pathway,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        var entity = await GraphQuery()
            .Where(value =>
                value.Pathway == pathway &&
                value.ContentSource != ClinicalContentSource.LegacyUnspecified &&
                value.ReviewStatus == ClinicalReviewStatus.Reviewed &&
                value.ApprovalStatus == ClinicalApprovalStatus.Approved &&
                value.ApprovedAt != null &&
                value.ActivatedAt != null &&
                value.SourceReference != null)
            .OrderByDescending(value => value.ActivatedAt)
            .ThenByDescending(value => value.ImportedAt)
            .ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : BuildVerifiedContent(entity, requireEligible: true);
    }

    public async Task<SymptomDiaryPackageContent?> GetExactPackageAsync(
        EntityId packageVersionId,
        CancellationToken cancellationToken = default)
    {
        if (packageVersionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A package-version identifier is required.", nameof(packageVersionId));
        }

        var entity = await GraphQuery().SingleOrDefaultAsync(
            value => value.Id == packageVersionId,
            cancellationToken);
        return entity is null ? null : BuildVerifiedContent(entity, requireEligible: false);
    }

    public async Task<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>>
        GetExactPackagesAsync(
            IReadOnlyCollection<EntityId> packageVersionIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageVersionIds);
        if (packageVersionIds.Count == 0)
        {
            return new Dictionary<EntityId, SymptomDiaryPackageContent>();
        }

        if (packageVersionIds.Any(id => id.Value == Guid.Empty) ||
            packageVersionIds.Count > ListSymptomCheckIns.MaximumPageSize)
        {
            throw new ArgumentException(
                "A bounded collection of non-empty package identifiers is required.",
                nameof(packageVersionIds));
        }

        var ids = packageVersionIds.Distinct().ToArray();
        var entities = await GraphQuery()
            .Where(entity => ids.Contains(entity.Id))
            .ToArrayAsync(cancellationToken);
        return entities.ToDictionary(
            entity => entity.Id,
            entity => BuildVerifiedContent(entity, requireEligible: false));
    }

    private IQueryable<SymptomDiaryPackageVersion> GraphQuery()
    {
        return dbContext.SymptomDiaryPackageVersions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .Include(value => value.WarningSigns);
    }

    private SymptomDiaryPackageContent BuildVerifiedContent(
        SymptomDiaryPackageVersion entity,
        bool requireEligible)
    {
        try
        {
            var content = SymptomDiaryPackageMapper.ToContent(entity);
            validator.Validate(content.Definition);
            if (requireEligible && !validator.IsDisplayEligible(content.Definition))
            {
                throw new SymptomDiaryPackageValidationException(
                    "The package is not eligible for active display.");
            }

            if (hashCalculator.Calculate(content.Definition) != content.CanonicalContentHash)
            {
                throw new SymptomDiaryPackageValidationException(
                    "The package content does not match its immutable content hash.");
            }

            return content;
        }
        catch (SymptomDiaryPackageValidationException exception)
        {
            throw new SymptomDiaryPackageIntegrityException(
                $"Symptom-diary package '{entity.Id}' failed integrity validation: " +
                exception.Message);
        }
    }
}
