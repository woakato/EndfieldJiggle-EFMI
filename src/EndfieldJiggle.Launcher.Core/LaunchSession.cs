using System.Diagnostics;

namespace EndfieldJiggle.Launcher.Core;

public static class LaunchSession
{
    public static ProcessStartInfo CreateStartInfo(GameLocation game)
    {
        ProcessStartInfo start = new(game.Executable)
        {
            WorkingDirectory = game.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-force-d3d11");
        return start;
    }

    public static void Start(StandalonePackage package, string gamePath,
        Action<string> status, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using Mutex session = new(false, @"Local\EndfieldJiggleStandaloneLaunch");
        bool acquired;
        try { acquired = session.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("另一个独立启动器正在启动游戏，请等待它完成。");
        try { StartOwned(package, gamePath, status, token); }
        finally { session.ReleaseMutex(); }
    }

    private static void StartOwned(StandalonePackage package, string gamePath,
        Action<string> status, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        package.Validate();
        GameLocation game = GameLocationStore.Resolve(gamePath);
        foreach (string name in new[] { "Endfield", "XXMI Launcher" })
        {
            Process[] processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length != 0)
                    throw new IOException("请先退出正在运行的游戏与 XXMI，再从独立包启动。");
            }
            finally { foreach (Process running in processes) running.Dispose(); }
        }
        using OfficialInjector injector = new(package.LoaderPath);
        token.ThrowIfCancellationRequested();
        using Process process = Process.Start(CreateStartInfo(game))
            ?? throw new IOException("Windows 未能启动游戏。");
        status("游戏已启动，等待 EFMI 加载…");
        token.ThrowIfCancellationRequested();
        injector.Load(process.Id, package.D3dPath);
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            token.ThrowIfCancellationRequested();
            if (injector.WaitOnce(package.D3dPath))
            {
                status("EFMI 已加载，可以关闭此窗口。");
                return;
            }
            if (process.HasExited)
                throw new IOException("游戏在 EFMI 加载前退出。请先用官方游戏启动器更新并确认游戏可以正常启动。");
        }
        throw new TimeoutException("等待 EFMI 超时。请退出游戏，确认选择了正确的 Endfield.exe 后重试。");
    }
}
