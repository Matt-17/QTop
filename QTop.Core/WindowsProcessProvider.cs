using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace QTop.Core;

public sealed class WindowsProcessProvider : IProcessProvider
{
    private static readonly HashSet<string> KnownSystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle",
        "System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "svchost",
        "fontdrvhost",
        "Memory Compression",
        "Secure System"
    };

    private static readonly ConcurrentDictionary<string, (string? Company, string? Product, string? Description)> VersionInfoCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string?> AccountNamesBySid =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceProcessMapper _serviceProcessMapper;
    private readonly Dictionary<int, CpuSample> _cpuSamples = new();

    public WindowsProcessProvider(IServiceProcessMapper? serviceProcessMapper = null)
    {
        _serviceProcessMapper = serviceProcessMapper ?? new WindowsServiceProcessMapper();
    }

    public IReadOnlyList<ProcessSnapshot> GetProcesses()
    {
        IReadOnlyDictionary<int, IReadOnlyList<string>> serviceMap = _serviceProcessMapper.GetServiceNamesByProcessId();
        IReadOnlyDictionary<int, int> parentMap = WindowsProcessUtilities.GetParentProcessIds();
        var snapshots = new List<ProcessSnapshot>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                snapshots.Add(CreateSnapshot(process, serviceMap, parentMap));
            }
        }

        PruneCpuSamples(snapshots);
        return ProcessCategorizer.PropagateAppCategory(snapshots);
    }

    private void PruneCpuSamples(List<ProcessSnapshot> snapshots)
    {
        var seen = new HashSet<int>(snapshots.Count);
        foreach (ProcessSnapshot snapshot in snapshots)
            seen.Add(snapshot.ProcessId);

        List<int>? stale = null;
        foreach (int processId in _cpuSamples.Keys)
        {
            if (!seen.Contains(processId))
                (stale ??= []).Add(processId);
        }

        if (stale is not null)
        {
            foreach (int processId in stale)
                _cpuSamples.Remove(processId);
        }
    }

    public ProcessSnapshot? TryGetProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            IReadOnlyDictionary<int, IReadOnlyList<string>> serviceMap = _serviceProcessMapper.GetServiceNamesByProcessId();
            IReadOnlyDictionary<int, int> parentMap = WindowsProcessUtilities.GetParentProcessIds();
            return CreateSnapshot(process, serviceMap, parentMap);
        }
        catch (Exception exception) when (IsExpectedProcessException(exception))
        {
            return null;
        }
    }

    private ProcessSnapshot CreateSnapshot(
        Process process,
        IReadOnlyDictionary<int, IReadOnlyList<string>> serviceMap,
        IReadOnlyDictionary<int, int> parentMap)
    {
        int processId = process.Id;
        string processName = TryRead(() => process.ProcessName) ?? $"PID {processId}";
        int? parentProcessId = parentMap.TryGetValue(processId, out int parent) && parent > 0 ? parent : null;
        int sessionId = TryRead(() => (int?)process.SessionId) ?? -1;
        IntPtr mainWindowHandle = TryRead(() => process.MainWindowHandle);
        string mainWindowTitle = TryRead(() => process.MainWindowTitle) ?? string.Empty;
        bool hasVisibleMainWindow = mainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(mainWindowTitle);

        string? executablePath;
        string? commandLine;
        string? integrityLevel;
        string? userName;
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        bool canQuery = handle != IntPtr.Zero;
        try
        {
            executablePath = TryReadExecutablePath(process, handle);
            commandLine = TryReadCommandLine(handle);
            (integrityLevel, userName) = TryReadTokenDetails(handle);
        }
        finally
        {
            if (handle != IntPtr.Zero)
                NativeMethods.CloseHandle(handle);
        }

        DateTime? startTime = canQuery ? TryRead(() => (DateTime?)process.StartTime) : null;
        TimeSpan? processorTime = canQuery ? TryRead(() => (TimeSpan?)process.TotalProcessorTime) : null;
        double? cpuPercent = ComputeCpuPercent(processId, startTime, processorTime);
        long? workingSet = TryRead(() => (long?)process.WorkingSet64);
        int? threadCount = TryRead(() => (int?)process.Threads.Count);
        int? handleCount = TryRead(() => (int?)process.HandleCount);
        IReadOnlyList<string> serviceNames = serviceMap.TryGetValue(processId, out IReadOnlyList<string>? names)
            ? names
            : Array.Empty<string>();

        (string? companyName, string? productName, string? description) = GetVersionInfo(executablePath);
        bool detailsDenied = executablePath is null && startTime is null && processorTime is null && processId > 4;
        bool isKnownSystemProcess = KnownSystemProcessNames.Contains(processName);
        bool isWindowsOwned = IsWindowsOwned(executablePath);

        var classification = new ProcessClassificationInput
        {
            HasVisibleMainWindow = hasVisibleMainWindow,
            IsService = serviceNames.Count > 0,
            IsSessionZero = sessionId == 0,
            IsWindowsOwned = isWindowsOwned,
            IsKnownSystemProcess = isKnownSystemProcess,
            DetailsDenied = detailsDenied
        };

        ProcessCategory category = ProcessCategorizer.Categorize(classification);

        return new ProcessSnapshot
        {
            ProcessId = processId,
            ProcessName = processName,
            ParentProcessId = parentProcessId,
            Category = category,
            ExecutablePath = executablePath,
            CommandLine = commandLine,
            CompanyName = companyName,
            ProductName = productName,
            Description = description,
            StartTime = startTime is null ? null : new DateTimeOffset(startTime.Value),
            TotalProcessorTime = processorTime,
            CpuPercent = cpuPercent,
            WorkingSetBytes = workingSet,
            ThreadCount = threadCount,
            HandleCount = handleCount,
            SessionId = sessionId,
            UserName = userName,
            ServiceNames = serviceNames,
            HasMainWindow = hasVisibleMainWindow,
            IsProtectedOrSystem = category == ProcessCategory.WindowsSystem || isKnownSystemProcess || detailsDenied,
            IsCurrentProcess = processId == Environment.ProcessId,
            CanAccessDetails = !detailsDenied,
            IntegrityLevel = integrityLevel
        };
    }

    private double? ComputeCpuPercent(int processId, DateTime? startTime, TimeSpan? processorTime)
    {
        if (processorTime is not TimeSpan current)
            return null;

        long now = Stopwatch.GetTimestamp();
        long startTimeTicks = startTime?.Ticks ?? 0;
        double? percent = null;
        if (_cpuSamples.TryGetValue(processId, out CpuSample previous) &&
            previous.StartTimeTicks == startTimeTicks &&
            current >= previous.ProcessorTime)
        {
            double elapsedSeconds = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
            if (elapsedSeconds > 0.2)
            {
                double busySeconds = (current - previous.ProcessorTime).TotalSeconds;
                percent = Math.Clamp(busySeconds / (elapsedSeconds * Environment.ProcessorCount) * 100d, 0d, 100d);
            }
        }

        _cpuSamples[processId] = new CpuSample(current, now, startTimeTicks);
        return percent;
    }

    private readonly record struct CpuSample(TimeSpan ProcessorTime, long Timestamp, long StartTimeTicks);

    private static string? TryReadExecutablePath(Process process, IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            string? path = QueryImagePath(handle, 1024);
            if (path is null && Marshal.GetLastWin32Error() == NativeMethods.ErrorInsufficientBuffer)
                path = QueryImagePath(handle, 32768);

            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        try
        {
            string? modulePath = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(modulePath) ? null : modulePath;
        }
        catch (Exception exception) when (IsExpectedProcessException(exception))
        {
            return null;
        }
    }

    private static string? QueryImagePath(IntPtr handle, int capacity)
    {
        var builder = new StringBuilder(capacity);
        int size = builder.Capacity;
        return NativeMethods.QueryFullProcessImageName(handle, 0, builder, ref size)
            ? builder.ToString()
            : null;
    }

    private static string? TryReadCommandLine(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return null;

        NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out int length);
        if (length <= 0)
            return null;

        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, buffer, length, out _) != 0)
                return null;

            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            if (value.Buffer == IntPtr.Zero || value.Length == 0)
                return null;

            string? commandLine = Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
            return string.IsNullOrWhiteSpace(commandLine) ? null : commandLine;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string? IntegrityLevel, string? UserName) TryReadTokenDetails(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero)
            return (null, null);

        if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TokenQuery, out IntPtr tokenHandle))
            return (null, null);

        try
        {
            return (TryReadIntegrityLevel(tokenHandle), TryReadUserName(tokenHandle));
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }

    private static string? TryReadIntegrityLevel(IntPtr tokenHandle)
    {
        IntPtr buffer = QueryTokenInformation(tokenHandle, NativeMethods.TokenIntegrityLevel);
        if (buffer == IntPtr.Zero)
            return null;

        try
        {
            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            IntPtr subAuthorityCountPointer = NativeMethods.GetSidSubAuthorityCount(label.Label.Sid);
            if (subAuthorityCountPointer == IntPtr.Zero)
                return null;

            byte subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
            if (subAuthorityCount == 0)
                return null;

            IntPtr ridPointer = NativeMethods.GetSidSubAuthority(label.Label.Sid, subAuthorityCount - 1u);
            if (ridPointer == IntPtr.Zero)
                return null;

            int rid = Marshal.ReadInt32(ridPointer);
            return rid switch
            {
                < 0x1000 => "Untrusted",
                < 0x2000 => "Low",
                < 0x3000 => "Medium",
                < 0x4000 => "High",
                < 0x5000 => "System",
                _ => "Protected"
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? TryReadUserName(IntPtr tokenHandle)
    {
        IntPtr buffer = QueryTokenInformation(tokenHandle, NativeMethods.TokenUser);
        if (buffer == IntPtr.Zero)
            return null;

        try
        {
            var user = Marshal.PtrToStructure<SidAndAttributes>(buffer);
            if (user.Sid == IntPtr.Zero)
                return null;

            if (!NativeMethods.ConvertSidToStringSid(user.Sid, out IntPtr sidStringPointer))
                return null;

            string? sidString;
            try
            {
                sidString = Marshal.PtrToStringUni(sidStringPointer);
            }
            finally
            {
                NativeMethods.LocalFree(sidStringPointer);
            }

            if (string.IsNullOrWhiteSpace(sidString))
                return null;

            return AccountNamesBySid.GetOrAdd(sidString, _ => LookupAccountName(user.Sid));
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? LookupAccountName(IntPtr sid)
    {
        var name = new StringBuilder(256);
        var domain = new StringBuilder(256);
        int nameLength = name.Capacity;
        int domainLength = domain.Capacity;
        if (!NativeMethods.LookupAccountSid(null, sid, name, ref nameLength, domain, ref domainLength, out _))
            return null;

        return domain.Length > 0 ? $"{domain}\\{name}" : name.ToString();
    }

    private static IntPtr QueryTokenInformation(IntPtr tokenHandle, int informationClass)
    {
        NativeMethods.GetTokenInformation(tokenHandle, informationClass, IntPtr.Zero, 0, out int length);
        if (length <= 0)
            return IntPtr.Zero;

        IntPtr buffer = Marshal.AllocHGlobal(length);
        if (NativeMethods.GetTokenInformation(tokenHandle, informationClass, buffer, length, out _))
            return buffer;

        Marshal.FreeHGlobal(buffer);
        return IntPtr.Zero;
    }

    private static (string? Company, string? Product, string? Description) GetVersionInfo(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return (null, null, null);

        return VersionInfoCache.GetOrAdd(executablePath, static path =>
        {
            try
            {
                if (!File.Exists(path))
                    return (null, null, null);

                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return (Clean(info.CompanyName), Clean(info.ProductName), Clean(info.FileDescription));
            }
            catch
            {
                return (null, null, null);
            }
        });
    }

    private static bool IsWindowsOwned(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrWhiteSpace(windowsDirectory) &&
               executablePath.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static T? TryRead<T>(Func<T> reader)
    {
        try
        {
            return reader();
        }
        catch (Exception exception) when (IsExpectedProcessException(exception))
        {
            return default;
        }
    }

    private static bool IsExpectedProcessException(Exception exception)
    {
        return exception is Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException;
    }
}
