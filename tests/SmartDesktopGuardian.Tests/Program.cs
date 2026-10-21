using PerformanceGuardian.Models;
using PerformanceGuardian.Services;

var root = Path.Combine(Path.GetTempPath(), "SmartDesktopGuardianTests", Guid.NewGuid().ToString("N"));
var desktop = Path.Combine(root, "Desktop");
var data = Path.Combine(root, "Data");
Directory.CreateDirectory(desktop);

try
{
    var settings = new OrganizerSettings { NewFileWaitMinutes = 60 };
    var settingsService = new SettingsService(data);
    var log = new ActivityLog(Path.Combine(data, "activity.jsonl"));
    var organizer = new OrganizerService(settings, settingsService, log, desktop);
    var stamp = new DateTime(2026, 10, 21, 10, 0, 0);

    var pdf = Path.Combine(desktop, "研究资料.pdf");
    var docx = Path.Combine(desktop, "报告.docx");
    var image = Path.Combine(desktop, "照片.jpg");
    await File.WriteAllTextAsync(pdf, "same-content");
    await File.WriteAllTextAsync(docx, "document");
    await File.WriteAllTextAsync(image, "image");
    File.SetLastWriteTime(pdf, stamp);
    File.SetLastWriteTime(docx, stamp);

    var first = await organizer.OrganizeAsync(true, CancellationToken.None);
    Assert(first.Moved == 2, "应移动两个文书档案");
    Assert(File.Exists(Path.Combine(organizer.OrganizedPath, "2026年10月", "研究资料.pdf")), "PDF 应按月份归档");
    Assert(File.Exists(image), "图片不应被移动");

    var duplicate = Path.Combine(desktop, "研究资料.pdf");
    await File.WriteAllTextAsync(duplicate, "same-content");
    File.SetLastWriteTime(duplicate, stamp);
    var second = await organizer.OrganizeAsync(true, CancellationToken.None);
    Assert(second.Moved == 1, "重复档也应安全归档");
    Assert(Directory.GetFiles(Path.Combine(organizer.OrganizedPath, "重複檔 Duplicates")).Length == 1, "相同内容应进入重复档资料夹");

    var undo = await organizer.UndoLastAsync(CancellationToken.None);
    Assert(undo.Moved == 1 && File.Exists(Path.Combine(desktop, "研究资料.pdf")), "应复原最近一批整理");
    Console.WriteLine("PASS: 月份分类、格式筛选、重复档与复原测试通过。");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
}
