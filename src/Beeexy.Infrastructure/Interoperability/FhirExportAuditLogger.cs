using Beeexy.Application.Interoperability;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FhirExportAuditLogger(
    ILogger<FhirExportAuditLogger> logger) : IFhirExportAuditLogger
{
    public void Created(
        EntityId actorAccountId,
        EntityId patientProfileId,
        EntityId fhirExportId,
        PatientAccessReason accessReason,
        DateTimeOffset occurredAt) =>
        logger.LogInformation(
            "FHIR export {FhirExportId} created for patient {PatientProfileId} by account " +
            "{ActorAccountId} via {AccessReason} at {OccurredAt}.",
            fhirExportId.Value,
            patientProfileId.Value,
            actorAccountId.Value,
            accessReason,
            occurredAt);

    public void ValidationCompleted(
        EntityId patientProfileId,
        EntityId fhirExportId,
        FhirExportStatus status,
        DateTimeOffset occurredAt) =>
        logger.LogInformation(
            "FHIR export {FhirExportId} validation completed for patient {PatientProfileId} " +
            "with status {Status} at {OccurredAt}.",
            fhirExportId.Value,
            patientProfileId.Value,
            status,
            occurredAt);

    public void Downloaded(
        EntityId actorAccountId,
        EntityId patientProfileId,
        EntityId fhirExportId,
        PatientAccessReason accessReason,
        DateTimeOffset occurredAt) =>
        logger.LogInformation(
            "Validated FHIR export {FhirExportId} downloaded for patient {PatientProfileId} " +
            "by account {ActorAccountId} via {AccessReason} at {OccurredAt}.",
            fhirExportId.Value,
            patientProfileId.Value,
            actorAccountId.Value,
            accessReason,
            occurredAt);

    public void IntegrityRejected(
        EntityId actorAccountId,
        EntityId patientProfileId,
        EntityId fhirExportId,
        PatientAccessReason accessReason,
        DateTimeOffset occurredAt) =>
        logger.LogWarning(
            "FHIR export {FhirExportId} download rejected by integrity verification for " +
            "patient {PatientProfileId}, account {ActorAccountId}, via {AccessReason} at " +
            "{OccurredAt}.",
            fhirExportId.Value,
            patientProfileId.Value,
            actorAccountId.Value,
            accessReason,
            occurredAt);
}
