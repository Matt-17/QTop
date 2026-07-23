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
}
