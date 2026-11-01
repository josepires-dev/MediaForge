using System.Text;
using System.Text.Json;
using MediaForge.Services;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

public sealed class DocumentConversionTests(Workspace workspace) : IClassFixture<Workspace>
{
    private async Task<string> CreateHtmlAsync()
    {
        var html = workspace.PathOf("source.html");
        await File.WriteAllTextAsync(html, "<html><body><h1>MediaForge HTML</h1><p>Local conversion</p></body></html>");
        return html;
    }

    private async Task<string> ReadResultAsync(string input, string format)
    {
        var result = await workspace.Service.ConvertAsync(input, new ConversionOptions(format), workspace.Output);
        return await File.ReadAllTextAsync(result.FullPath);
    }

    [Fact(DisplayName = "Pandoc converts HTML to Markdown")]
    public async Task Html_to_markdown()
    {
        var text = await ReadResultAsync(await CreateHtmlAsync(), "md");
        Assert.Contains("# MediaForge HTML", text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Pandoc converts HTML to plain text")]
    public async Task Html_to_plain_text()
    {
        var text = await ReadResultAsync(await CreateHtmlAsync(), "txt");
        Assert.Contains("Local conversion", text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Plain text converts to Markdown")]
    public async Task Plain_text_to_markdown()
    {
        var input = workspace.PathOf("notes.txt");
        await File.WriteAllTextAsync(input, "MediaForge plain text");
        Assert.Contains("MediaForge plain text", await ReadResultAsync(input, "md"), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Python converts to a notebook with marked cells")]
    public async Task Python_to_notebook()
    {
        var python = workspace.PathOf("cells.py");
        await File.WriteAllTextAsync(python, "# %% [markdown]\n# A notebook title\n# %%\nprint('MediaForge')\n");
        using var notebook = JsonDocument.Parse(await ReadResultAsync(python, "ipynb"));
        Assert.Equal(4, notebook.RootElement.GetProperty("nbformat").GetInt32());
        Assert.Equal(2, notebook.RootElement.GetProperty("cells").GetArrayLength());
    }

    [Fact(DisplayName = "Office document conversion service stores Markdown result")]
    public async Task Office_document_is_dispatched_to_the_document_converter()
    {
        var docx = workspace.PathOf("document.docx");
        TestData.CreateDocx(docx);
        var result = await workspace.Service.ConvertAsync(
            docx,
            new ConversionOptions("md"),
            workspace.Output,
            documentConverter: (bytes, extension, _) =>
            {
                if (extension != "docx" || bytes.Length == 0)
                {
                    throw new InvalidOperationException("Office document dispatch failed.");
                }
                return Task.FromResult("# MediaForge Office document");
            });
        Assert.Contains("MediaForge Office document", await File.ReadAllTextAsync(result.FullPath), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "WASM converts Office document to Markdown")]
    public async Task Wasm_converts_office_document()
    {
        var docx = workspace.PathOf("wasm-document.docx");
        TestData.CreateDocx(docx);
        var output = await TestData.ConvertDocxWithNodeAsync(TestPaths.Wasm, docx, workspace.Root);
        Assert.Contains("PASS: WASM converts Office document to Markdown", output, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Content-based detection identifies text file with .net extension")]
    public async Task Detects_text_file_with_unusual_extension()
    {
        var path = workspace.PathOf("Consultas do Search Console — biblia360.net");
        await File.WriteAllTextAsync(path, "# Consultas do Search Console — biblia360.net\n\n| Consulta | Cliques |\n|---|---:|\n| biblia 360 | 4 |\n", new UTF8Encoding(false));
        Assert.True(ConversionService.IsTextFile(path));
    }

    [Fact(DisplayName = "Text file with non-standard extension converts to Markdown preserving full basename and unicode chars")]
    public async Task Converts_text_file_with_unusual_extension()
    {
        var path = workspace.PathOf("Consultas do Search Console — biblia360.net");
        await File.WriteAllTextAsync(path, "# Consultas do Search Console — biblia360.net\n\n| Consulta | Cliques |\n|---|---:|\n| biblia 360 | 4 |\n", new UTF8Encoding(false));
        var result = await workspace.Service.ConvertAsync(path, new ConversionOptions("md"), workspace.Output);
        Assert.Equal("Consultas do Search Console — biblia360.net_convertido.md", result.FileName);
        Assert.Contains("biblia 360", await File.ReadAllTextAsync(result.FullPath), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Content-based detection identifies extensionless text file")]
    public async Task Detects_extensionless_text_file()
    {
        var path = workspace.PathOf("DOCUMENTO_SEM_EXTENSAO");
        await File.WriteAllTextAsync(path, "Conteúdo de texto sem extensão", new UTF8Encoding(false));
        Assert.True(ConversionService.IsTextFile(path));
    }

    [Fact(DisplayName = "Extensionless text file converts to Markdown")]
    public async Task Converts_extensionless_text_file()
    {
        var path = workspace.PathOf("DOCUMENTO_SEM_EXTENSAO");
        await File.WriteAllTextAsync(path, "Conteúdo de texto sem extensão", new UTF8Encoding(false));
        var result = await workspace.Service.ConvertAsync(path, new ConversionOptions("md"), workspace.Output);
        Assert.Equal("DOCUMENTO_SEM_EXTENSAO_convertido.md", result.FileName);
    }

    [Fact(DisplayName = "Binary file with null bytes is not identified as text")]
    public async Task Binary_file_is_not_text()
    {
        var path = workspace.PathOf("app.bin");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0xFF]);
        Assert.False(ConversionService.IsTextFile(path));
    }

    [Fact(DisplayName = "Unsupported binary file is rejected cleanly without invoking ImageMagick")]
    public async Task Unsupported_binary_is_rejected()
    {
        var path = workspace.PathOf("app.bin");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0xFF]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ConvertAsync(path, new ConversionOptions("md"), workspace.Output));
        Assert.Contains("não possui um formato suportado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
