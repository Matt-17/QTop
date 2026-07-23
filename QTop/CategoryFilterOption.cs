using System.Globalization;
using QTop.Core;

namespace QTop;

public sealed class CategoryFilterOption : ObservableObject
{
    private int _matchCount;
    private double _cpuPercent;

    public CategoryFilterOption(string displayName, ProcessCategory? category)
    {
        DisplayName = displayName;
        Category = category;
    }

    public string DisplayName { get; }
    public ProcessCategory? Category { get; }
    public bool HasMatches => _matchCount > 0;

    public string StatsText => string.Create(CultureInfo.CurrentCulture, $"{_matchCount:N0} · {_cpuPercent:0.0} %");

    public void UpdateStats(int matchCount, double cpuPercent)
    {
        if (_matchCount == matchCount && Math.Abs(_cpuPercent - cpuPercent) < 0.05)
            return;

        bool hadMatches = HasMatches;
        _matchCount = matchCount;
        _cpuPercent = cpuPercent;
        OnPropertyChanged(nameof(StatsText));
        if (hadMatches != HasMatches)
            OnPropertyChanged(nameof(HasMatches));
    }
}
