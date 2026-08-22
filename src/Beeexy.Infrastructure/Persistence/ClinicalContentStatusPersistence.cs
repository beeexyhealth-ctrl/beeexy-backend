using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Persistence;

internal static class ClinicalContentStatusPersistence
{
    public static string SerializeSource(ClinicalContentSource value) => value switch
    {
        ClinicalContentSource.LegacyUnspecified => "LEGACY_UNSPECIFIED",
        ClinicalContentSource.ReferencePlatformDerived => "REFERENCE_PLATFORM_DERIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static ClinicalContentSource DeserializeSource(string value) => value switch
    {
        "LEGACY_UNSPECIFIED" => ClinicalContentSource.LegacyUnspecified,
        "REFERENCE_PLATFORM_DERIVED" => ClinicalContentSource.ReferencePlatformDerived,
        _ => throw new InvalidOperationException($"Unknown clinical content source '{value}'.")
    };

    public static string SerializeReviewStatus(ClinicalReviewStatus value) => value switch
    {
        ClinicalReviewStatus.Reviewed => "REVIEWED",
        ClinicalReviewStatus.Provisional => "PROVISIONAL",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static ClinicalReviewStatus DeserializeReviewStatus(string value) => value switch
    {
        "REVIEWED" => ClinicalReviewStatus.Reviewed,
        "PROVISIONAL" => ClinicalReviewStatus.Provisional,
        _ => throw new InvalidOperationException($"Unknown clinical review status '{value}'.")
    };

    public static string SerializeApprovalStatus(ClinicalApprovalStatus value) => value switch
    {
        ClinicalApprovalStatus.Approved => "APPROVED",
        ClinicalApprovalStatus.PendingFormalReview => "PENDING_FORMAL_REVIEW",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static ClinicalApprovalStatus DeserializeApprovalStatus(string value) => value switch
    {
        "APPROVED" => ClinicalApprovalStatus.Approved,
        "PENDING_FORMAL_REVIEW" => ClinicalApprovalStatus.PendingFormalReview,
        _ => throw new InvalidOperationException($"Unknown clinical approval status '{value}'.")
    };
}
