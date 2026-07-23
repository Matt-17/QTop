using QTop.Core;

namespace QTop.Tests;

[TestClass]
public sealed class ProcessFilterTests
{
    [TestMethod]
    public void Matches_FiltersByCategory()
    {
        ProcessSnapshot app = Snapshot("notepad", 100, ProcessCategory.App);

        Assert.IsTrue(ProcessListFilters.Matches(app, null, ProcessCategory.App, hideProtectedSystem: false));
        Assert.IsFalse(ProcessListFilters.Matches(app, null, ProcessCategory.Service, hideProtectedSystem: false));
    }

    [TestMethod]
    public void Matches_SearchesNamePidPathAndServices()
    {
        ProcessSnapshot service = Snapshot("svchost", 220, ProcessCategory.Service) with
        {
            ExecutablePath = @"C:\Windows\System32\svchost.exe",
            ServiceNames = ["Dnscache"]
        };

        Assert.IsTrue(ProcessListFilters.Matches(service, "dns", null, hideProtectedSystem: false));
        Assert.IsTrue(ProcessListFilters.Matches(service, "220", null, hideProtectedSystem: false));
        Assert.IsTrue(ProcessListFilters.Matches(service, "system32", null, hideProtectedSystem: false));
        Assert.IsFalse(ProcessListFilters.Matches(service, "missing", null, hideProtectedSystem: false));
    }

    [TestMethod]
    public void Matches_HidesProtectedSystem_WhenRequested()
    {
        ProcessSnapshot system = Snapshot("System", 4, ProcessCategory.WindowsSystem) with
        {
            IsProtectedOrSystem = true
        };

        Assert.IsFalse(ProcessListFilters.Matches(system, null, null, hideProtectedSystem: true));
        Assert.IsTrue(ProcessListFilters.Matches(system, null, null, hideProtectedSystem: false));
    }

    private static ProcessSnapshot Snapshot(string name, int pid, ProcessCategory category)
    {
        return new ProcessSnapshot
        {
            ProcessId = pid,
            ProcessName = name,
            Category = category
        };
    }
}
