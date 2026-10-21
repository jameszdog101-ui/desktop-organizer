using System.Text.Json;
using System.IO;

namespace PerformanceGuardian.Services;

public sealed class ActivityLog
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ActivityLog(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartDesktopGuardian", "activity.jsonl");
    }

    public string LogPath => _path;

    public async Task WriteAsync(string category, string message, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, category, message });
            await File.AppendAllTextAsync(_path, line + Environment.NewLine, cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
