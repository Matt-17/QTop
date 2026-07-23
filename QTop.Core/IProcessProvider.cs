namespace QTop.Core;

public interface IProcessProvider
{
    IReadOnlyList<ProcessSnapshot> GetProcesses();

    ProcessSnapshot? TryGetProcess(int processId);
}
