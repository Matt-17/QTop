using System.ComponentModel;
using System.Diagnostics;

namespace QTop.Core;

public sealed class WindowsProcessTerminator : IProcessTerminator
{
    public ProcessTerminationResult TryCloseMainWindow(int processId, TimeSpan timeout, DateTimeOffset? expectedStartTime = null)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
                return new ProcessTerminationResult(ProcessTerminationOutcome.NotRunning, $"PID {processId} is no longer running.");

            if (!IsSameProcess(process, expectedStartTime))
                return new ProcessTerminationResult(ProcessTerminationOutcome.NotRunning, $"PID {processId} now belongs to a different process; nothing was closed.");

            if (process.MainWindowHandle == IntPtr.Zero)
                return new ProcessTerminationResult(ProcessTerminationOutcome.NoMainWindow, $"PID {processId} has no main window to close.");

            if (!process.CloseMainWindow())
                return new ProcessTerminationResult(ProcessTerminationOutcome.Failed, $"PID {processId} did not accept the close request.");

            if (process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue)))
                return new ProcessTerminationResult(ProcessTerminationOutcome.Succeeded, $"PID {processId} closed gracefully.");

            return new ProcessTerminationResult(ProcessTerminationOutcome.TimedOut, $"PID {processId} is still running after the close request.");
        }
        catch (Exception exception)
        {
            return ProcessTerminationResult.FromException(exception, $"Close PID {processId}");
        }
    }

    public ProcessTerminationResult ForceKill(int processId, DateTimeOffset? expectedStartTime = null)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
                return new ProcessTerminationResult(ProcessTerminationOutcome.NotRunning, $"PID {processId} is no longer running.");

            if (!IsSameProcess(process, expectedStartTime))
                return new ProcessTerminationResult(ProcessTerminationOutcome.NotRunning, $"PID {processId} now belongs to a different process; nothing was killed.");

            process.Kill(entireProcessTree: true);
            if (process.WaitForExit(5000))
                return new ProcessTerminationResult(ProcessTerminationOutcome.Succeeded, $"PID {processId} was killed.");

            return new ProcessTerminationResult(ProcessTerminationOutcome.TimedOut, $"PID {processId} kill was requested, but it is still running.");
        }
        catch (Exception exception)
        {
            return ProcessTerminationResult.FromException(exception, $"Kill PID {processId}");
        }
    }

    private static bool IsSameProcess(Process process, DateTimeOffset? expectedStartTime)
    {
        if (expectedStartTime is null)
            return true;

        try
        {
            var actualStartTime = new DateTimeOffset(process.StartTime);
            return (actualStartTime - expectedStartTime.Value).Duration() <= TimeSpan.FromSeconds(2);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            // The start time is unreadable (usually access denied); the kill itself will surface the real error.
            return true;
        }
    }
}
