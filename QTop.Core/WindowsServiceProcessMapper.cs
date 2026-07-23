using System.Runtime.InteropServices;

namespace QTop.Core;

public sealed class WindowsServiceProcessMapper : IServiceProcessMapper
{
    private const uint ScManagerEnumerateService = 0x0004;
    private const int ScEnumProcessInfo = 0;
    private const int ServiceWin32 = 0x00000030;
    private const int ServiceStateAll = 0x00000003;
    private const int MaxEnumAttempts = 4;

    public IReadOnlyDictionary<int, IReadOnlyList<string>> GetServiceNamesByProcessId()
    {
        IntPtr manager = NativeMethods.OpenSCManager(null, null, ScManagerEnumerateService);
        if (manager == IntPtr.Zero)
            return new Dictionary<int, IReadOnlyList<string>>();

        IntPtr buffer = IntPtr.Zero;
        try
        {
            int bufferSize = 0;

            // Services can be installed between the size probe and the data call, so grow with
            // headroom and retry instead of trusting a single bytesNeeded value.
            for (int attempt = 0; attempt < MaxEnumAttempts; attempt++)
            {
                int resumeHandle = 0;
                if (NativeMethods.EnumServicesStatusEx(
                    manager,
                    ScEnumProcessInfo,
                    ServiceWin32,
                    ServiceStateAll,
                    buffer,
                    bufferSize,
                    out int bytesNeeded,
                    out int servicesReturned,
                    ref resumeHandle,
                    null))
                {
                    return ParseServices(buffer, servicesReturned);
                }

                int lastError = Marshal.GetLastWin32Error();
                if (bytesNeeded <= 0 || lastError is not NativeMethods.ErrorMoreData and not NativeMethods.ErrorInsufficientBuffer)
                    return new Dictionary<int, IReadOnlyList<string>>();

                bufferSize = bytesNeeded + 8192;
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);

                buffer = Marshal.AllocHGlobal(bufferSize);
            }

            return new Dictionary<int, IReadOnlyList<string>>();
        }
        catch
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);

            NativeMethods.CloseServiceHandle(manager);
        }
    }

    private static Dictionary<int, IReadOnlyList<string>> ParseServices(IntPtr buffer, int servicesReturned)
    {
        int size = Marshal.SizeOf<EnumServiceStatusProcess>();
        var map = new Dictionary<int, List<string>>();
        for (int index = 0; index < servicesReturned; index++)
        {
            IntPtr current = IntPtr.Add(buffer, index * size);
            var service = Marshal.PtrToStructure<EnumServiceStatusProcess>(current);
            int processId = service.ServiceStatusProcess.ProcessId;
            if (processId <= 0)
                continue;

            if (!map.TryGetValue(processId, out List<string>? names))
            {
                names = [];
                map[processId] = names;
            }

            if (!string.IsNullOrWhiteSpace(service.ServiceName))
                names.Add(service.ServiceName);
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
