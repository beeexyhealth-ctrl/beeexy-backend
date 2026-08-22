namespace Beeexy.Domain.Triage;

public enum ClinicalContentSource
{
    LegacyUnspecified,
    ReferencePlatformDerived,
    ProductDemoDefined
}

public enum ClinicalReviewStatus
{
    Reviewed,
    Provisional,
    NotApplicable
}

public enum ClinicalApprovalStatus
{
    Approved,
    PendingFormalReview,
    NotClinicallyApproved
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

    public static ClinicalContentStatus NonClinicalDemo { get; } = new(
        ClinicalContentSource.ProductDemoDefined,
        ClinicalReviewStatus.NotApplicable,
        ClinicalApprovalStatus.NotClinicallyApproved);
}
