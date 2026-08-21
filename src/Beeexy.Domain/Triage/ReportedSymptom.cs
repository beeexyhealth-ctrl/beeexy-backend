using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class ReportedSymptom
{
    public const int MaximumTerminologySystemLength = 200;
    public const int MaximumTerminologyCodeLength = 100;
    public const int MaximumTerminologyDisplayLength = 500;
    public const int MaximumNormalizationSourceLength = 200;

    private ReportedSymptom()
    {
        OriginalText = null!;
    }

    private ReportedSymptom(
        EntityId id,
        EntityId sessionId,
        SymptomText originalText,
        int sequence,
        string? terminologySystem,
        string? terminologyCode,
        string? terminologyDisplay,
        string? normalizationSource,
        DateTimeOffset? normalizedAt,
        DateTimeOffset reportedAt)
    {
        Id = id;
        SessionId = sessionId;
        OriginalText = originalText;
        Sequence = sequence;
        TerminologySystem = terminologySystem;
        TerminologyCode = terminologyCode;
        TerminologyDisplay = terminologyDisplay;
        NormalizationSource = normalizationSource;
        NormalizedAt = normalizedAt;
        ReportedAt = reportedAt;
    }

    public EntityId Id { get; private set; }

    public EntityId? SessionId { get; private set; }

    public EntityId? EpisodeId { get; private set; }

    public SymptomText OriginalText { get; private set; }

    public int Sequence { get; private set; }

    public string? TerminologySystem { get; private set; }

    public string? TerminologyCode { get; private set; }

    public string? TerminologyDisplay { get; private set; }

    public string? NormalizationSource { get; private set; }

    public DateTimeOffset? NormalizedAt { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }

    internal static ReportedSymptom CreateForSession(
        EntityId sessionId,
        SymptomText originalText,
        int sequence,
        DateTimeOffset reportedAt,
        string? terminologySystem,
        string? terminologyCode,
        string? terminologyDisplay,
        string? normalizationSource,
        DateTimeOffset? normalizedAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        InstantGuard.EnsureUtc(reportedAt, nameof(reportedAt));
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Symptom sequence must be positive.");
        }

        var hasNormalization = terminologySystem is not null ||
            terminologyCode is not null ||
            terminologyDisplay is not null ||
            normalizationSource is not null ||
            normalizedAt.HasValue;
        if (hasNormalization &&
            (terminologySystem is null || terminologyCode is null ||
             normalizationSource is null || !normalizedAt.HasValue))
        {
            throw new ArgumentException(
                "Normalized symptoms require a terminology system, code, source, and timestamp.");
        }

        if (normalizedAt.HasValue)
        {
            InstantGuard.EnsureUtc(normalizedAt.Value, nameof(normalizedAt));
            if (normalizedAt < reportedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(normalizedAt),
                    "Normalization cannot precede symptom reporting.");
            }
        }

        return new ReportedSymptom(
            id ?? EntityId.New(),
            sessionId,
            originalText,
            sequence,
            TriageValueGuard.OptionalText(
                terminologySystem,
                MaximumTerminologySystemLength,
                nameof(terminologySystem)),
            TriageValueGuard.OptionalText(
                terminologyCode,
                MaximumTerminologyCodeLength,
                nameof(terminologyCode)),
            TriageValueGuard.OptionalText(
                terminologyDisplay,
                MaximumTerminologyDisplayLength,
                nameof(terminologyDisplay)),
            TriageValueGuard.OptionalText(
                normalizationSource,
                MaximumNormalizationSourceLength,
                nameof(normalizationSource)),
            normalizedAt,
            reportedAt);
    }

    internal void PromoteToEpisode(EntityId episodeId)
    {
        if (SessionId is null || EpisodeId is not null)
        {
            throw new InvalidOperationException("Only a temporary symptom can be promoted.");
        }

        SessionId = null;
        EpisodeId = episodeId;
    }
}
