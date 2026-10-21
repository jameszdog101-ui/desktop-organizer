namespace PerformanceGuardian.Models;

public sealed class OrganizerSettings
{
    public bool AutoOrganizeEnabled { get; set; } = true;
    public int ScanIntervalMinutes { get; set; } = 15;
    public int NewFileWaitMinutes { get; set; } = 60;
    public string OrganizedFolderName { get; set; } = "整理 Organized";
    public string[] Extensions { get; set; } =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".ppt", ".pptx",
        ".txt", ".rtf", ".md", ".odt", ".ods", ".odp", ".hwp"
    ];
}

public sealed record FileMove(string From, string To);

public sealed record OrganizerBatch(DateTimeOffset Timestamp, IReadOnlyList<FileMove> Moves);

public sealed record OrganizerResult(int Moved, int Skipped, IReadOnlyList<string> Messages);
