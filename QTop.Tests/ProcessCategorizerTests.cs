using QTop.Core;

namespace QTop.Tests;

[TestClass]
public sealed class ProcessCategorizerTests
{
    [TestMethod]
    public void Categorize_ReturnsService_WhenServiceMappingExists()
    {
        var input = new ProcessClassificationInput
        {
            IsService = true,
            IsSessionZero = true,
            IsWindowsOwned = true
        };

        Assert.AreEqual(ProcessCategory.Service, ProcessCategorizer.Categorize(input));
    }

    [TestMethod]
    public void Categorize_ReturnsApp_WhenMainWindowIsVisible()
    {
        var input = new ProcessClassificationInput
        {
            HasVisibleMainWindow = true
        };

        Assert.AreEqual(ProcessCategory.App, ProcessCategorizer.Categorize(input));
    }

    [TestMethod]
    public void Categorize_ReturnsWindowsSystem_ForSessionZeroWithoutService()
    {
        var input = new ProcessClassificationInput
        {
            IsSessionZero = true
        };

        Assert.AreEqual(ProcessCategory.WindowsSystem, ProcessCategorizer.Categorize(input));
    }

    [TestMethod]
    public void Categorize_ReturnsBackground_ForUserProcessWithoutWindow()
    {
        var input = new ProcessClassificationInput();

        Assert.AreEqual(ProcessCategory.Background, ProcessCategorizer.Categorize(input));
    }

    [TestMethod]
    public void Categorize_ReturnsUnknown_WhenOnlyDetailsAreDenied()
    {
        var input = new ProcessClassificationInput
        {
            DetailsDenied = true
        };

        Assert.AreEqual(ProcessCategory.Unknown, ProcessCategorizer.Categorize(input));
    }

    [TestMethod]
    public void PropagateAppCategory_PromotesBackgroundDescendantsOfApps()
    {
        var snapshots = new ProcessSnapshot[]
        {
            Snapshot(1, ProcessCategory.App),
            Snapshot(2, ProcessCategory.Background, parentProcessId: 1),
            Snapshot(3, ProcessCategory.Background, parentProcessId: 2),
            Snapshot(4, ProcessCategory.Service),
            Snapshot(5, ProcessCategory.Background, parentProcessId: 4),
            Snapshot(6, ProcessCategory.WindowsSystem, parentProcessId: 1),
            Snapshot(7, ProcessCategory.Background)
        };

        IReadOnlyList<ProcessSnapshot> result = ProcessCategorizer.PropagateAppCategory(snapshots);

        Assert.AreEqual(ProcessCategory.App, result.Single(s => s.ProcessId == 2).Category);
        Assert.AreEqual(ProcessCategory.App, result.Single(s => s.ProcessId == 3).Category);
        Assert.AreEqual(ProcessCategory.Background, result.Single(s => s.ProcessId == 5).Category);
        Assert.AreEqual(ProcessCategory.WindowsSystem, result.Single(s => s.ProcessId == 6).Category);
        Assert.AreEqual(ProcessCategory.Background, result.Single(s => s.ProcessId == 7).Category);
    }

    [TestMethod]
    public void PropagateAppCategory_ToleratesParentIdCycles()
    {
        var snapshots = new ProcessSnapshot[]
        {
            Snapshot(1, ProcessCategory.Background, parentProcessId: 2),
            Snapshot(2, ProcessCategory.Background, parentProcessId: 1)
        };

        IReadOnlyList<ProcessSnapshot> result = ProcessCategorizer.PropagateAppCategory(snapshots);

        Assert.IsTrue(result.All(s => s.Category == ProcessCategory.Background));
    }

    private static ProcessSnapshot Snapshot(int processId, ProcessCategory category, int? parentProcessId = null)
    {
        return new ProcessSnapshot
        {
            ProcessId = processId,
            ProcessName = $"proc{processId}",
            Category = category,
            ParentProcessId = parentProcessId
        };
    }
}
