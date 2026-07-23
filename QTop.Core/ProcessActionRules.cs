namespace QTop.Core;

public static class ProcessActionRules
{
    public static bool CanKill(ProcessSnapshot? snapshot, int currentProcessId)
    {
        return snapshot is not null &&
               snapshot.ProcessId != currentProcessId &&
               !snapshot.IsCurrentProcess;
    }

    public static bool ShouldTryGracefulClose(ProcessSnapshot snapshot)
    {
        return snapshot.Category == ProcessCategory.App && snapshot.HasMainWindow;
    }
}
