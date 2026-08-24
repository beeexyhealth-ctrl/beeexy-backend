namespace Beeexy.Domain.Interoperability;

public enum FhirExportStatus
{
    Pending = 1,
    Generated = 2,
    ValidationFailed = 3,
    Validated = 4
}
