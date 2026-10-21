using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PerformanceGuardian.Models;

namespace PerformanceGuardian.Services;

public sealed class SystemMetricsService
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private readonly Dictionary<string, (long Read, long Write, DateTimeOffset At)> _diskSamples = new();
    private readonly PerformanceCounter? _diskTimeCounter;
    private readonly PerformanceCounter? _diskQueueCounter;
    private readonly DriveInfo? _systemDrive;

    public SystemMetricsService()
    {
        try
        {
            _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
            _diskQueueCounter = new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", "_Total", true);
            _diskTimeCounter.NextValue();
            _diskQueueCounter.NextValue();
        }
        catch
        {
            _diskTimeCounter?.Dispose();
            _diskQueueCounter?.Dispose();
        }

        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            _systemDrive = string.IsNullOrEmpty(root) ? null : new DriveInfo(root);
        }
        catch { }
    }

    public SystemSnapshot Capture()
    {
        var cpu = ReadCpuPercent();
        var (memoryPercent, availableMb) = ReadMemory();
        var disk = ReadDiskPressure();
        var (diskFree, diskProfile) = ReadDiskProfile();
        var foreground = ProcessSafetyService.GetForegroundProcessName() ?? "無";
        var reasons = new List<string>();
        if (memoryPercent >= 85) reasons.Add("記憶體壓力偏高");
        if (cpu >= 90) reasons.Add("CPU 長時間忙碌");
        if (disk >= 90) reasons.Add("磁碟活動偏高");
        if (diskFree < 15) reasons.Add("SSD 剩餘空間低於 15%，建議保留更多可用空間");

        return new SystemSnapshot(DateTimeOffset.Now, cpu, memoryPercent, disk, diskFree, diskProfile, availableMb,
            foreground, reasons.Count == 0 ? "系統運作正常" : string.Join("、", reasons));
    }

    private double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        if (_previousKernel == 0)
        {
            (_previousIdle, _previousKernel, _previousUser) = (idleValue, kernelValue, userValue);
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var totalDelta = (kernelValue - _previousKernel) + (userValue - _previousUser);
        (_previousIdle, _previousKernel, _previousUser) = (idleValue, kernelValue, userValue);
        return totalDelta == 0 ? 0 : Math.Clamp(100d * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private static (double Percent, ulong AvailableMb) ReadMemory()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status)
            ? (status.MemoryLoad, status.AvailablePhysical / 1024 / 1024)
            : (0, 0);
    }

    private double ReadDiskPressure()
    {
        try
        {
            if (_diskTimeCounter is not null && _diskQueueCounter is not null)
            {
                var activeTime = _diskTimeCounter.NextValue();
                var queueLength = _diskQueueCounter.NextValue();
                // A sustained queue of four requests is significant for an office workload,
                // even when a 256 GB SSD still reports high sequential throughput.
                return Math.Clamp(Math.Max(activeTime, queueLength * 25d), 0, 100);
            }
        }
        catch { }

        return ReadDiskThroughputFallback();
    }

    private (double FreePercent, string Profile) ReadDiskProfile()
    {
        try
        {
            if (_systemDrive is null || !_systemDrive.IsReady || _systemDrive.TotalSize <= 0)
                return (100, "256 GB SSD 最佳化");
            var free = 100d * _systemDrive.AvailableFreeSpace / _systemDrive.TotalSize;
            var capacityGb = _systemDrive.TotalSize / 1_000_000_000d;
            return (free, $"SSD {capacityGb:0} GB｜可用 {free:0}%");
        }
        catch { return (100, "256 GB SSD 最佳化"); }
    }

    private double ReadDiskThroughputFallback()
    {
        // Uses I/O throughput deltas as a low-overhead pressure signal. It deliberately avoids
        // synchronous WMI and high-frequency performance counter sampling.
        var now = DateTimeOffset.UtcNow;
        long read = 0, write = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (GetProcessIoCounters(process.Handle, out var io))
                {
                    read += (long)Math.Min(io.ReadTransferCount, long.MaxValue);
                    write += (long)Math.Min(io.WriteTransferCount, long.MaxValue);
                }
            }
            catch { }
            finally { process.Dispose(); }
        }

        const string key = "all";
        if (!_diskSamples.TryGetValue(key, out var previous))
        {
            _diskSamples[key] = (read, write, now);
            return 0;
        }

        _diskSamples[key] = (read, write, now);
        var seconds = Math.Max((now - previous.At).TotalSeconds, .1);
        var bytesPerSecond = Math.Max(0, read - previous.Read + write - previous.Write) / seconds;
        // SSD fallback: 350 MB/s avoids treating healthy high-throughput work as saturation.
        return Math.Clamp(bytesPerSecond / (350 * 1024 * 1024) * 100, 0, 100);
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters counters);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
