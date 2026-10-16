using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EndfieldJiggle.Launcher.Core;

public static class ProcessImagePath
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    public static string? Read(int processId)
    {
        using SafeProcessHandle handle = Open(processId);
        if (handle.IsInvalid) return null;
        StringBuilder path = new(32768);
        int length = path.Capacity;
        if (QueryFullProcessImageNameW(handle, 0, path, ref length))
        {
            if (length == 0) throw new IOException("Windows 返回了空的游戏进程路径，不能核验目标。");
            return path.ToString();
        }
        int error = Marshal.GetLastWin32Error();
        if (GetExitCodeProcess(handle, out uint exitCode) && exitCode != StillActive) return null;
        throw new Win32Exception(error, "无法核验游戏路径（QueryFullProcessImageNameW）。未跳过目标路径检查。");
    }

    public static bool HasExited(int processId)
    {
        using SafeProcessHandle handle = Open(processId);
        if (handle.IsInvalid) return true;
        if (!GetExitCodeProcess(handle, out uint exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法查询游戏进程状态（GetExitCodeProcess）。");
        return exitCode != StillActive;
    }

    private static SafeProcessHandle Open(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        // These metadata queries need neither module enumeration nor VM read access.
        SafeProcessHandle handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error();
        if (error == 87) return handle; // A process may exit between enumeration and querying.
        handle.Dispose();
        throw new Win32Exception(error, "无法以有限查询权限核验游戏进程（OpenProcess）。");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process,
        uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
