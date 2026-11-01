using System.Text.Json;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

public sealed class MergeTests(Workspace workspace) : IClassFixture<Workspace>
{
    private async Task<string> WriteAsync(string name, string content)
    {
        var path = workspace.PathOf(name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private async Task<JsonDocument> MergeJsonAsync(params string[] files)
    {
        var merged = await workspace.Service.MergeJsonAsync(files, workspace.Output);
        return JsonDocument.Parse(await File.ReadAllTextAsync(merged.FullPath));
    }

    [Fact(DisplayName = "Markdown files merge in input order")]
    public async Task Markdown_files_merge_in_order()
    {
        var first = await WriteAsync("one.md", "# First");
        var second = await WriteAsync("two.md", "# Second");
        var merged = await workspace.Service.MergeMarkdownAsync([first, second], workspace.Output);
        var text = await File.ReadAllTextAsync(merged.FullPath);
        Assert.Contains("# First", text, StringComparison.Ordinal);
        Assert.Contains("# Second", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("# First", StringComparison.Ordinal) < text.IndexOf("# Second", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "JSON object files merge into single JSON object")]
    public async Task Json_objects_merge()
    {
        var a = await WriteAsync("one.json", "{\"a\": 1}");
        var b = await WriteAsync("two.json", "{\"b\": 2}");
        var c = await WriteAsync("three.json", "{\"c\": 3}");
        using var parsed = await MergeJsonAsync(a, b, c);
        Assert.Equal(1, parsed.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(2, parsed.RootElement.GetProperty("b").GetInt32());
        Assert.Equal(3, parsed.RootElement.GetProperty("c").GetInt32());
    }

    [Fact(DisplayName = "JSON array files merge into single combined array")]
    public async Task Json_arrays_merge()
    {
        var a = await WriteAsync("array1.json", "[1, 2]");
        var b = await WriteAsync("array2.json", "[3, 4]");
        using var parsed = await MergeJsonAsync(a, b);
        Assert.Equal(4, parsed.RootElement.GetArrayLength());
    }

    [Fact(DisplayName = "JSON conversation exports merge all messages without overwriting")]
    public async Task Json_conversation_exports_merge()
    {
        var a = await WriteAsync("chat1.json", "{\"version\": \"1.0\", \"messages\": [{\"id\": 1}, {\"id\": 2}]}");
        var b = await WriteAsync("chat2.json", "{\"version\": \"1.0\", \"messages\": [{\"id\": 3}]}");
        using var parsed = await MergeJsonAsync(a, b);
        Assert.Equal(3, parsed.RootElement.GetProperty("messages").GetArrayLength());
        Assert.Equal(2, parsed.RootElement.GetProperty("totalSessions").GetInt32());
    }

    [Fact(DisplayName = "Mixed object and array JSON files are rejected fail-fast")]
    public async Task Mixed_json_is_rejected()
    {
        var obj = await WriteAsync("one.json", "{\"a\": 1}");
        var array = await WriteAsync("array1.json", "[1, 2]");
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => workspace.Service.MergeJsonAsync([obj, array], workspace.Output));
        Assert.Contains("incompatíveis", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Markdown merge with a single file reports an accurate message")]
    public async Task Single_markdown_merge_reports_minimum()
    {
        var single = await WriteAsync("one.md", "# First");
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => workspace.Service.MergeMarkdownAsync([single], workspace.Output));
        Assert.Contains("pelo menos dois", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Markdown merge rejects non-Markdown files without mentioning JSON")]
    public async Task Markdown_merge_rejects_other_types()
    {
        var markdown = await WriteAsync("one.md", "# First");
        var text = await WriteAsync("notes.txt", "MediaForge plain text");
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => workspace.Service.MergeMarkdownAsync([markdown, text], workspace.Output));
        Assert.Contains(".md", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
