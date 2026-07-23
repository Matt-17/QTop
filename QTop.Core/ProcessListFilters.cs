using System.Globalization;

namespace QTop.Core;

public static class ProcessListFilters
{
    public static bool Matches(
        ProcessSnapshot snapshot,
        string? searchText,
        ProcessCategory? category,
        bool hideProtectedSystem)
    {
        if (hideProtectedSystem &&
            (snapshot.Category == ProcessCategory.WindowsSystem || snapshot.IsProtectedOrSystem))
            return false;

        if (category.HasValue && snapshot.Category != category.Value)
            return false;

        if (string.IsNullOrWhiteSpace(searchText))
            return true;

        string search = searchText.Trim();
        return Contains(snapshot.ProcessName, search) ||
               snapshot.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase) ||
               Contains(snapshot.ExecutablePath, search) ||
               snapshot.ServiceNames.Any(name => Contains(name, search));
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }
}
