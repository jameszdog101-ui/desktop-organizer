using System.IO;
using System.Text;

namespace PerformanceGuardian.Services;

public sealed class DocumentationService(SettingsService settingsService, OrganizerService organizer)
{
    public string Generate()
    {
        Directory.CreateDirectory(settingsService.DataDirectory);
        var path = Path.Combine(settingsService.DataDirectory, "桌面整理工具说明书 Manual.txt");
        var content = new StringBuilder()
            .AppendLine("Smart Desktop Guardian｜智慧桌面管家")
            .AppendLine(new string('=', 48))
            .AppendLine()
            .AppendLine($"程序位置：{AppContext.BaseDirectory}")
            .AppendLine($"桌面位置：{organizer.DesktopPath}")
            .AppendLine($"整理资料夹：{organizer.OrganizedPath}")
            .AppendLine($"设定与纪录：{settingsService.DataDirectory}")
            .AppendLine()
            .AppendLine("文件整理")
            .AppendLine("- 支援 PDF、Word、Excel、PowerPoint、文字及开放文件格式。")
            .AppendLine("- 自动整理依文件最后修改时间放入 yyyy年MM月 资料夹。")
            .AppendLine("- 正在使用或仍在写入的文件会被略过。")
            .AppendLine("- 同名文件不会覆盖；相同内容会移入重複檔 Duplicates。")
            .AppendLine("- 可从程序内复原最近一次整理。")
            .AppendLine()
            .AppendLine("效能优化")
            .AppendLine("- 显示 CPU、RAM 与 SSD I/O 压力。")
            .AppendLine("- 只进行可还原的暂时程序优先权调度。")
            .AppendLine("- 不强制关闭应用程式，不清空系统快取，不停用 Pagefile。")
            .AppendLine()
            .AppendLine("路径会在每次重新产生说明书时依目前电脑自动更新。")
            .ToString();
        File.WriteAllText(path, content, new UTF8Encoding(true));
        return path;
    }
}
