using System.Text.Json;

namespace MediaForge.Services;

public sealed record ActivityHistoryItem(
    string Kind,
    string SourceName,
    string OutputName,
    string CompletedAt,
    string? OutputPath);

public sealed class HistoryService
{
    private const int MaximumItems = 100;
    private readonly string _historyPath;

    public HistoryService(string? historyPath = null)
    {
        _historyPath = Path.GetFullPath(historyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaForge",
            "activity-history.json"));
    }

    public async Task<IReadOnlyList<ActivityHistoryItem>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(_historyPath, cancellationToken);
        return JsonSerializer.Deserialize<List<ActivityHistoryItem>>(json) ?? [];
    }

    public async Task AddAsync(ActivityHistoryItem item, CancellationToken cancellationToken = default)
    {
        var history = (await ReadAsync(cancellationToken)).ToList();
        history.Insert(0, item);
        if (history.Count > MaximumItems)
        {
            history.RemoveRange(MaximumItems, history.Count - MaximumItems);
        }

        var directory = Path.GetDirectoryName(_historyPath)
            ?? throw new InvalidOperationException("Não foi possível determinar a pasta do histórico.");
        Directory.CreateDirectory(directory);
        var temporary = _historyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(history), cancellationToken);
            File.Move(temporary, _historyPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_historyPath))
        {
            File.Delete(_historyPath);
        }
        return Task.CompletedTask;
    }
}
