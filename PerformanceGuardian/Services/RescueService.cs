using PerformanceGuardian.Models;

namespace PerformanceGuardian.Services;

public sealed class RescueService(SystemMetricsService metrics, ProcessSafetyService safety, ActivityLog log)
{
    public async Task<RescueReport> RunAsync(CancellationToken cancellationToken)
    {
        var before = metrics.Capture();
        var actions = safety.ApplySafePriorityAdjustment();
        foreach (var action in actions) await log.WriteAsync("介入", action, cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        var after = metrics.Capture();
        var improved = after.CpuPercent < before.CpuPercent - 3 ||
                       after.DiskBusyPercent < before.DiskBusyPercent - 5 ||
                       after.AvailableMemoryMb > before.AvailableMemoryMb + 64;
        var summary = actions.Count == 0
            ? "未找到符合安全條件的介入目標；系統設定保持不變。"
            : improved
                ? "資源競爭指標已有改善；暫時設定可隨時還原。"
                : "已完成安全調度，但短時間內尚未觀察到明顯改善。";
        await log.WriteAsync("結果", summary, cancellationToken);
        return new RescueReport(before, after, actions, summary);
    }
}
