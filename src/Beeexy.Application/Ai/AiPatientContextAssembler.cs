using System.Text.Json;
using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Application.Ai;

public sealed class AiPatientContextAssembler(
    AuthorizePatientAccess authorizePatientAccess,
    IPatientProfileReadRepository patientProfiles,
    IClinicalHistoryReadRepository clinicalHistory,
    IClinicalHistoryEventReadRepository clinicalHistoryEvents,
    IClock clock) : IAiPatientContextAssembler
{
    private const int MaximumHistoryEvents = 3;

    public async Task<AiPatientContext> AssembleAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await authorizePatientAccess.ExecuteAsync(
            patientProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PatientProfileNotFoundException();
        }

        var profile = await patientProfiles.FindAsync(patientProfileId, cancellationToken);
        if (profile is null)
        {
            throw new PatientProfileNotFoundException();
        }

        var recentHistory = await clinicalHistory.ListAsync(
            patientProfileId,
            eventType: null,
            after: null,
            MaximumHistoryEvents,
            cancellationToken);
        var historyItems = new List<object>(recentHistory.Count);
        var sources = new List<AiPatientContextSource>
        {
            new("patient-profile", patientProfileId, null)
        };
        foreach (var item in recentHistory)
        {
            var detail = await clinicalHistoryEvents.GetAsync(
                patientProfileId,
                item.EventId,
                cancellationToken);
            if (detail is null)
            {
                continue;
            }

            sources.Add(new AiPatientContextSource(
                "clinical-history-event",
                item.EventId,
                item.OccurredAt));
            sources.Add(new AiPatientContextSource(
                "pre-triage-episode",
                item.SourceId,
                item.OccurredAt));
            historyItems.Add(new
            {
                type = item.EventType == ClinicalHistoryEventType.CompletedPreTriage
                    ? "completed-pre-triage"
                    : "clinical-history-event",
                occurredAt = item.OccurredAt,
                preTriage = detail.PreTriageSummary is null
                    ? null
                    : new
                    {
                        primarySymptom = new
                        {
                            detail.PreTriageSummary.PrimarySymptom.Code,
                            detail.PreTriageSummary.PrimarySymptom.Display
                        },
                        duration = new
                        {
                            detail.PreTriageSummary.Duration.Value,
                            detail.PreTriageSummary.Duration.Unit
                        },
                        detail.PreTriageSummary.Intensity,
                        detail.PreTriageSummary.AdditionalSymptoms
                    }
            });
        }

        var context = new
        {
            demographics = new
            {
                age = CalculateAge(profile.DateOfBirth, clock.UtcNow),
                sexAssignedAtBirth = profile.SexAssignedAtBirth?.ToString()
            },
            clinicalHistory = historyItems
        };
        return new AiPatientContext(
            JsonSerializer.Serialize(context),
            sources);
    }

    private static int? CalculateAge(DateOnly? dateOfBirth, DateTimeOffset now)
    {
        if (!dateOfBirth.HasValue)
        {
            return null;
        }

        var current = DateOnly.FromDateTime(now.UtcDateTime);
        var age = current.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value > current.AddYears(-age))
        {
            age--;
        }

        return Math.Max(age, 0);
    }
}
