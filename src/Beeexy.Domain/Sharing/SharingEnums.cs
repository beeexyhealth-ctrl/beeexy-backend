namespace Beeexy.Domain.Sharing;

public enum ShareScope
{
    FullProfile,
    Case,
    PreTriage,
    Visit,
    SpecificRecords
}

public enum ShareGrantStatus
{
    Active,
    Revoked,
    Expired
}

public enum ShareAccessEventType
{
    ShareCreated,
    ShareAccessed,
    ShareDownloaded,
    ShareRevoked,
    ShareExpired
}

public enum ShareAccessOutcome
{
    Succeeded,
    Denied
}

public enum ExportArtifactFormat
{
    BeeexyJson,
    Pdf,
    FhirJson
}

public enum ExportArtifactStatus
{
    Pending,
    Available,
    Failed,
    Deleted
}
