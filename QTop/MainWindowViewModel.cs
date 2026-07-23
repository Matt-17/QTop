using System.Globalization;
using System.Security.Principal;
using QTop.Core;

namespace QTop;

public sealed class MainWindowViewModel : ObservableObject
{
    // Nearly every service/system process descends from these supervisors; nesting them
    // all under one node makes the tree useless, so their children stay top-level.
    private static readonly HashSet<string> FlattenedParentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "services",
        "wininit"
    };

    private readonly IProcessProvider _processProvider;
    private readonly List<ProcessRowViewModel> _allRows = [];
    private readonly List<ProcessRowViewModel> _rootRows = [];
    private string _searchText = string.Empty;
    private string _sortKey = "ProcessName";
    private bool _sortDescending;
    private CategoryFilterOption _selectedCategoryFilter = null!;
    private RefreshIntervalOption _selectedRefreshInterval = null!;
    private bool _hideProtectedSystem = true;
    private bool _confirmBeforeKill;
    private ProcessRowViewModel? _selectedProcess;
    private bool _isRefreshing;
    private string _statusText = "Ready.";
    private string? _alertText;
    private string _lastRefreshStatusText = "Last refresh: not started";
    private int _totalProcessCount;
    private int _visibleProcessCount;
    private int _appCount;
    private int _serviceCount;
    private int _protectedCount;

    public MainWindowViewModel(IProcessProvider processProvider)
    {
        _processProvider = processProvider;
        CategoryFilters =
        [
            new CategoryFilterOption("All categories", null),
            new CategoryFilterOption(ProcessCategory.App.ToDisplayName(), ProcessCategory.App),
            new CategoryFilterOption(ProcessCategory.Background.ToDisplayName(), ProcessCategory.Background),
            new CategoryFilterOption(ProcessCategory.Service.ToDisplayName(), ProcessCategory.Service),
            new CategoryFilterOption(ProcessCategory.WindowsSystem.ToDisplayName(), ProcessCategory.WindowsSystem),
            new CategoryFilterOption(ProcessCategory.Unknown.ToDisplayName(), ProcessCategory.Unknown)
        ];
        RefreshIntervals =
        [
            new RefreshIntervalOption("5 s", 5),
            new RefreshIntervalOption("10 s", 10),
            new RefreshIntervalOption("30 s", 30),
            new RefreshIntervalOption("60 s", 60)
        ];
        _selectedCategoryFilter = CategoryFilters[0];
        _selectedRefreshInterval = RefreshIntervals[0];

        IsElevated = DetectElevation();
    }

    public BulkObservableCollection<ProcessRowViewModel> Processes { get; } = [];
    public IReadOnlyList<CategoryFilterOption> CategoryFilters { get; }
    public IReadOnlyList<RefreshIntervalOption> RefreshIntervals { get; }
    public bool IsElevated { get; }
    public string ElevationStatusText => IsElevated ? "Admin" : "Limited";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RebuildVisibleRows();
        }
    }

    public CategoryFilterOption SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedCategoryFilter, value))
                RebuildVisibleRows();
        }
    }

    public RefreshIntervalOption SelectedRefreshInterval
    {
        get => _selectedRefreshInterval;
        set
        {
            if (value is not null && SetProperty(ref _selectedRefreshInterval, value))
            {
                OnPropertyChanged(nameof(RefreshStatusText));
            }
        }
    }

    public bool HideProtectedSystem
    {
        get => _hideProtectedSystem;
        set
        {
            if (SetProperty(ref _hideProtectedSystem, value))
                RebuildVisibleRows();
        }
    }

    public bool ConfirmBeforeKill
    {
        get => _confirmBeforeKill;
        set => SetProperty(ref _confirmBeforeKill, value);
    }

    public ProcessRowViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
            {
                OnPropertyChanged(nameof(CanKillSelected));
                OnPropertyChanged(nameof(SelectedProcessTitle));
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? AlertText
    {
        get => _alertText;
        private set
        {
            if (SetProperty(ref _alertText, value))
                OnPropertyChanged(nameof(HasAlert));
        }
    }

    public bool HasAlert => !string.IsNullOrWhiteSpace(AlertText);

    public string LastRefreshStatusText
    {
        get => _lastRefreshStatusText;
        private set => SetProperty(ref _lastRefreshStatusText, value);
    }

    public int TotalProcessCount
    {
        get => _totalProcessCount;
        private set
        {
            if (SetProperty(ref _totalProcessCount, value))
                OnPropertyChanged(nameof(TotalProcessCountText));
        }
    }

    public int VisibleProcessCount
    {
        get => _visibleProcessCount;
        private set
        {
            if (SetProperty(ref _visibleProcessCount, value))
                OnPropertyChanged(nameof(VisibleProcessCountText));
        }
    }

    public int AppCount
    {
        get => _appCount;
        private set
        {
            if (SetProperty(ref _appCount, value))
                OnPropertyChanged(nameof(AppCountText));
        }
    }

    public int ServiceCount
    {
        get => _serviceCount;
        private set
        {
            if (SetProperty(ref _serviceCount, value))
                OnPropertyChanged(nameof(ServiceCountText));
        }
    }

    public int ProtectedCount
    {
        get => _protectedCount;
        private set
        {
            if (SetProperty(ref _protectedCount, value))
                OnPropertyChanged(nameof(ProtectedCountText));
        }
    }

    public string TotalProcessCountText => TotalProcessCount.ToString("N0", CultureInfo.CurrentCulture);
    public string VisibleProcessCountText => VisibleProcessCount.ToString("N0", CultureInfo.CurrentCulture);
    public string AppCountText => AppCount.ToString("N0", CultureInfo.CurrentCulture);
    public string ServiceCountText => ServiceCount.ToString("N0", CultureInfo.CurrentCulture);
    public string ProtectedCountText => ProtectedCount.ToString("N0", CultureInfo.CurrentCulture);
    public string ProcessCountStatusText => $"Shown: {VisibleProcessCount:N0}/{TotalProcessCount:N0}";
    public string RefreshStatusText => $"Refresh: {SelectedRefreshInterval.DisplayName}";
    public string FilterStatusText => BuildFilterStatusText();
    public string SelectedProcessTitle => SelectedProcess is null ? "No process selected" : $"{SelectedProcess.ProcessName} ({SelectedProcess.ProcessId})";
    public bool CanKillSelected => ProcessActionRules.CanKill(SelectedProcess?.Snapshot, Environment.ProcessId);

    public async Task RefreshAsync(int? preferredProcessId = null)
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        try
        {
            IReadOnlyList<ProcessSnapshot> snapshots = await Task.Run(_processProvider.GetProcesses);
            ApplySnapshots(snapshots, preferredProcessId);
            SetStatus($"Refreshed {snapshots.Count:N0} processes.");
        }
        catch (Exception exception)
        {
            SetAlert($"Refresh failed: {exception.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void SetStatus(string message)
    {
        AlertText = null;
        StatusText = message;
    }

    public void SetAlert(string message)
    {
        AlertText = message;
    }

    internal void ApplySnapshots(IReadOnlyList<ProcessSnapshot> snapshots, int? selectedProcessId)
    {
        selectedProcessId ??= SelectedProcess?.ProcessId;

        Dictionary<string, int> groupCounts = snapshots
            .GroupBy(snapshot => snapshot.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        // Reuse row instances by PID so unchanged rows update in place, keeping selection,
        // expansion state, and (when the tree shape is unchanged) the scroll position.
        var previousByPid = new Dictionary<int, ProcessRowViewModel>(_allRows.Count);
        foreach (ProcessRowViewModel existing in _allRows)
            previousByPid[existing.ProcessId] = existing;

        _allRows.Clear();
        int appCount = 0;
        int serviceCount = 0;
        int protectedCount = 0;
        foreach (ProcessSnapshot snapshot in snapshots)
        {
            int nameGroupCount = groupCounts.GetValueOrDefault(snapshot.ProcessName, 1);
            if (previousByPid.TryGetValue(snapshot.ProcessId, out ProcessRowViewModel? row))
            {
                previousByPid.Remove(snapshot.ProcessId);
                row.Update(snapshot, nameGroupCount);
            }
            else
            {
                row = new ProcessRowViewModel(snapshot, nameGroupCount);
            }

            row.ExpansionChanged = RebuildVisibleRows;
            _allRows.Add(row);

            if (snapshot.Category == ProcessCategory.App)
                appCount++;
            if (snapshot.Category == ProcessCategory.Service)
                serviceCount++;
            if (snapshot.Category == ProcessCategory.WindowsSystem || snapshot.IsProtectedOrSystem)
                protectedCount++;
        }

        BuildTree();

        TotalProcessCount = _allRows.Count;
        AppCount = appCount;
        ServiceCount = serviceCount;
        ProtectedCount = protectedCount;
        RebuildVisibleRows();
        SelectedProcess = selectedProcessId is int pid ? Processes.FirstOrDefault(row => row.ProcessId == pid) : null;
        LastRefreshStatusText = $"Last refresh: {DateTime.Now:T}";
    }

    private void BuildTree()
    {
        var byPid = new Dictionary<int, ProcessRowViewModel>(_allRows.Count);
        foreach (ProcessRowViewModel row in _allRows)
        {
            row.Children.Clear();
            row.Parent = null;
            byPid[row.ProcessId] = row;
        }

        _rootRows.Clear();
        foreach (ProcessRowViewModel row in _allRows)
        {
            if (row.ParentProcessId is int parentId &&
                parentId != row.ProcessId &&
                byPid.TryGetValue(parentId, out ProcessRowViewModel? parent) &&
                !FlattenedParentNames.Contains(parent.ProcessName) &&
                !IsInAncestorChain(row, parent))
            {
                row.Parent = parent;
                parent.Children.Add(row);
            }
            else
            {
                _rootRows.Add(row);
            }
        }

        foreach (ProcessRowViewModel root in _rootRows)
            ComputeSubtreeAggregates(root);

        SortTree();
    }

    internal void SetSort(string? sortKey, bool descending)
    {
        if (string.IsNullOrEmpty(sortKey))
            return;

        _sortKey = sortKey;
        _sortDescending = descending;
        SortTree();
        RebuildVisibleRows();
    }

    private void SortTree()
    {
        Comparison<ProcessRowViewModel> comparison = BuildComparison();
        _rootRows.Sort(comparison);
        foreach (ProcessRowViewModel row in _allRows)
        {
            if (row.Children.Count > 1)
                row.Children.Sort(comparison);
        }
    }

    private Comparison<ProcessRowViewModel> BuildComparison()
    {
        int direction = _sortDescending ? -1 : 1;
        Func<ProcessRowViewModel, ProcessRowViewModel, int> primary = _sortKey switch
        {
            "ProcessId" => static (a, b) => a.ProcessId.CompareTo(b.ProcessId),
            "CategorySortOrder" => static (a, b) => a.CategorySortOrder.CompareTo(b.CategorySortOrder),
            "CpuPercent" => static (a, b) => a.SubtreeCpuPercent.CompareTo(b.SubtreeCpuPercent),
            "CpuTime" => static (a, b) => a.SubtreeCpuTimeTicks.CompareTo(b.SubtreeCpuTimeTicks),
            "Memory" => static (a, b) => a.SubtreeMemoryBytes.CompareTo(b.SubtreeMemoryBytes),
            "NameGroupCount" => static (a, b) => a.NameGroupCount.CompareTo(b.NameGroupCount),
            _ => static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.ProcessName, b.ProcessName)
        };

        return (left, right) =>
        {
            int result = direction * primary(left, right);
            if (result != 0)
                return result;

            result = StringComparer.OrdinalIgnoreCase.Compare(left.ProcessName, right.ProcessName);
            return result != 0 ? result : left.ProcessId.CompareTo(right.ProcessId);
        };
    }

    private static void ComputeSubtreeAggregates(ProcessRowViewModel row)
    {
        double cpuPercent = row.Snapshot.CpuPercent ?? 0;
        long cpuTimeTicks = row.Snapshot.TotalProcessorTime?.Ticks ?? 0;
        long memoryBytes = row.Snapshot.WorkingSetBytes ?? 0;
        foreach (ProcessRowViewModel child in row.Children)
        {
            ComputeSubtreeAggregates(child);
            cpuPercent += child.SubtreeCpuPercent;
            cpuTimeTicks += child.SubtreeCpuTimeTicks;
            memoryBytes += child.SubtreeMemoryBytes;
        }

        row.SubtreeCpuPercent = cpuPercent;
        row.SubtreeCpuTimeTicks = cpuTimeTicks;
        row.SubtreeMemoryBytes = memoryBytes;
    }

    private static bool IsInAncestorChain(ProcessRowViewModel target, ProcessRowViewModel start)
    {
        for (ProcessRowViewModel? current = start; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, target))
                return true;
        }

        return false;
    }

    private void RebuildVisibleRows()
    {
        ProcessCategory? selectedCategory = SelectedCategoryFilter?.Category;
        int optionCount = CategoryFilters.Count;
        var optionCounts = new int[optionCount];
        var optionCpu = new double[optionCount];
        int matchCount = 0;

        foreach (ProcessRowViewModel row in _allRows)
        {
            ProcessSnapshot snapshot = row.Snapshot;
            bool baseMatch = ProcessListFilters.Matches(snapshot, SearchText, null, HideProtectedSystem);
            row.IsMatch = baseMatch && (selectedCategory is null || snapshot.Category == selectedCategory);
            if (row.IsMatch)
                matchCount++;

            if (!baseMatch)
                continue;

            double cpu = snapshot.CpuPercent ?? 0;
            for (int index = 0; index < optionCount; index++)
            {
                CategoryFilterOption option = CategoryFilters[index];
                if (option.Category is null || option.Category == snapshot.Category)
                {
                    optionCounts[index]++;
                    optionCpu[index] += cpu;
                }
            }
        }

        for (int index = 0; index < optionCount; index++)
            CategoryFilters[index].UpdateStats(optionCounts[index], optionCpu[index]);

        foreach (ProcessRowViewModel root in _rootRows)
            ComputeSubtreeMatches(root);

        // While a search is active, matches under collapsed parents must still be reachable,
        // so the tree is traversed as if fully expanded.
        bool forceExpand = !string.IsNullOrWhiteSpace(SearchText);
        var visible = new List<ProcessRowViewModel>(matchCount + 16);
        foreach (ProcessRowViewModel root in _rootRows)
            AppendVisibleRows(root, 0, visible, forceExpand);

        if (!SameSequence(visible))
            Processes.ReplaceAll(visible);

        VisibleProcessCount = matchCount;
        OnPropertyChanged(nameof(ProcessCountStatusText));
        OnPropertyChanged(nameof(FilterStatusText));
    }

    private static bool ComputeSubtreeMatches(ProcessRowViewModel row)
    {
        bool matches = row.IsMatch;
        foreach (ProcessRowViewModel child in row.Children)
            matches |= ComputeSubtreeMatches(child);

        row.SubtreeMatches = matches;
        return matches;
    }

    private static void AppendVisibleRows(ProcessRowViewModel row, int depth, List<ProcessRowViewModel> visible, bool forceExpand)
    {
        if (!row.SubtreeMatches)
            return;

        row.Level = depth;
        bool hasVisibleChildren = false;
        foreach (ProcessRowViewModel child in row.Children)
        {
            if (child.SubtreeMatches)
            {
                hasVisibleChildren = true;
                break;
            }
        }

        row.HasVisibleChildren = hasVisibleChildren;
        visible.Add(row);
        if ((!row.IsExpanded && !forceExpand) || !hasVisibleChildren)
            return;

        foreach (ProcessRowViewModel child in row.Children)
            AppendVisibleRows(child, depth + 1, visible, forceExpand);
    }

    private bool SameSequence(List<ProcessRowViewModel> visible)
    {
        if (visible.Count != Processes.Count)
            return false;

        for (int index = 0; index < visible.Count; index++)
        {
            if (!ReferenceEquals(visible[index], Processes[index]))
                return false;
        }

        return true;
    }

    private string BuildFilterStatusText()
    {
        string category = SelectedCategoryFilter.DisplayName;
        if (string.IsNullOrWhiteSpace(SearchText))
            return $"Filter: {category}";

        return $"Filter: {category}, search '{SearchText.Trim()}'";
    }

    private static bool DetectElevation()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
