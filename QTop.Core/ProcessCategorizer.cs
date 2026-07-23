namespace QTop.Core;

public static class ProcessCategorizer
{
    public static ProcessCategory Categorize(ProcessClassificationInput input)
    {
        if (input.IsService)
            return ProcessCategory.Service;

        if (input.HasVisibleMainWindow)
            return ProcessCategory.App;

        if (input.IsSessionZero ||
            input.IsWindowsOwned ||
            input.IsKnownSystemProcess ||
            input.DetailsDenied)
            return ProcessCategory.WindowsSystem;

        return ProcessCategory.Background;
    }

    /// <summary>
    /// Promotes windowless Background processes to App when an ancestor in the process
    /// tree is an App (e.g. browser renderer/GPU child processes). The walk stops at
    /// Service/System ancestors so their helpers keep their own category.
    /// </summary>
    public static IReadOnlyList<ProcessSnapshot> PropagateAppCategory(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var byPid = new Dictionary<int, ProcessSnapshot>(snapshots.Count);
        foreach (ProcessSnapshot snapshot in snapshots)
            byPid[snapshot.ProcessId] = snapshot;

        var result = new ProcessSnapshot[snapshots.Count];
        for (int index = 0; index < snapshots.Count; index++)
        {
            ProcessSnapshot snapshot = snapshots[index];
            result[index] = snapshot.Category == ProcessCategory.Background && HasAppAncestor(snapshot, byPid)
                ? snapshot with { Category = ProcessCategory.App }
                : snapshot;
        }

        return result;
    }

    private static bool HasAppAncestor(ProcessSnapshot snapshot, Dictionary<int, ProcessSnapshot> byPid)
    {
        var seen = new HashSet<int> { snapshot.ProcessId };
        ProcessSnapshot current = snapshot;
        while (current.ParentProcessId is int parentId &&
               seen.Add(parentId) &&
               byPid.TryGetValue(parentId, out ProcessSnapshot? parent))
        {
            if (parent.Category == ProcessCategory.App)
                return true;

            if (parent.Category != ProcessCategory.Background)
                return false;

            current = parent;
        }

        return false;
    }
}
