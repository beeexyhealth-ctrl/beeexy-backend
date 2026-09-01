namespace Beeexy.Domain.Ai;

public enum AiExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Rejected
}

public enum AiMessageRole
{
    User,
    Assistant
}

public enum AiAnalysisPurpose
{
    Conversation,
    SecondOpinion
}

public enum AiDocumentStatus
{
    Active,
    Deleted,
    Expired
}

public enum AiSafetyCategory
{
    Approved,
    UnsafeMedicalAdvice,
    Diagnosis,
    Prescription,
    Unsupported,
    Malformed
}
