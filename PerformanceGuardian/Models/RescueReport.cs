namespace PerformanceGuardian.Models;

public sealed record RescueReport(
    SystemSnapshot Before,
    SystemSnapshot After,
    IReadOnlyList<string> Actions,
    string Summary);
