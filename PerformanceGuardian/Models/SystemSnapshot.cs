namespace PerformanceGuardian.Models;

public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double MemoryPercent,
    double DiskBusyPercent,
    double DiskFreePercent,
    string DiskProfile,
    ulong AvailableMemoryMb,
    string ForegroundProcess,
    string PressureReason)
{
    public bool IsUnderPressure => MemoryPercent >= 85 || CpuPercent >= 90 || DiskBusyPercent >= 90;
}
