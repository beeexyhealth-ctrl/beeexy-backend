using System.Text;
using Beeexy.Application.Ai;
using Beeexy.Infrastructure.Ai;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase105")]
[Trait("Category", "Phase108")]
public sealed class AiDocumentInfrastructureTests
{
    private readonly PdfTxtAiDocumentTextExtractor extractor = new();
    private readonly BaselineAiDocumentSafetyScanner scanner = new();

    [Fact]
    public async Task TextNativePdf_IsExtractedWithoutOcr()
    {
        var result = await extractor.ExtractAsync(CreatePdf("Useful embedded health text"), "application/pdf");
        Assert.Equal(AiDocumentExtractionStatus.Success, result.Status);
        Assert.Contains("Useful", result.ExtractedText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    [InlineData("ab")]
    public async Task EmptyWhitespaceOrMeaninglessTxt_IsRejected(string value)
    {
        var result = await extractor.ExtractAsync(Encoding.UTF8.GetBytes(value), "text/plain");
        Assert.Equal(AiDocumentExtractionStatus.NoUsefulText, result.Status);
    }

    [Fact]
    public async Task BinaryTxt_IsMalformed()
    {
        var result = await extractor.ExtractAsync(new byte[] { 0, 1, 2, 0xff }, "text/plain");
        Assert.Equal(AiDocumentExtractionStatus.Malformed, result.Status);
    }

    [Theory]
    [InlineData("%PDF-1.7 garbage")]
    [InlineData("%PDF-1.7\n%%EOF")]
    public async Task FakeOrMalformedPdf_IsRejected(string value)
    {
        var result = await extractor.ExtractAsync(Encoding.ASCII.GetBytes(value), "application/pdf");
        Assert.Equal(AiDocumentExtractionStatus.Malformed, result.Status);
    }

    [Fact]
    public async Task ImageOnlyEquivalentPdfWithoutText_IsRejectedAndNeverOcred()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var result = await extractor.ExtractAsync(builder.Build(), "application/pdf");
        Assert.Equal(AiDocumentExtractionStatus.NoUsefulText, result.Status);
    }

    [Fact]
    public async Task EicarAndActivePdfContent_AreUnsafe()
    {
        var eicar = Encoding.ASCII.GetBytes(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!");
        Assert.Equal(AiFileSafetyStatus.Unsafe,
            (await scanner.ScanAsync(eicar, "text/plain")).Status);
        Assert.Equal(AiFileSafetyStatus.Unsafe,
            (await scanner.ScanAsync("%PDF-1.7 /JavaScript"u8.ToArray(), "application/pdf")).Status);
        Assert.Equal(AiFileSafetyStatus.Safe,
            (await scanner.ScanAsync("ordinary notes"u8.ToArray(), "text/plain")).Status);
    }

    [Fact]
    public async Task FileSystemStore_IsPrivateOpaqueAndDeleteIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"beeexy-ai-doc-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemAiDocumentBlobStore(root);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute,
                    File.GetUnixFileMode(root));
            }

            var key = AiBlobKey.CreateNew();
            await store.WritePrivateAsync(key, "private PHI"u8.ToArray());
            Assert.Equal("private PHI", Encoding.UTF8.GetString(await store.ReadPrivateAsync(key)));
            var file = Assert.Single(Directory.GetFiles(root));
            Assert.Equal(key.Value + ".blob", Path.GetFileName(file));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(file));
            }

            Assert.True(await store.DeleteAsync(key));
            Assert.False(await store.DeleteAsync(key));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileSystemStore_SweepsOnlyOpaqueArtifactsAtOrPastRetentionCutoff()
    {
        var root = Path.Combine(Path.GetTempPath(), $"beeexy-ai-sweep-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemAiDocumentBlobStore(root);
            var expired = AiBlobKey.CreateNew();
            var future = AiBlobKey.CreateNew();
            await store.WritePrivateAsync(expired, "expired"u8.ToArray());
            await store.WritePrivateAsync(future, "future"u8.ToArray());
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            File.SetLastWriteTimeUtc(
                Path.Combine(root, expired.Value + ".blob"),
                cutoff.UtcDateTime.AddMinutes(-1));

            Assert.Equal(1, await store.DeleteCreatedBeforeAsync(cutoff));
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.ReadPrivateAsync(expired));
            Assert.Equal("future", Encoding.UTF8.GetString(await store.ReadPrivateAsync(future)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("ABCDEF")]
    [InlineData("")]
    public void BlobKeysRejectPathAndNonOpaqueInput(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => AiBlobKey.Parse(value));
    }

    private static byte[] CreatePdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(40, 700), font);
        return builder.Build();
    }
}
