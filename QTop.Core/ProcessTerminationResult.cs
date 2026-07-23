using System.ComponentModel;

namespace QTop.Core;

public sealed record ProcessTerminationResult(
    ProcessTerminationOutcome Outcome,
    string Message)
{
    public bool IsSuccess => Outcome == ProcessTerminationOutcome.Succeeded ||
                             Outcome == ProcessTerminationOutcome.NotRunning;

    public static ProcessTerminationResult FromException(Exception exception, string action)
    {
        return exception switch
        {
            Win32Exception win32 when win32.NativeErrorCode == 5 =>
                new ProcessTerminationResult(ProcessTerminationOutcome.AccessDenied, $"{action} failed: access denied."),
            UnauthorizedAccessException =>
                new ProcessTerminationResult(ProcessTerminationOutcome.AccessDenied, $"{action} failed: access denied."),
            ArgumentException =>
                new ProcessTerminationResult(ProcessTerminationOutcome.NotRunning, $"{action} skipped: the process is no longer running."),
            InvalidOperationException =>
                new ProcessTerminationResult(ProcessTerminationOutcome.NotRunning, $"{action} skipped: the process has already exited."),
            Win32Exception win32 =>
                new ProcessTerminationResult(ProcessTerminationOutcome.Failed, $"{action} failed: {win32.Message} (Win32 {win32.NativeErrorCode})."),
            _ =>
                new ProcessTerminationResult(ProcessTerminationOutcome.Failed, $"{action} failed: {exception.Message}")
        };
    }
}
