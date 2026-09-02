using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class RequestSecondOpinion(
    ICurrentSessionIdentity currentIdentity,
    ISecondOpinionInputAssembler inputAssembler,
    ISecondOpinionRepository repository,
    ExecuteSafeAiAnalysis safeExecution,
    IClock clock)
{
    public async Task<SecondOpinionRequestReceipt> ExecuteAsync(
        RequestSecondOpinionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = currentIdentity.GetRequired();
        var prepared = await inputAssembler.AssembleAsync(
            command,
            current.AccountId,
            cancellationToken);
        var request = AiAnalysisRequest.Create(
            current.AccountId,
            AiAnalysisPurpose.SecondOpinion,
            "ai-second-opinion-input@v1",
            prepared.ImmutableInputJson,
            clock.UtcNow,
            command.PatientProfileId);
        prepared.Document?.AssociateWithAnalysis(request.Id);
        repository.Add(request);
        await repository.SaveChangesAsync(cancellationToken);

        var outcome = await safeExecution.ExecuteAsync(
            new ExecuteSafeAiAnalysisCommand(
                new ExecuteAiAnalysisCommand(
                    request.Id,
                    AiWorkloadIdentifiers.SecondOpinion,
                    SecondOpinionContract.Prompt,
                    prepared.ProviderInputJson,
                    SecondOpinionContract.Result,
                    command.CorrelationIdentifier),
                SecondOpinionProductContent.Disclaimer,
                SecondOpinionProductContent.DisclaimerVersion),
            cancellationToken);
        return new SecondOpinionRequestReceipt(
            request.Id,
            outcome.ExecutionId,
            MapStatus(outcome));
    }

    private static SecondOpinionStatus MapStatus(AiSafeAnalysisOutcome outcome) =>
        outcome.TechnicalOutcome switch
        {
            AiExecutionOutcomeKind.StructurallyValid when outcome.ProviderOutputDisplayEligible =>
                SecondOpinionStatus.Succeeded,
            AiExecutionOutcomeKind.StructurallyValid or AiExecutionOutcomeKind.MalformedResult =>
                SecondOpinionStatus.Rejected,
            _ => SecondOpinionStatus.Failed
        };
}

public sealed class GetSecondOpinion(
    ICurrentSessionIdentity currentIdentity,
    AuthorizePatientAccess authorizePatientAccess,
    ISecondOpinionRepository repository,
    AiSafetyProductContent safetyContent)
{
    public async Task<SecondOpinionDetail> ExecuteAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default)
    {
        var current = currentIdentity.GetRequired();
        var analysis = await repository.FindOwnedAsync(
            analysisId,
            current.AccountId,
            cancellationToken);
        if (analysis is null)
        {
            throw new SecondOpinionNotFoundException();
        }

        var patientId = analysis.PatientProfileId;
        var authorization = await authorizePatientAccess.ExecuteAsync(patientId, cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new SecondOpinionNotFoundException();
        }

        var state = await repository.GetStateAsync(analysis.AnalysisId, cancellationToken);
        var status = MapStatus(state);
        if (status != SecondOpinionStatus.Succeeded ||
            string.IsNullOrWhiteSpace(state.ResultContentJson) ||
            !state.ResultCreatedAt.HasValue)
        {
            return new SecondOpinionDetail(
                analysis.AnalysisId,
                patientId,
                state.ExecutionId,
                status,
                null,
                null,
                status == SecondOpinionStatus.Rejected
                    ? SafeFallback(state.ProductContentVersion)
                    : null);
        }

        var result = ParseResult(state.ResultContentJson);
        return new SecondOpinionDetail(
            analysis.AnalysisId,
            patientId,
            state.ExecutionId,
            status,
            result,
            new SecondOpinionMetadata(
                true,
                state.ResultCreatedAt.Value,
                SecondOpinionProductContent.ResultVersion,
                state.ProviderIdentifier,
                state.ModelIdentifier,
                state.PromptVersion,
                SecondOpinionProductContent.DisclaimerVersion),
            null);
    }

    private string SafeFallback(string? version) =>
        string.Equals(
            version,
            safetyContent.CriticalFallbackVersion,
            StringComparison.Ordinal)
            ? safetyContent.CriticalFallback
            : safetyContent.GenericFallback;

    private SecondOpinionResult ParseResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new SecondOpinionResult(
            root.GetProperty("summary").GetString()!,
            ReadArray(root, "importantPoints"),
            ReadArray(root, "possibleQuestionsForDoctor"),
            ReadArray(root, "missingInformation"),
            SecondOpinionProductContent.Disclaimer);
    }

    private static IReadOnlyList<string> ReadArray(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static SecondOpinionStatus MapStatus(SecondOpinionStoredState state)
    {
        if (state.ExecutionStatus is null or AiExecutionStatus.Pending)
        {
            return SecondOpinionStatus.Pending;
        }

        if (state.ExecutionStatus == AiExecutionStatus.Running)
        {
            return SecondOpinionStatus.Running;
        }

        if (state.ExecutionStatus == AiExecutionStatus.Failed)
        {
            return SecondOpinionStatus.Failed;
        }

        if (state.ExecutionStatus == AiExecutionStatus.Rejected ||
            state.SafetyCategory is { } safety && safety != AiSafetyCategory.Approved ||
            state.DisplayEligible == false)
        {
            return SecondOpinionStatus.Rejected;
        }

        return state.ResultContentJson is not null && state.DisplayEligible == true
            ? SecondOpinionStatus.Succeeded
            : SecondOpinionStatus.Rejected;
    }
}
