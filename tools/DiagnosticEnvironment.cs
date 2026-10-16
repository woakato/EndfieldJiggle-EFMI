using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class EndfieldDiagnosticEnvironment
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software,
        uint flags, [In] uint[] levels, uint count, uint sdk, out IntPtr device,
        out uint selectedLevel, out IntPtr context);

    private static object ProbeDevice(int driver)
    {
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
        uint level = 0;
        int hr = D3D11CreateDevice(IntPtr.Zero, driver, IntPtr.Zero, 0,
            new uint[] { 0xb000, 0xa100, 0xa000 }, 3, 7, out device, out level, out context);
        try
        {
            return new Dictionary<string, object> {
                { "hresult", "0x" + unchecked((uint)hr).ToString("X8") },
                { "created", hr >= 0 }, { "featureLevel", "0x" + level.ToString("X4") },
                { "supportsFeatureLevel11", hr >= 0 && level >= 0xb000 },
                { "note", "Default hardware adapter or WARP probe; not proof of the game's active GPU or touch." }
            };
        }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (device != IntPtr.Zero) Marshal.Release(device);
        }
    }

    public static object Inspect(string loaderPath)
    {
        var result = new Dictionary<string, object>();
        if (!Environment.Is64BitProcess)
        {
            result["error"] = "Use the x64 diagnostic collector.";
            return result;
        }
        // Resolve imports but never call Inject/WaitForInjection or start a process.
        IntPtr library = LoadLibraryEx(loaderPath, IntPtr.Zero, 0x1100);
        int error = library == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        try
        {
            result["loaderLibraryLoads"] = library != IntPtr.Zero;
            result["loaderWindowsError"] = error;
            result["injectExportPresent"] = library != IntPtr.Zero && GetProcAddress(library, "Inject") != IntPtr.Zero;
            result["waitExportPresent"] = library != IntPtr.Zero && GetProcAddress(library, "WaitForInjection") != IntPtr.Zero;
        }
        finally { if (library != IntPtr.Zero) FreeLibrary(library); }
        result["hardwareD3d11"] = ProbeDevice(1);
        result["warpD3d11"] = ProbeDevice(5);
        result["gameStarted"] = false;
        result["injectorCalled"] = false;
        return result;
    }
}
