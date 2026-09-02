using System.Text;
using Beeexy.Application.Ai;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Beeexy.Infrastructure.Ai;

internal sealed class BaselineAiDocumentSafetyScanner : IAiDocumentSafetyScanner
{
    private static readonly byte[] EicarMarker = Encoding.ASCII.GetBytes(
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!");

    private static readonly byte[][] UnsafePdfMarkers =
    [
        "/JavaScript"u8.ToArray(),
        "/JS"u8.ToArray(),
        "/Launch"u8.ToArray(),
        "/EmbeddedFile"u8.ToArray(),
        "/OpenAction"u8.ToArray()
    ];

    public Task<AiFileSafetyResult> ScanAsync(
        ReadOnlyMemory<byte> content,
        string normalizedContentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = content.Span;
        var unsafePdf = false;
        if (string.Equals(normalizedContentType, "application/pdf", StringComparison.Ordinal))
        {
            foreach (var marker in UnsafePdfMarkers)
            {
                if (bytes.IndexOf(marker) >= 0)
                {
                    unsafePdf = true;
                    break;
                }
            }
        }

        if (bytes.IndexOf(EicarMarker) >= 0 || unsafePdf)
        {
            return Task.FromResult(new AiFileSafetyResult(AiFileSafetyStatus.Unsafe));
        }

        return Task.FromResult(new AiFileSafetyResult(AiFileSafetyStatus.Safe));
    }
}

internal sealed class PdfTxtAiDocumentTextExtractor : IAiDocumentTextExtractor
{
    private const int MaximumValidatedTextCharacters = 1_000_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<AiDocumentExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> content,
        string normalizedContentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(normalizedContentType switch
        {
            "text/plain" => ExtractText(content.Span),
            "application/pdf" => ExtractPdf(content.ToArray(), cancellationToken),
            _ => new AiDocumentExtractionResult(AiDocumentExtractionStatus.Unsupported)
        });
    }

    private static AiDocumentExtractionResult ExtractText(ReadOnlySpan<byte> content)
    {
        try
        {
            var text = StrictUtf8.GetString(content);
            if (text.Any(character => char.IsControl(character) &&
                    character is not '\r' and not '\n' and not '\t'))
            {
                return new AiDocumentExtractionResult(AiDocumentExtractionStatus.Malformed);
            }

            return Useful(text)
                ? new AiDocumentExtractionResult(AiDocumentExtractionStatus.Success, text)
                : new AiDocumentExtractionResult(AiDocumentExtractionStatus.NoUsefulText);
        }
        catch (DecoderFallbackException)
        {
            return new AiDocumentExtractionResult(AiDocumentExtractionStatus.Malformed);
        }
    }

    private static AiDocumentExtractionResult ExtractPdf(
        byte[] content,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            if (document.NumberOfPages <= 0)
            {
                return new AiDocumentExtractionResult(AiDocumentExtractionStatus.NoUsefulText);
            }

            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = MaximumValidatedTextCharacters - builder.Length;
                if (remaining <= 0)
                {
                    break;
                }

                var pageText = ContentOrderTextExtractor.GetText(page);
                builder.Append(pageText.AsSpan(0, Math.Min(pageText.Length, remaining)));
                builder.AppendLine();
            }

            var text = builder.ToString();
            return Useful(text)
                ? new AiDocumentExtractionResult(AiDocumentExtractionStatus.Success, text)
                : new AiDocumentExtractionResult(AiDocumentExtractionStatus.NoUsefulText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AiDocumentExtractionResult(AiDocumentExtractionStatus.Malformed);
        }
    }

    private static bool Useful(string text) =>
        text.Count(char.IsLetterOrDigit) >= 3;
}
