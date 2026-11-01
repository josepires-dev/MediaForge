using MediaForge.Services;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

public sealed class ImageConversionTests(Workspace workspace) : IClassFixture<Workspace>
{
    private async Task<string> CreateInputAsync()
    {
        var ppm = workspace.PathOf("input.ppm");
        await TestData.WritePpmAsync(ppm);
        return ppm;
    }

    [Fact(DisplayName = "ImageMagick advertises writable common formats")]
    public async Task Advertises_common_formats()
    {
        var formats = await workspace.Service.GetAvailableFormatsAsync();
        Assert.Contains("png", formats);
        Assert.Contains("jpg", formats);
    }

    [Fact(DisplayName = "ImageMagick converts and resizes image")]
    public async Task Converts_and_resizes_to_png()
    {
        var ppm = await CreateInputAsync();
        var image = await workspace.Service.ConvertAsync(ppm, new ConversionOptions("png", Width: 4, Height: 4, Rotation: 90), workspace.Output);
        var header = (await File.ReadAllBytesAsync(image.FullPath))[..8];
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, header);
    }

    [Fact(DisplayName = "ImageMagick converts image to JPEG with quality option")]
    public async Task Converts_to_jpeg_with_quality()
    {
        var ppm = await CreateInputAsync();
        var jpeg = await workspace.Service.ConvertAsync(ppm, new ConversionOptions("jpg", Quality: 80), workspace.Output);
        var header = (await File.ReadAllBytesAsync(jpeg.FullPath))[..3];
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, header);
    }

    [Fact(DisplayName = "Conversion rejects dimensions above the configured safety limit")]
    public async Task Rejects_dimensions_above_limit()
    {
        var ppm = await CreateInputAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => workspace.Service.ConvertAsync(ppm, new ConversionOptions("png", Width: 16_001), workspace.Output));
    }

    [Fact(DisplayName = "Conversion honors cancellation before starting work")]
    public async Task Honors_cancellation()
    {
        var ppm = await CreateInputAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.Service.ConvertAsync(ppm, new ConversionOptions("png"), workspace.Output, cancellationToken: cancelled.Token));
    }

    [Fact(DisplayName = "Image formats exclude non-image formats like Markdown and text")]
    public async Task Image_formats_exclude_text_formats()
    {
        var imageFormats = await workspace.Service.GetAvailableImageFormatsAsync();
        Assert.Contains("png", imageFormats);
        Assert.DoesNotContain("md", imageFormats);
        Assert.DoesNotContain("txt", imageFormats);
    }
}
