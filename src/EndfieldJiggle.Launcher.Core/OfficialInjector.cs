using System.Runtime.InteropServices;

namespace EndfieldJiggle.Launcher.Core;

// These are the public InjectorLib exports used by XXMI Launcher.
public sealed class OfficialInjector : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int Inject(uint processId, [MarshalAs(UnmanagedType.LPWStr)] string path,
        int timeoutSeconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int WaitForInjection([MarshalAs(UnmanagedType.LPWStr)] string path,
        [MarshalAs(UnmanagedType.LPWStr)] string processName, int timeoutSeconds);
    private readonly nint library;
    private readonly Inject inject;
    private readonly WaitForInjection wait;
    private bool disposed;

    public OfficialInjector(string loaderPath)
    {
        library = NativeLibrary.Load(Path.GetFullPath(loaderPath));
        try
        {
            inject = Marshal.GetDelegateForFunctionPointer<Inject>(
                NativeLibrary.GetExport(library, "Inject"));
            wait = Marshal.GetDelegateForFunctionPointer<WaitForInjection>(
                NativeLibrary.GetExport(library, "WaitForInjection"));
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    public void Load(int processId, string d3dPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int code = inject(checked((uint)processId), Path.GetFullPath(d3dPath), 10);
        if (code != 0)
            throw new IOException(code switch
            {
                100 => "游戏进程已退出或 Windows 拒绝访问，请检查游戏与启动器的运行权限。",
                110 => "官方 d3d11.dll 路径无效，请重新解压完整包。",
                200 or 300 or 400 => "Windows 未能加载 EFMI，请检查游戏与启动器的运行权限是否一致。",
                500 => "加载 EFMI 超时，请退出游戏后重试。",
                600 => "游戏未能加载官方 d3d11.dll，请退出游戏后检查 EFMI 日志。",
                _ => "官方加载器返回错误码：" + code,
            });
    }

    public bool WaitOnce(string d3dPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return wait(Path.GetFullPath(d3dPath), "Endfield.exe", 1) == 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        NativeLibrary.Free(library);
    }
}
