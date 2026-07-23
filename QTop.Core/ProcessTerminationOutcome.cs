namespace QTop.Core;

public enum ProcessTerminationOutcome
{
    Succeeded = 0,
    TimedOut = 1,
    NotRunning = 2,
    AccessDenied = 3,
    NoMainWindow = 4,
    Failed = 5
}
