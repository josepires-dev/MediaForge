using MediaForge.Services;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

public sealed class PdfConversionTests(Workspace workspace) : IClassFixture<Workspace>
{
    private async Task<string> CreatePdfAsync(string name, params string[] pages)
    {
        var path = workspace.PathOf(name);
        await File.WriteAllBytesAsync(path, TestData.CreatePdf(pages));
        return path;
    }

    [Fact(DisplayName = "Poppler extracts selectable PDF text")]
    public async Task Extracts_selectable_text()
    {
        var pdf = await CreatePdfAsync("two-pages.pdf", "MediaForge page one", "MediaForge page two");
        var result = await workspace.Service.ConvertAsync(pdf, new ConversionOptions("md"), workspace.Output);
        Assert.Contains("MediaForge page one", await File.ReadAllTextAsync(result.FullPath), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Multipage PDF converts to a ZIP of page images")]
    public async Task Multipage_pdf_becomes_zip_of_images()
    {
        var pdf = await CreatePdfAsync("two-pages.pdf", "MediaForge page one", "MediaForge page two");
        var result = await workspace.Service.ConvertAsync(pdf, new ConversionOptions("png"), workspace.Output);
        Assert.Equal("zip", result.Format);
        Assert.Equal(2, TestData.GetZipEntryCount(result.FullPath));
    }

    [Fact(DisplayName = "Single-page PDF returns one image directly")]
    public async Task Single_page_pdf_returns_one_image()
    {
        var pdf = await CreatePdfAsync("one-page.pdf", "MediaForge single page");
        var result = await workspace.Service.ConvertAsync(pdf, new ConversionOptions("png"), workspace.Output);
        Assert.Equal("png", result.Format);
        Assert.True(File.Exists(result.FullPath));
    }
}
