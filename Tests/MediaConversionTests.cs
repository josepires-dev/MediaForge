using MediaForge.Services;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

/// <summary>Áudio e vídeo: gera uma onda sonora e um vídeo curto uma única vez para a classe.</summary>
public sealed class MediaWorkspace : Workspace, IAsyncLifetime
{
    public string Wave => PathOf("tone.wav");

    public string Video => PathOf("sample.mp4");

    public async Task InitializeAsync()
    {
        await File.WriteAllBytesAsync(Wave, TestData.CreateWave());
        await TestData.RunAsync(
            Path.Combine(TestPaths.Tools, "ffmpeg.exe"),
            TestPaths.Tools,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=32x32:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", Video]);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class MediaConversionTests(MediaWorkspace workspace) : IClassFixture<MediaWorkspace>
{
    [Theory(DisplayName = "FFmpeg converts audio")]
    [InlineData("mp3")]
    [InlineData("wav")]
    [InlineData("m4a")]
    [InlineData("aac")]
    public async Task Converts_audio(string format)
    {
        var audio = await workspace.Service.ConvertAsync(workspace.Wave, new ConversionOptions(format), workspace.Output);
        Assert.True(new FileInfo(audio.FullPath).Length > 1_024, $"A saída {format} é demasiado pequena.");
    }

    [Theory(DisplayName = "FFmpeg converts video")]
    [InlineData("mp4")]
    [InlineData("webm")]
    [InlineData("mkv")]
    [InlineData("avi")]
    [InlineData("mov")]
    public async Task Converts_video(string format)
    {
        var video = await workspace.Service.ConvertAsync(workspace.Video, new ConversionOptions(format), workspace.Output);
        Assert.True(new FileInfo(video.FullPath).Length > 1_024, $"A saída {format} é demasiado pequena.");
    }

    [Fact(DisplayName = "FFmpeg extracts video frames to ZIP")]
    public async Task Extracts_video_frames()
    {
        var frames = await workspace.Service.ConvertAsync(workspace.Video, new ConversionOptions("zip"), workspace.Output);
        Assert.Equal("zip", frames.Format);
        Assert.True(TestData.GetZipEntryCount(frames.FullPath) > 0);
    }
}
