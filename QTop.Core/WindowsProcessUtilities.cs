using System.Runtime.InteropServices;

namespace QTop.Core;

internal static class WindowsProcessUtilities
{
    public static IReadOnlyDictionary<int, int> GetParentProcessIds()
    {
        var parents = new Dictionary<int, int>();
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapProcess, 0);
        if (snapshot == NativeMethods.InvalidHandleValue)
            return parents;

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };

            if (!NativeMethods.Process32First(snapshot, ref entry))
                return parents;

            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        return parents;
    }
}
