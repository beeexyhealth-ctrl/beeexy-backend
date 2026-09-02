using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class SecondOpinionInputAssembler(
    AuthorizePatientAccess authorizePatientAccess,
    IPatientProfileReadRepository patientProfiles,
    IClinicalHistoryEventReadRepository clinicalHistoryEvents,
    IPreTriageCompletionRepository preTriageRepository,
    GetPreTriageResult getPreTriageResult,
    IAiDocumentRepository documents,
    IAiDocumentBlobStore blobStore,
    IAiDocumentTextExtractor textExtractor,
    IClock clock) : ISecondOpinionInputAssembler
{
    public async Task<SecondOpinionPreparedInput> AssembleAsync(
        RequestSecondOpinionCommand command,
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            command.PatientProfileId,
            cancellationToken);
        var profile = authorization.IsAuthorized
            ? await patientProfiles.FindAsync(command.PatientProfileId, cancellationToken)
            : null;
        if (profile is null)
        {
            throw new PatientProfileNotFoundException();
        }

        var text = NormalizeText(command.Text);
        var documentIds = NormalizeIds(command.DocumentIds, "document");
        var historyIds = NormalizeIds(command.ClinicalHistoryEventIds, "Clinical History event");
        if (documentIds.Count > 1)
        {
            throw Invalid(
                "ai.second_opinion.document_limit",
                "A Second Opinion supports at most one temporary document.");
        }

        if (historyIds.Count > SecondOpinionOptions.MaximumClinicalHistoryEvents)
        {
            throw Invalid(
                "ai.second_opinion.history_limit",
                $"A Second Opinion supports at most {SecondOpinionOptions.MaximumClinicalHistoryEvents} Clinical History events.");
        }

        if (text is null && documentIds.Count == 0 &&
            command.PreTriageSessionId is null && historyIds.Count == 0)
        {
            throw Invalid(
                "ai.second_opinion.input_required",
                "Provide text, one temporary document, Pre-Triage, or Clinical History.");
        }

        AiUploadedDocument? selectedDocument = null;
        object? documentInput = null;
        if (documentIds.Count == 1)
        {
            selectedDocument = await documents.FindOwnedAsync(
                documentIds[0],
                accountId,
                cancellationToken) ?? throw new SecondOpinionNotFoundException();
            if (selectedDocument.PatientProfileId is { } documentPatientId &&
                documentPatientId != command.PatientProfileId)
            {
                throw new SecondOpinionNotFoundException();
            }

            if (selectedDocument.Status != AiDocumentStatus.Active ||
                selectedDocument.ExpiresAt <= clock.UtcNow ||
                selectedDocument.AnalysisRequestId.HasValue)
            {
                throw Invalid(
                    "ai.second_opinion.document_unavailable",
                    "The temporary document is no longer available for analysis.");
            }

            var extractedText = await ReadDocumentTextAsync(selectedDocument, cancellationToken);
            documentInput = new
            {
                selectedDocument.ContentType,
                text = extractedText
            };
        }

        object? preTriageInput = null;
        if (command.PreTriageSessionId is { } sessionId)
        {
            var graph = await preTriageRepository.GetAsync(sessionId, cancellationToken)
                ?? throw new SecondOpinionNotFoundException();
            var sourcePatientId = graph.Session.PatientProfileId ??
                (graph.Episode?.IsClaimed == true ? graph.Episode.PatientProfileId : null);
            if (sourcePatientId != command.PatientProfileId)
            {
                throw new SecondOpinionNotFoundException();
            }

            var result = await getPreTriageResult.ExecuteAsync(
                new GetPreTriageResultQuery(
                    sessionId,
                    PreTriageCallerMode.Authenticated,
                    AnonymousCapability: null),
                cancellationToken);
            preTriageInput = new
            {
                primarySymptom = new
                {
                    code = result.PrimarySymptom.Value,
                    display = result.PrimarySymptomDisplay
                },
                duration = new { value = result.DurationValue, unit = result.DurationUnit },
                result.Intensity,
                result.AdditionalSymptoms,
                result.CompletedAt,
                questionnaire = new
                {
                    code = result.QuestionnaireCode.Value,
                    version = result.QuestionnaireVersion.Value
                },
                package = new
                {
                    code = result.PackageCode.Value,
                    version = result.PackageVersion.Value
                }
            };
        }

        var historyInput = new List<object>(historyIds.Count);
        foreach (var historyId in historyIds)
        {
            var detail = await clinicalHistoryEvents.GetAsync(
                command.PatientProfileId,
                historyId,
                cancellationToken) ?? throw new SecondOpinionNotFoundException();
            historyInput.Add(new
            {
                eventType = detail.Event.EventType.ToString(),
                detail.Event.OccurredAt,
                source = new
                {
                    type = detail.AuthoritativeSource.SourceType.ToString(),
                    completedAt = detail.AuthoritativeSource.CompletedAt,
                    questionnaireVersionId = detail.AuthoritativeSource.QuestionnaireVersionId.Value,
                    clinicalRuleSetVersionId = detail.AuthoritativeSource.ClinicalRuleSetVersionId.Value
                },
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
                    },
                amendments = detail.Amendments.Select(amendment => new
                {
                    amendment.Reason,
                    amendment.CreatedAt
                })
            });
        }

        var input = new
        {
            demographics = new
            {
                age = CalculateAge(profile.DateOfBirth, clock.UtcNow),
                sexAssignedAtBirth = profile.SexAssignedAtBirth?.ToString()
            },
            typedText = text,
            document = documentInput,
            preTriage = preTriageInput,
            clinicalHistory = historyInput
        };
        var providerJson = JsonSerializer.Serialize(input);
        var immutableJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "v1",
            input,
            provenance = new
            {
                patientId = command.PatientProfileId.Value,
                documentId = selectedDocument?.Id.Value,
                preTriageSessionId = command.PreTriageSessionId?.Value,
                clinicalHistoryEventIds = historyIds.Select(id => id.Value)
            }
        });
        return new SecondOpinionPreparedInput(providerJson, immutableJson, selectedDocument);
    }

    private async Task<string> ReadDocumentTextAsync(
        AiUploadedDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await blobStore.ReadPrivateAsync(
                AiBlobKey.Parse(document.StorageKey),
                cancellationToken);
            var extraction = await textExtractor.ExtractAsync(
                bytes,
                document.ContentType,
                cancellationToken);
            if (extraction.Status != AiDocumentExtractionStatus.Success ||
                string.IsNullOrWhiteSpace(extraction.ExtractedText) ||
                extraction.ExtractedText.Length > SecondOpinionOptions.MaximumDocumentTextCharacters)
            {
                throw Invalid(
                    "ai.second_opinion.document_text_unavailable",
                    "The temporary document does not contain usable bounded text.");
            }

            return extraction.ExtractedText.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestValidationException)
        {
            throw;
        }
        catch
        {
            throw Invalid(
                "ai.second_opinion.document_text_unavailable",
                "The temporary document text is unavailable.");
        }
    }

    private static string? NormalizeText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0 ||
            normalized.Length > SecondOpinionOptions.MaximumTypedTextCharacters ||
            !normalized.Any(char.IsLetterOrDigit))
        {
            throw Invalid(
                "ai.second_opinion.text_invalid",
                $"Text must contain meaningful content and not exceed {SecondOpinionOptions.MaximumTypedTextCharacters} characters.");
        }

        return normalized;
    }

    private static IReadOnlyList<EntityId> NormalizeIds(
        IReadOnlyList<EntityId>? values,
        string name)
    {
        var normalized = values?.ToArray() ?? [];
        if (normalized.Any(value => value.Value == Guid.Empty) ||
            normalized.Distinct().Count() != normalized.Length)
        {
            throw Invalid(
                "ai.second_opinion.source_ids_invalid",
                $"Each selected {name} identifier must be non-empty and unique.");
        }

        return normalized;
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

    private static RequestValidationException Invalid(string code, string message) =>
        new(code, message);
}
