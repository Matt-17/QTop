using QTop.Core;

namespace QTop.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
{
    [TestMethod]
    public void ApplySnapshots_PopulatesAllRowsAndCounts()
    {
        var snapshots = new[]
        {
            Snapshot(10, "alpha", ProcessCategory.App),
            Snapshot(11, "beta", ProcessCategory.Background),
            Snapshot(12, "gamma", ProcessCategory.Service) with { ServiceNames = ["SampleService"] }
        };
        var viewModel = new MainWindowViewModel(new FakeProcessProvider(snapshots));

        viewModel.ApplySnapshots(snapshots, selectedProcessId: null);

        Assert.AreEqual(3, viewModel.TotalProcessCount);
        Assert.AreEqual(3, viewModel.VisibleProcessCount);
        Assert.AreEqual(1, viewModel.AppCount);
        Assert.AreEqual(1, viewModel.ServiceCount);
        Assert.IsFalse(viewModel.HasAlert, viewModel.AlertText);
        CollectionAssert.AreEquivalent(new[] { 10, 11, 12 }, viewModel.Processes.Select(row => row.ProcessId).ToArray());
    }


    [TestMethod]
    public void ApplySnapshots_PreservesCurrentSelection_WhenNoPreferredProcessIsProvided()
    {
        var snapshots = new[]
        {
            Snapshot(10, "alpha", ProcessCategory.App),
            Snapshot(11, "beta", ProcessCategory.Background)
        };
        var viewModel = new MainWindowViewModel(new FakeProcessProvider(snapshots))
        {
            SelectedProcess = new ProcessRowViewModel(Snapshot(11, "beta", ProcessCategory.Background), 1)
        };

        viewModel.ApplySnapshots(snapshots, selectedProcessId: null);

        Assert.IsNotNull(viewModel.SelectedProcess);
        Assert.AreEqual(11, viewModel.SelectedProcess.ProcessId);
    }

    [TestMethod]
    public void ApplySnapshots_UsesPreferredProcess_WhenProvided()
    {
        var snapshots = new[]
        {
            Snapshot(10, "alpha", ProcessCategory.App),
            Snapshot(11, "beta", ProcessCategory.Background)
        };
        var viewModel = new MainWindowViewModel(new FakeProcessProvider(snapshots))
        {
            SelectedProcess = new ProcessRowViewModel(Snapshot(11, "beta", ProcessCategory.Background), 1)
        };

        viewModel.ApplySnapshots(snapshots, selectedProcessId: 10);

        Assert.IsNotNull(viewModel.SelectedProcess);
        Assert.AreEqual(10, viewModel.SelectedProcess.ProcessId);
    }

    [TestMethod]
    public void Constructor_UsesFiveSecondsAsMinimumRefreshInterval()
    {
        var viewModel = new MainWindowViewModel(new FakeProcessProvider([]));

        Assert.AreEqual(5, viewModel.RefreshIntervals.Min(interval => interval.Seconds));
        Assert.AreEqual(5, viewModel.SelectedRefreshInterval.Seconds);
    }

    private static ProcessSnapshot Snapshot(int pid, string name, ProcessCategory category)
    {
        return new ProcessSnapshot
        {
            ProcessId = pid,
            ProcessName = name,
            Category = category,
            SessionId = 1,
            CanAccessDetails = true
        };
    }

    private sealed class FakeProcessProvider(IReadOnlyList<ProcessSnapshot> snapshots) : IProcessProvider
    {
        public IReadOnlyList<ProcessSnapshot> GetProcesses() => snapshots;

        public ProcessSnapshot? TryGetProcess(int processId)
        {
            return snapshots.FirstOrDefault(snapshot => snapshot.ProcessId == processId);
        }
    }
}
