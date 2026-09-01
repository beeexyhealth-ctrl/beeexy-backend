using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiMessage
{
    private AiMessage()
    {
        Content = null!;
    }

    private AiMessage(
        EntityId id,
        EntityId conversationId,
        AiMessageRole role,
        string content,
        int sequence,
        DateTimeOffset createdAt)
    {
        Id = id;
        ConversationId = conversationId;
        Role = role;
        Content = content;
        Sequence = sequence;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId ConversationId { get; private set; }

    public AiMessageRole Role { get; private set; }

    public string Content { get; private set; }

    public int Sequence { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AiMessage Create(
        EntityId conversationId,
        AiMessageRole role,
        string content,
        int sequence,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        AiGuard.EnsureId(conversationId, nameof(conversationId));
        AiGuard.EnsureDefined(role, nameof(role));
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "Message sequence must be positive.");
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiMessage(
            AiGuard.IdOrNew(id, nameof(id)),
            conversationId,
            role,
            AiGuard.RequiredContent(content, nameof(content)),
            sequence,
            createdAt);
    }
}
