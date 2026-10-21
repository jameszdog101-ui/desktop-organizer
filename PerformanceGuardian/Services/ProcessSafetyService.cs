using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PerformanceGuardian.Services;

public sealed class ProcessSafetyService
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
        "svchost", "dwm", "winlogon", "explorer", "fontdrvhost", "sihost", "taskhostw",
        "StartMenuExperienceHost", "SearchHost", "ShellExperienceHost", "RuntimeBroker",
        "PerformanceGuardian"
    };

    private readonly Dictionary<int, ProcessPriorityClass> _changedPriorities = new();

    public IReadOnlyList<string> ApplySafePriorityAdjustment()
    {
        var actions = new List<string>();
        var foregroundId = GetForegroundProcessId();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || ProtectedNames.Contains(process.ProcessName)) continue;
                var current = process.PriorityClass;
                if ((uint)process.Id == foregroundId && current == ProcessPriorityClass.Normal)
                {
                    RememberAndSet(process, ProcessPriorityClass.AboveNormal);
                    actions.Add($"提高前景程式 {process.ProcessName} 的排程優先權（暫時）");
                }
                else if ((uint)process.Id != foregroundId && current == ProcessPriorityClass.Normal &&
                         process.TotalProcessorTime > TimeSpan.FromSeconds(20) && process.WorkingSet64 > 350L * 1024 * 1024)
                {
                    RememberAndSet(process, ProcessPriorityClass.BelowNormal);
                    actions.Add($"降低背景程式 {process.ProcessName} 的排程優先權（暫時）");
                }
            }
            catch { }
            finally { process.Dispose(); }
        }

        return actions;
    }

    public IReadOnlyList<string> RestoreAll()
    {
        var restored = new List<string>();
        foreach (var item in _changedPriorities.ToArray())
        {
            try
            {
                using var process = Process.GetProcessById(item.Key);
                process.PriorityClass = item.Value;
                restored.Add(process.ProcessName);
            }
            catch { }
            _changedPriorities.Remove(item.Key);
        }
        return restored;
    }

    private void RememberAndSet(Process process, ProcessPriorityClass priority)
    {
        _changedPriorities.TryAdd(process.Id, process.PriorityClass);
        process.PriorityClass = priority;
    }

    public static string? GetForegroundProcessName()
    {
        var id = GetForegroundProcessId();
        if (id == 0) return null;
        try { using var process = Process.GetProcessById((int)id); return process.ProcessName; }
        catch { return null; }
    }

    private static uint GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        return window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out var id) == 0 ? 0 : id;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
