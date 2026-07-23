namespace QTop.Core;

public sealed record ProcessSnapshot
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public ProcessCategory Category { get; init; } = ProcessCategory.Unknown;
    public int? ParentProcessId { get; init; }
    public string? ExecutablePath { get; init; }
    public string? CommandLine { get; init; }
    public string? CompanyName { get; init; }
    public string? ProductName { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? StartTime { get; init; }
    public TimeSpan? TotalProcessorTime { get; init; }
    public double? CpuPercent { get; init; }
    public long? WorkingSetBytes { get; init; }
    public int? ThreadCount { get; init; }
    public int? HandleCount { get; init; }
    public int SessionId { get; init; }
    public string? UserName { get; init; }
    public IReadOnlyList<string> ServiceNames { get; init; } = Array.Empty<string>();
    public bool HasMainWindow { get; init; }
    public bool IsProtectedOrSystem { get; init; }
    public bool IsCurrentProcess { get; init; }
    public bool CanAccessDetails { get; init; }
    public string? IntegrityLevel { get; init; }
}
