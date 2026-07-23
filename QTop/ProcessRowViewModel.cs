using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using QTop.Core;

namespace QTop;

public sealed class ProcessRowViewModel : ObservableObject
{
    private ProcessSnapshot _snapshot;
    private int _nameGroupCount;
    private int _level;
    private bool _hasVisibleChildren;
    private bool _isExpanded = true;

    public ProcessRowViewModel(ProcessSnapshot snapshot, int nameGroupCount)
    {
        _snapshot = snapshot;
        _nameGroupCount = nameGroupCount;
    }

    public ProcessSnapshot Snapshot => _snapshot;
    public int ProcessId => _snapshot.ProcessId;
    public string ProcessName => _snapshot.ProcessName;
    public int? ParentProcessId => _snapshot.ParentProcessId;
    public string ParentProcessIdText => _snapshot.ParentProcessId?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable";
    public string CategoryLabel => _snapshot.Category.ToDisplayName();
    public int CategorySortOrder => _snapshot.Category.ToSortOrder();
    public string UserSession => string.IsNullOrWhiteSpace(_snapshot.UserName)
        ? $"Session {_snapshot.SessionId}"
        : $"{_snapshot.UserName} / Session {_snapshot.SessionId}";
    public string CpuTimeText => FormatDuration(_snapshot.TotalProcessorTime);
    public string CpuPercentText => _snapshot.CpuPercent is double percent
        ? string.Create(CultureInfo.CurrentCulture, $"{percent:0.0} %")
        : "—";
    public string CpuDetailText => _snapshot.CpuPercent is null
        ? CpuTimeText
        : $"{CpuPercentText} · {CpuTimeText}";
    public string MemoryText => FormatBytes(_snapshot.WorkingSetBytes);
    public string StartTimeText => _snapshot.StartTime?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Unavailable";
    public int NameGroupCount => _nameGroupCount;
    public string NameGroupCountText => _nameGroupCount.ToString(CultureInfo.InvariantCulture);
    public string ExecutablePath => _snapshot.ExecutablePath ?? "Unavailable";
    public string CommandLine => _snapshot.CommandLine ?? "Unavailable";
    public string CompanyName => _snapshot.CompanyName ?? "Unavailable";
    public string ProductName => _snapshot.ProductName ?? "Unavailable";
    public string Description => _snapshot.Description ?? "Unavailable";
    public string ThreadCountText => _snapshot.ThreadCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unavailable";
    public string HandleCountText => _snapshot.HandleCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unavailable";
    public string ServiceNamesText => _snapshot.ServiceNames.Count == 0 ? "None" : string.Join(", ", _snapshot.ServiceNames);
    public string AccessState => _snapshot.CanAccessDetails ? "Accessible" : "Restricted";
    public string IntegrityText => _snapshot.IntegrityLevel ?? "Unavailable";
    public BitmapSource? Icon => ProcessIconCache.GetIcon(_snapshot.ExecutablePath);

    // Tree state. Children/Parent/match flags are rebuilt by the view model on every
    // refresh or filter change; only display-relevant state raises change notifications.
    public List<ProcessRowViewModel> Children { get; } = [];
    public ProcessRowViewModel? Parent { get; set; }
    internal bool IsMatch { get; set; }
    internal bool SubtreeMatches { get; set; }
    internal Action? ExpansionChanged { get; set; }

    public int Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
                OnPropertyChanged(nameof(IndentMargin));
        }
    }

    public Thickness IndentMargin => new(_level * 16, 0, 0, 0);

    public bool HasVisibleChildren
    {
        get => _hasVisibleChildren;
        set => SetProperty(ref _hasVisibleChildren, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                ExpansionChanged?.Invoke();
        }
    }

    public void Update(ProcessSnapshot snapshot, int nameGroupCount)
    {
        if (ReferenceEquals(_snapshot, snapshot) && _nameGroupCount == nameGroupCount)
            return;

        _snapshot = snapshot;
        _nameGroupCount = nameGroupCount;
        OnPropertyChanged(string.Empty);
    }

    private static string FormatDuration(TimeSpan? value)
    {
        if (value is null)
            return "Unavailable";

        TimeSpan duration = value.Value;
        return duration.TotalDays >= 1
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalDays}d {duration:hh\\:mm\\:ss}")
            : duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
    }

    private static string FormatBytes(long? value)
    {
        if (value is null)
            return "Unavailable";

        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double amount = value.Value;
        int unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return $"{amount:N1} {units[unit]}";
    }
}
