using Beeexy.Domain.Common;

namespace Beeexy.Domain.Care;

public sealed class SymptomWarningSign
{
    private SymptomWarningSign()
    {
        Code = null!;
        DisplayText = null!;
    }

    private SymptomWarningSign(
        EntityId id,
        EntityId packageVersionId,
        SymptomDiaryCode code,
        string displayText,
        int sourceOrder)
    {
        Id = id;
        PackageVersionId = packageVersionId;
        Code = code;
        DisplayText = displayText;
        SourceOrder = sourceOrder;
    }

    public EntityId Id { get; private set; }
    public EntityId PackageVersionId { get; private set; }
    public SymptomDiaryCode Code { get; private set; }
    public string DisplayText { get; private set; }
    public int SourceOrder { get; private set; }

    internal static SymptomWarningSign Create(
        EntityId packageVersionId,
        SymptomDiaryCode code,
        string displayText,
        int sourceOrder,
        EntityId? id = null)
    {
        SymptomDiaryGuard.EnsureId(packageVersionId, nameof(packageVersionId));
        ArgumentNullException.ThrowIfNull(code);
        SymptomDiaryGuard.EnsurePositiveOrder(sourceOrder, nameof(sourceOrder));
        var entityId = id ?? EntityId.New();
        SymptomDiaryGuard.EnsureId(entityId, nameof(id));
        return new SymptomWarningSign(
            entityId,
            packageVersionId,
            code,
            SymptomDiaryGuard.RequiredText(
                displayText,
                SymptomDiaryGuard.MaximumTextLength,
                nameof(displayText)),
            sourceOrder);
    }
}

public sealed record SymptomWarningSignInput(
    SymptomDiaryCode Code,
    string DisplayText,
    int SourceOrder,
    EntityId? Id = null);
