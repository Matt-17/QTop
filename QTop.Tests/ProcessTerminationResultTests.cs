using System.ComponentModel;
using QTop.Core;

namespace QTop.Tests;

[TestClass]
public sealed class ProcessTerminationResultTests
{
    [TestMethod]
    public void FromException_MapsWin32AccessDenied()
    {
        ProcessTerminationResult result = ProcessTerminationResult.FromException(new Win32Exception(5), "Kill PID 12");

        Assert.AreEqual(ProcessTerminationOutcome.AccessDenied, result.Outcome);
        StringAssert.Contains(result.Message, "access denied");
    }

    [TestMethod]
    public void FromException_MapsArgumentExceptionToNotRunning()
    {
        ProcessTerminationResult result = ProcessTerminationResult.FromException(new ArgumentException("missing"), "Kill PID 12");

        Assert.AreEqual(ProcessTerminationOutcome.NotRunning, result.Outcome);
    }

    [TestMethod]
    public void FromException_MapsUnknownWin32FailureWithCode()
    {
        ProcessTerminationResult result = ProcessTerminationResult.FromException(new Win32Exception(87), "Kill PID 12");

        Assert.AreEqual(ProcessTerminationOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.Message, "Win32 87");
    }
}
