using Beeexy.Application.Directory;
using Beeexy.Domain.Directory;

namespace Beeexy.Infrastructure.DirectoryServices;

public static class ProductApprovedDemoDoctorMatchRule
{
    public const string PackageCode = "beeexy-demo-doctor-match-rules";
    public const string Version = "2026.08.29-demo.1";
    public const string ExpectedContentHash =
        "2aefb8bfb21fadef1ad4bede0d4545988ddfc7c66dc5f79332555773756fd926";

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    public static DoctorMatchRulePackage Create()
    {
        var package = DoctorMatchRulePackage.Create(
            DirectoryCode.Create(PackageCode),
            DirectoryCode.Create(Version),
            CreatedAt,
            [
                new(
                    DoctorMatchFactorCodes.Specialty,
                    DoctorMatchFactorSemanticsCodes.Specialty,
                    25),
                new(
                    DoctorMatchFactorCodes.Language,
                    DoctorMatchFactorSemanticsCodes.Language,
                    25),
                new(
                    DoctorMatchFactorCodes.Location,
                    DoctorMatchFactorSemanticsCodes.Location,
                    25),
                new(
                    DoctorMatchFactorCodes.StoredInsurance,
                    DoctorMatchFactorSemanticsCodes.StoredInsurance,
                    25)
            ]);
        if (!string.Equals(package.ContentHash, ExpectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The approved demo doctor matching configuration changed (actual content hash " +
                $"{package.ContentHash}). Review it and assign a new version and expected hash " +
                "before importing.");
        }

        return package;
    }
}
