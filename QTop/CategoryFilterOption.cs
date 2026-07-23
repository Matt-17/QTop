using System.Globalization;
using QTop.Core;

namespace QTop;

public sealed class CategoryFilterOption : ObservableObject
{
    private int _matchCount;
    private double _cpuPercent;
    private bool _isVisible = true;

    public CategoryFilterOption(string displayName, ProcessCategory? category)
    {
        DisplayName = displayName;
        Category = category;
    }

    public string DisplayName { get; }
    public ProcessCategory? Category { get; }
    public bool IsVisible => _isVisible;

    public string StatsText => string.Create(CultureInfo.CurrentCulture, $"{_matchCount:N0} · {_cpuPercent:0.0} %");

    public void UpdateStats(int matchCount, double cpuPercent, bool isVisible)
    {
        if (_isVisible != isVisible)
        {
            _isVisible = isVisible;
            OnPropertyChanged(nameof(IsVisible));
        }

        if (_matchCount == matchCount && Math.Abs(_cpuPercent - cpuPercent) < 0.05)
            return;

        _matchCount = matchCount;
        _cpuPercent = cpuPercent;
        OnPropertyChanged(nameof(StatsText));
    }
}
