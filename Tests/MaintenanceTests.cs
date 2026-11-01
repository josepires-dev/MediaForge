using MediaForge.Services;
using MediaForge.Tests.Support;

namespace MediaForge.Tests;

public sealed class MaintenanceTests(Workspace workspace) : IClassFixture<Workspace>
{
    [Fact(DisplayName = "Stale work directories are removed while recent and foreign folders are kept")]
    public void Removes_only_stale_work_directories()
    {
        var staleTemp = workspace.PathOf("temp-stale");
        var service = new ConversionService(TestPaths.Tools, staleTemp);
        var staleDirectory = Path.Combine(staleTemp, Guid.NewGuid().ToString("N"));
        var freshDirectory = Path.Combine(staleTemp, Guid.NewGuid().ToString("N"));
        var foreignDirectory = Path.Combine(staleTemp, "not-a-work-directory");
        Directory.CreateDirectory(staleDirectory);
        Directory.CreateDirectory(freshDirectory);
        Directory.CreateDirectory(foreignDirectory);
        Directory.SetLastWriteTimeUtc(staleDirectory, DateTime.UtcNow.AddDays(-3));
        Directory.SetLastWriteTimeUtc(foreignDirectory, DateTime.UtcNow.AddDays(-3));

        var removed = service.CleanupStaleWorkDirectories(TimeSpan.FromHours(24));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(staleDirectory));
        Assert.True(Directory.Exists(freshDirectory));
        Assert.True(Directory.Exists(foreignDirectory));
    }

    [Fact(DisplayName = "Environment doctor approves the bundled tools and ImageMagick policy")]
    public void Doctor_approves_the_bundled_tools()
    {
        var report = EnvironmentDoctor.Run(TestPaths.Tools, workspace.PathOf("doctor-temp"));
        Assert.All(report, check => Assert.True(check.Passed, EnvironmentDoctor.Format(report)));
    }

    [Fact(DisplayName = "Environment doctor reports missing tools and policy")]
    public void Doctor_reports_missing_tools()
    {
        var emptyTools = workspace.PathOf("no-tools");
        Directory.CreateDirectory(emptyTools);
        var report = EnvironmentDoctor.Run(emptyTools, workspace.PathOf("doctor-temp"));
        Assert.True(report.Count(check => !check.Passed) >= 7, EnvironmentDoctor.Format(report));
    }

    [Fact(DisplayName = "Activity history persists conversion records")]
    public async Task History_persists_records()
    {
        var history = new HistoryService(workspace.PathOf(Path.Combine("history-persist", "activity.json")));
        await history.AddAsync(new ActivityHistoryItem("Conversão", "input.docx", "output.md", "08/10/2026 20:00", "output.md"));
        Assert.Single(await history.ReadAsync());
    }

    [Fact(DisplayName = "Activity history can be cleared without touching outputs")]
    public async Task History_can_be_cleared()
    {
        var history = new HistoryService(workspace.PathOf(Path.Combine("history-clear", "activity.json")));
        await history.AddAsync(new ActivityHistoryItem("Conversão", "input.docx", "output.md", "08/10/2026 20:00", "output.md"));
        await history.ClearAsync();
        Assert.Empty(await history.ReadAsync());
    }
}
