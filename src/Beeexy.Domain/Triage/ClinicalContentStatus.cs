namespace Beeexy.Domain.Triage;

public enum ClinicalContentSource
{
    LegacyUnspecified,
    ReferencePlatformDerived
}

public enum ClinicalReviewStatus
{
    Reviewed,
    Provisional
}

public enum ClinicalApprovalStatus
{
    Approved,
    PendingFormalReview
}

public sealed record ClinicalContentStatus(
    ClinicalContentSource Source,
    ClinicalReviewStatus ReviewStatus,
    ClinicalApprovalStatus ApprovalStatus)
{
    public static ClinicalContentStatus LegacyApproved { get; } = new(
        ClinicalContentSource.LegacyUnspecified,
        ClinicalReviewStatus.Reviewed,
        ClinicalApprovalStatus.Approved);

    public static ClinicalContentStatus ProvisionalReferencePlatformDerived { get; } = new(
        ClinicalContentSource.ReferencePlatformDerived,
        ClinicalReviewStatus.Provisional,
        ClinicalApprovalStatus.PendingFormalReview);
}
