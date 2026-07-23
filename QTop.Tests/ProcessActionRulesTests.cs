using QTop.Core;

namespace QTop.Tests;

[TestClass]
public sealed class ProcessActionRulesTests
{
    [TestMethod]
    public void CanKill_ReturnsFalse_ForMissingSelection()
    {
        Assert.IsFalse(ProcessActionRules.CanKill(null, currentProcessId: 10));
    }

    [TestMethod]
    public void CanKill_ReturnsFalse_ForCurrentProcessId()
    {
        ProcessSnapshot snapshot = Snapshot(10) with
        {
            IsCurrentProcess = false
        };

        Assert.IsFalse(ProcessActionRules.CanKill(snapshot, currentProcessId: 10));
    }

    [TestMethod]
    public void CanKill_ReturnsFalse_WhenSnapshotMarksCurrentProcess()
    {
        ProcessSnapshot snapshot = Snapshot(42) with
        {
            IsCurrentProcess = true
        };

        Assert.IsFalse(ProcessActionRules.CanKill(snapshot, currentProcessId: 10));
    }

    [TestMethod]
    public void CanKill_ReturnsTrue_ForOtherProcess()
    {
        ProcessSnapshot snapshot = Snapshot(42);

        Assert.IsTrue(ProcessActionRules.CanKill(snapshot, currentProcessId: 10));
    }

    [TestMethod]
    public void ShouldTryGracefulClose_OnlyForAppWithWindow()
    {
        Assert.IsTrue(ProcessActionRules.ShouldTryGracefulClose(Snapshot(1) with { Category = ProcessCategory.App, HasMainWindow = true }));
        Assert.IsFalse(ProcessActionRules.ShouldTryGracefulClose(Snapshot(2) with { Category = ProcessCategory.Background, HasMainWindow = true }));
        Assert.IsFalse(ProcessActionRules.ShouldTryGracefulClose(Snapshot(3) with { Category = ProcessCategory.App, HasMainWindow = false }));
    }

    private static ProcessSnapshot Snapshot(int pid)
    {
        return new ProcessSnapshot
        {
            ProcessId = pid,
            ProcessName = "sample",
            Category = ProcessCategory.Background
        };
    }
}
