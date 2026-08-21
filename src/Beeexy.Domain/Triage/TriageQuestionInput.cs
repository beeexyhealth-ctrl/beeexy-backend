using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed record TriageQuestionInput(
    QuestionCode Code,
    string PromptText,
    int DisplayOrder,
    string? AnswerSchemaJson = null,
    string? BranchingMetadataJson = null,
    EntityId? Id = null);
