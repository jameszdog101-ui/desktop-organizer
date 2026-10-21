using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PerformanceGuardian.Models;

namespace PerformanceGuardian.Services;

public sealed class OrganizerService(OrganizerSettings settings, SettingsService settingsService, ActivityLog log, string? desktopPath = null)
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DesktopPath { get; } = desktopPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string OrganizedPath => Path.Combine(DesktopPath, settings.OrganizedFolderName);
    private string HistoryPath => Path.Combine(settingsService.DataDirectory, "organizer-history.json");

    public async Task<OrganizerResult> OrganizeAsync(bool ignoreWaitingPeriod, CancellationToken cancellationToken)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return new OrganizerResult(0, 0, ["已有整理作业正在执行。"]);

        try
        {
            var messages = new List<string>();
            var moves = new List<FileMove>();
            var skipped = 0;
            var allowed = settings.Extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.Now;

            foreach (var file in new DirectoryInfo(DesktopPath).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!allowed.Contains(file.Extension)) continue;
                if (file.Attributes.HasFlag(FileAttributes.Hidden) || file.Attributes.HasFlag(FileAttributes.System)) continue;
                if (!ignoreWaitingPeriod && now - file.LastWriteTime < TimeSpan.FromMinutes(settings.NewFileWaitMinutes))
                {
                    skipped++;
                    continue;
                }
                if (IsFileInUse(file.FullName))
                {
                    skipped++;
                    messages.Add($"使用中，已略过：{file.Name}");
                    continue;
                }

                var monthFolder = file.LastWriteTime.ToString("yyyy年MM月");
                var destinationDirectory = Path.Combine(OrganizedPath, monthFolder);
                var destination = Path.Combine(destinationDirectory, file.Name);
                Directory.CreateDirectory(destinationDirectory);

                if (File.Exists(destination))
                {
                    if (await FilesAreEqualAsync(file.FullName, destination, cancellationToken))
                    {
                        var duplicateDirectory = Path.Combine(OrganizedPath, "重複檔 Duplicates");
                        Directory.CreateDirectory(duplicateDirectory);
                        destination = GetUniquePath(Path.Combine(duplicateDirectory, file.Name));
                    }
                    else destination = GetUniquePath(destination);
                }

                File.Move(file.FullName, destination);
                moves.Add(new FileMove(file.FullName, destination));
                messages.Add($"{file.Name} → {monthFolder}");
                await log.WriteAsync("文件整理", $"{file.FullName} → {destination}", cancellationToken);
            }

            if (moves.Count > 0) await AppendBatchAsync(new OrganizerBatch(now, moves), cancellationToken);
            return new OrganizerResult(moves.Count, skipped, messages);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<OrganizerResult> UndoLastAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var batches = await LoadBatchesAsync(cancellationToken);
            if (batches.Count == 0) return new OrganizerResult(0, 0, ["没有可复原的整理纪录。"]);
            var batch = batches[^1];
            var restored = 0;
            var skipped = 0;
            var messages = new List<string>();
            foreach (var move in batch.Moves.Reverse())
            {
                if (!File.Exists(move.To)) { skipped++; continue; }
                var destination = GetUniquePath(move.From);
                File.Move(move.To, destination);
                restored++;
                messages.Add($"已复原：{Path.GetFileName(destination)}");
                await log.WriteAsync("复原整理", $"{move.To} → {destination}", cancellationToken);
            }
            batches.RemoveAt(batches.Count - 1);
            await SaveBatchesAsync(batches, cancellationToken);
            return new OrganizerResult(restored, skipped, messages);
        }
        finally { _operationGate.Release(); }
    }

    private static bool IsFileInUse(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name}_{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static async Task<bool> FilesAreEqualAsync(string first, string second, CancellationToken cancellationToken)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        await using var firstStream = File.OpenRead(first);
        await using var secondStream = File.OpenRead(second);
        var firstHash = await SHA256.HashDataAsync(firstStream, cancellationToken);
        var secondHash = await SHA256.HashDataAsync(secondStream, cancellationToken);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    private async Task AppendBatchAsync(OrganizerBatch batch, CancellationToken cancellationToken)
    {
        var batches = await LoadBatchesAsync(cancellationToken);
        batches.Add(batch);
        if (batches.Count > 100) batches.RemoveRange(0, batches.Count - 100);
        await SaveBatchesAsync(batches, cancellationToken);
    }

    private async Task<List<OrganizerBatch>> LoadBatchesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(HistoryPath)) return [];
            await using var stream = File.OpenRead(HistoryPath);
            return await JsonSerializer.DeserializeAsync<List<OrganizerBatch>>(stream, JsonOptions, cancellationToken) ?? [];
        }
        catch { return []; }
    }

    private async Task SaveBatchesAsync(List<OrganizerBatch> batches, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settingsService.DataDirectory);
        await using var stream = File.Create(HistoryPath);
        await JsonSerializer.SerializeAsync(stream, batches, JsonOptions, cancellationToken);
    }
}
