using Beeexy.Domain.Sharing;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal static class SharingPersistence
{
    public static string StoreScope(ShareScope value) => value switch
    {
        ShareScope.FullProfile => "full_profile",
        ShareScope.Case => "case",
        ShareScope.PreTriage => "pre_triage",
        ShareScope.Visit => "visit",
        ShareScope.SpecificRecords => "specific_records",
        _ => throw new InvalidOperationException("Unsupported share scope.")
    };

    public static ShareScope LoadScope(string value) => value switch
    {
        "full_profile" => ShareScope.FullProfile,
        "case" => ShareScope.Case,
        "pre_triage" => ShareScope.PreTriage,
        "visit" => ShareScope.Visit,
        "specific_records" => ShareScope.SpecificRecords,
        _ => throw new InvalidOperationException("Unsupported persisted share scope.")
    };

    public static string StoreEventType(ShareAccessEventType value) => value switch
    {
        ShareAccessEventType.ShareCreated => "share_created",
        ShareAccessEventType.ShareAccessed => "share_accessed",
        ShareAccessEventType.ShareDownloaded => "share_downloaded",
        ShareAccessEventType.ShareRevoked => "share_revoked",
        ShareAccessEventType.ShareExpired => "share_expired",
        _ => throw new InvalidOperationException("Unsupported share access event type.")
    };

    public static ShareAccessEventType LoadEventType(string value) => value switch
    {
        "share_created" => ShareAccessEventType.ShareCreated,
        "share_accessed" => ShareAccessEventType.ShareAccessed,
        "share_downloaded" => ShareAccessEventType.ShareDownloaded,
        "share_revoked" => ShareAccessEventType.ShareRevoked,
        "share_expired" => ShareAccessEventType.ShareExpired,
        _ => throw new InvalidOperationException("Unsupported persisted share access event type.")
    };

    public static string StoreEventOutcome(ShareAccessOutcome value) => value switch
    {
        ShareAccessOutcome.Succeeded => "succeeded",
        ShareAccessOutcome.Denied => "denied",
        _ => throw new InvalidOperationException("Unsupported share access outcome.")
    };

    public static ShareAccessOutcome LoadEventOutcome(string value) => value switch
    {
        "succeeded" => ShareAccessOutcome.Succeeded,
        "denied" => ShareAccessOutcome.Denied,
        _ => throw new InvalidOperationException("Unsupported persisted share access outcome.")
    };

    public static string StoreExportFormat(ExportArtifactFormat value) => value switch
    {
        ExportArtifactFormat.BeeexyJson => "beeexy_json",
        ExportArtifactFormat.Pdf => "pdf",
        ExportArtifactFormat.FhirJson => "fhir_json",
        _ => throw new InvalidOperationException("Unsupported export artifact format.")
    };

    public static ExportArtifactFormat LoadExportFormat(string value) => value switch
    {
        "beeexy_json" => ExportArtifactFormat.BeeexyJson,
        "pdf" => ExportArtifactFormat.Pdf,
        "fhir_json" => ExportArtifactFormat.FhirJson,
        _ => throw new InvalidOperationException("Unsupported persisted export artifact format.")
    };

    public static string StoreExportStatus(ExportArtifactStatus value) => value switch
    {
        ExportArtifactStatus.Pending => "pending",
        ExportArtifactStatus.Available => "available",
        ExportArtifactStatus.Failed => "failed",
        ExportArtifactStatus.Deleted => "deleted",
        _ => throw new InvalidOperationException("Unsupported export artifact status.")
    };

    public static ExportArtifactStatus LoadExportStatus(string value) => value switch
    {
        "pending" => ExportArtifactStatus.Pending,
        "available" => ExportArtifactStatus.Available,
        "failed" => ExportArtifactStatus.Failed,
        "deleted" => ExportArtifactStatus.Deleted,
        _ => throw new InvalidOperationException("Unsupported persisted export artifact status.")
    };
}
