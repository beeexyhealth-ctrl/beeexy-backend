using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Beeexy.Domain.Directory;

namespace Beeexy.Application.Directory;

public static class ProductApprovedDoctorMatchRule
{
    public const string Version = "2026.08.29-demo.1";
}

public static class DoctorMatchFactorCodes
{
    public const string Specialty = "specialty_exact";
    public const string Language = "language_exact";
    public const string Location = "location_exact";
    public const string StoredInsurance = "stored_insurance_participation_exact";

    public static readonly IReadOnlyList<string> Ordered =
    [
        Specialty,
        Language,
        Location,
        StoredInsurance
    ];
}

public static class DoctorMatchRuleSemantics
{
    public const string FormulaCode = "sum_matched_weight_points_no_reweight_v1";
    public const string MissingInputCode = "not_applicable_zero_contribution_v1";
    public const string TieBreakCode = "score_desc_uuid_text_asc_v1";
}

public static class DoctorMatchFactorSemanticsCodes
{
    public const string Specialty = "exact_canonical_doctor_specialty_relationship_v1";
    public const string Language = "exact_canonical_doctor_language_relationship_v1";
    public const string Location = "exact_same_eligible_affiliation_location_fields_v1";
    public const string StoredInsurance =
        "exact_stored_doctor_insurance_participation_v1";

    public static string For(string factorCode) => factorCode switch
    {
        DoctorMatchFactorCodes.Specialty => Specialty,
        DoctorMatchFactorCodes.Language => Language,
        DoctorMatchFactorCodes.Location => Location,
        DoctorMatchFactorCodes.StoredInsurance => StoredInsurance,
        _ => throw new ArgumentOutOfRangeException(nameof(factorCode))
    };
}

public sealed record DoctorMatchRuleFactorDefinition(
    string Code,
    string SemanticsCode,
    int WeightPoints);

public sealed class DoctorMatchRulePackage
{
    private DoctorMatchRulePackage(
        DirectoryCode packageCode,
        DirectoryCode version,
        DateTimeOffset createdAt,
        DoctorMatchRuleFactorDefinition[] factors)
    {
        PackageCode = packageCode;
        Version = version;
        CreatedAt = createdAt;
        Factors = Array.AsReadOnly(factors);
        ContentHash = DoctorMatchRuleIntegrity.Calculate(this);
    }

    public DirectoryCode PackageCode { get; }

    public DirectoryCode Version { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<DoctorMatchRuleFactorDefinition> Factors { get; }

    public string ContentHash { get; }

    public static DoctorMatchRulePackage Create(
        DirectoryCode packageCode,
        DirectoryCode version,
        DateTimeOffset createdAt,
        IEnumerable<DoctorMatchRuleFactorDefinition> factors)
    {
        ArgumentNullException.ThrowIfNull(packageCode);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(factors);
        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The rule-package timestamp must be UTC.", nameof(createdAt));
        }

        var values = factors.ToArray();
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Rule factors cannot contain null values.", nameof(factors));
        }

        return new DoctorMatchRulePackage(packageCode, version, createdAt, values);
    }
}

public static class DoctorMatchRuleIntegrity
{
    public static string Calculate(DoctorMatchRulePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var canonical = new StringBuilder()
            .Append("packageCode=").Append(package.PackageCode.Value).Append('\n')
            .Append("version=").Append(package.Version.Value).Append('\n')
            .Append("createdAt=").Append(package.CreatedAt.ToString("O", CultureInfo.InvariantCulture))
            .Append('\n')
            .Append("formula=").Append(DoctorMatchRuleSemantics.FormulaCode).Append('\n')
            .Append("missingInput=").Append(DoctorMatchRuleSemantics.MissingInputCode).Append('\n')
            .Append("tieBreak=").Append(DoctorMatchRuleSemantics.TieBreakCode).Append('\n');
        foreach (var factor in package.Factors)
        {
            canonical.Append("factor=")
                .Append(factor.Code)
                .Append(':')
                .Append(factor.SemanticsCode)
                .Append(':')
                .Append(factor.WeightPoints.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }
}

public sealed class DoctorMatchRulePackageValidator
{
    public void Validate(DoctorMatchRulePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Factors.Count != DoctorMatchFactorCodes.Ordered.Count)
        {
            throw Invalid("The demo matching package must contain exactly four factors.");
        }

        var codes = package.Factors.Select(factor => factor.Code).ToArray();
        if (codes.Any(string.IsNullOrWhiteSpace) ||
            codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
        {
            throw Invalid("Demo matching factor codes must be non-empty and unique.");
        }

        if (!codes.SequenceEqual(DoctorMatchFactorCodes.Ordered, StringComparer.Ordinal))
        {
            throw Invalid("The demo matching package contains unknown or incorrectly ordered factors.");
        }

        if (package.Factors.Any(factor =>
            !string.Equals(
                factor.SemanticsCode,
                DoctorMatchFactorSemanticsCodes.For(factor.Code),
                StringComparison.Ordinal)))
        {
            throw Invalid("The demo matching package contains unknown factor semantics.");
        }

        if (package.Factors.Any(factor => factor.WeightPoints is < 1 or > 100) ||
            package.Factors.Sum(factor => factor.WeightPoints) !=
                DoctorMatchRuleConfiguration.MaximumScorePoints)
        {
            throw Invalid("Demo matching weights must be positive integer points totaling 100.");
        }

        if (!string.Equals(
            package.ContentHash,
            DoctorMatchRuleIntegrity.Calculate(package),
            StringComparison.Ordinal))
        {
            throw Invalid("Demo matching content does not match its immutable content hash.");
        }
    }

    private static DoctorMatchRuleValidationException Invalid(string message) => new(message);
}

public enum DoctorMatchRuleImportOutcome
{
    Imported,
    AlreadyImported
}

public sealed record DoctorMatchRuleImportResult(
    DoctorMatchRuleImportOutcome Outcome,
    DirectoryCode PackageCode,
    DirectoryCode Version,
    string ContentHash);

public interface IDoctorMatchRuleImporter
{
    Task<DoctorMatchRuleImportResult> ImportAsync(
        DoctorMatchRulePackage package,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorMatchRuleValidationException(string message) : Exception(message);

public sealed class DoctorMatchRuleImportConflictException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
