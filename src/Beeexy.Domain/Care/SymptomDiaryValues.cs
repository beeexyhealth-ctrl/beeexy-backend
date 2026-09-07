using System.Text.Json;

namespace Beeexy.Domain.Care;

public sealed record SymptomDiaryCode
{
    public const int MaximumLength = 100;

    private SymptomDiaryCode(string value) => Value = value;

    public string Value { get; }

    public static SymptomDiaryCode Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var candidate = value.Trim();
        if (candidate.Length > MaximumLength || candidate.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The symptom-diary code is invalid.", nameof(value));
        }

        return new SymptomDiaryCode(candidate);
    }

    public override string ToString() => Value;
}

public sealed record SymptomDiarySha256
{
    public const int Length = 64;

    private SymptomDiarySha256(string value) => Value = value;

    public string Value { get; }

    public static SymptomDiarySha256 FromHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != Length || value.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new ArgumentException(
                "The SHA-256 hash must be 64 lowercase hexadecimal characters.",
                nameof(value));
        }

        return new SymptomDiarySha256(value);
    }

    public override string ToString() => Value;
}

internal static class SymptomDiaryGuard
{
    public const int MaximumTextLength = 4000;
    public const int MaximumBodyLength = 65536;
    public const int MaximumJsonLength = 65536;
    public const int MaximumReferenceLength = 500;

    public static string RequiredText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return value;
    }

    public static string? OptionalText(string? value, int maximumLength, string parameterName)
        => value is null ? null : RequiredText(value, maximumLength, parameterName);

    public static string RequiredJson(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumJsonLength)
        {
            throw new ArgumentException(
                $"JSON content cannot exceed {MaximumJsonLength} characters.",
                parameterName);
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The value must contain valid JSON.", parameterName, exception);
        }

        return value;
    }

    public static string RequiredJsonObject(string value, string parameterName)
    {
        var json = RequiredJson(value, parameterName);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The value must contain a JSON object.", parameterName);
        }

        return json;
    }

    public static void EnsureId(Beeexy.Domain.Common.EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier is required.", parameterName);
        }
    }

    public static void EnsurePositiveOrder(int sourceOrder, string parameterName)
    {
        if (sourceOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Source order must be positive.");
        }
    }
}
