namespace QTop.Core;

public interface IProcessTerminator
{
    ProcessTerminationResult TryCloseMainWindow(int processId, TimeSpan timeout, DateTimeOffset? expectedStartTime = null);

    ProcessTerminationResult ForceKill(int processId, DateTimeOffset? expectedStartTime = null);
}
