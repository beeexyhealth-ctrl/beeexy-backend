using Beeexy.Domain.Ai;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal static class AiPersistence
{
    public static string StoreExecutionStatus(AiExecutionStatus value) => value switch
    {
        AiExecutionStatus.Pending => "pending",
        AiExecutionStatus.Running => "running",
        AiExecutionStatus.Succeeded => "succeeded",
        AiExecutionStatus.Failed => "failed",
        AiExecutionStatus.Rejected => "rejected",
        _ => throw new InvalidOperationException("Unsupported AI execution status.")
    };

    public static AiExecutionStatus LoadExecutionStatus(string value) => value switch
    {
        "pending" => AiExecutionStatus.Pending,
        "running" => AiExecutionStatus.Running,
        "succeeded" => AiExecutionStatus.Succeeded,
        "failed" => AiExecutionStatus.Failed,
        "rejected" => AiExecutionStatus.Rejected,
        _ => throw new InvalidOperationException("Unsupported persisted AI execution status.")
    };

    public static string StoreMessageRole(AiMessageRole value) => value switch
    {
        AiMessageRole.User => "user",
        AiMessageRole.Assistant => "assistant",
        _ => throw new InvalidOperationException("Unsupported AI message role.")
    };

    public static AiMessageRole LoadMessageRole(string value) => value switch
    {
        "user" => AiMessageRole.User,
        "assistant" => AiMessageRole.Assistant,
        _ => throw new InvalidOperationException("Unsupported persisted AI message role.")
    };

    public static string StoreAnalysisPurpose(AiAnalysisPurpose value) => value switch
    {
        AiAnalysisPurpose.Conversation => "conversation",
        AiAnalysisPurpose.SecondOpinion => "second_opinion",
        _ => throw new InvalidOperationException("Unsupported AI analysis purpose.")
    };

    public static AiAnalysisPurpose LoadAnalysisPurpose(string value) => value switch
    {
        "conversation" => AiAnalysisPurpose.Conversation,
        "second_opinion" => AiAnalysisPurpose.SecondOpinion,
        _ => throw new InvalidOperationException("Unsupported persisted AI analysis purpose.")
    };

    public static string StoreDocumentStatus(AiDocumentStatus value) => value switch
    {
        AiDocumentStatus.Active => "active",
        AiDocumentStatus.Deleted => "deleted",
        AiDocumentStatus.Expired => "expired",
        _ => throw new InvalidOperationException("Unsupported AI document status.")
    };

    public static AiDocumentStatus LoadDocumentStatus(string value) => value switch
    {
        "active" => AiDocumentStatus.Active,
        "deleted" => AiDocumentStatus.Deleted,
        "expired" => AiDocumentStatus.Expired,
        _ => throw new InvalidOperationException("Unsupported persisted AI document status.")
    };

    public static string StoreSafetyCategory(AiSafetyCategory value) => value switch
    {
        AiSafetyCategory.Approved => "approved",
        AiSafetyCategory.UnsafeMedicalAdvice => "unsafe_medical_advice",
        AiSafetyCategory.Diagnosis => "diagnosis",
        AiSafetyCategory.Prescription => "prescription",
        AiSafetyCategory.Unsupported => "unsupported",
        AiSafetyCategory.Malformed => "malformed",
        _ => throw new InvalidOperationException("Unsupported AI safety category.")
    };

    public static AiSafetyCategory LoadSafetyCategory(string value) => value switch
    {
        "approved" => AiSafetyCategory.Approved,
        "unsafe_medical_advice" => AiSafetyCategory.UnsafeMedicalAdvice,
        "diagnosis" => AiSafetyCategory.Diagnosis,
        "prescription" => AiSafetyCategory.Prescription,
        "unsupported" => AiSafetyCategory.Unsupported,
        "malformed" => AiSafetyCategory.Malformed,
        _ => throw new InvalidOperationException("Unsupported persisted AI safety category.")
    };
}
