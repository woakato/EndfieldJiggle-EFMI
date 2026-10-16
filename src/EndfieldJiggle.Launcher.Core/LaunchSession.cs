using System.Diagnostics;
using System.ComponentModel;

namespace EndfieldJiggle.Launcher.Core;

public sealed record GameProcess(int Id, string Executable);

public interface IManualLaunchRuntime
{
    GameProcess[] FindGames();
    bool OtherLoaderRunning();
    bool HasExited(int processId);
    void Load(int processId);
    bool WaitOnce();
}

public static class LaunchSession
{
    public static string DescribeWindowsFailure(Win32Exception error) => error.NativeErrorCode switch
    {
        740 or 5 => "Windows 拒绝访问。请确认本程序与官方启动器的运行权限一致；不要关闭安全防护。",
        1223 => "已取消 Windows 授权。",
        2 or 3 => "找不到所选游戏或运行库，请检查路径并完整解压本包。",
        299 => "无法完整查询游戏进程信息（299），本次停止加载。请发送 Logs/launcher.log，不要关闭安全防护。",
        _ => $"Windows 加载失败（错误码 {error.NativeErrorCode}）。详细信息见 Logs/launcher.log。",
    };

    public static void Start(StandalonePackage package, string gamePath,
        Action<string> status, CancellationToken token, Action<string>? trace = null)
    {
        token.ThrowIfCancellationRequested();
        using Mutex session = new(false, @"Local\EndfieldJiggleStandaloneLaunch");
        bool acquired;
        try { acquired = session.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("另一个独立启动器正在等待游戏，请先取消它的等待。");
        try
        {
            token.ThrowIfCancellationRequested();
            package.Validate();
            GameLocation game = GameLocationStore.Resolve(gamePath);
            using NativeManualLaunchRuntime runtime = new(package, game);
            RunManual(game, runtime, status, token, trace);
        }
        finally { session.ReleaseMutex(); }
    }

    public static void RunManual(GameLocation game, IManualLaunchRuntime runtime,
        Action<string> status, CancellationToken token, Action<string>? trace = null,
        TimeSpan? processTimeout = null, TimeSpan? loadTimeout = null)
    {
        token.ThrowIfCancellationRequested();
        if (runtime.OtherLoaderRunning())
            throw new IOException("请先退出另一套 XXMI，避免同一次会话混用加载器；官方游戏启动器可以保持打开。");
        if (runtime.FindGames().Length != 0)
            throw new IOException("请先退出游戏，再点击“等待官方启动”，然后通过官方启动器重新启动游戏。");

        trace?.Invoke("phase=waiting-for-official-launch");
        status("等待官方启动（最多 5 分钟）。请在官方启动器确认 DirectX 11，再点击开始游戏。");
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.Elapsed < (processTimeout ?? TimeSpan.FromMinutes(5)))
        {
            token.ThrowIfCancellationRequested();
            if (runtime.OtherLoaderRunning())
                throw new IOException("检测到另一套 XXMI，本次等待已停止。请只保留一个加载器。");
            GameProcess[] games = runtime.FindGames();
            if (games.Length > 1)
                throw new IOException("检测到多个游戏进程，无法确定目标；请退出游戏后重试。");
            if (games.Length == 1)
            {
                GameProcess process = games[0];
                if (!Path.GetFullPath(process.Executable).Equals(game.Executable, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("官方启动器启动了不同目录的游戏。请退出游戏，重新选择实际的 Endfield.exe。");
                trace?.Invoke("phase=game-detected");
                token.ThrowIfCancellationRequested();
                if (runtime.HasExited(process.Id)) throw new IOException("游戏在加载 EFMI 前退出。");
                status("已检测到所选游戏，等待 EFMI 加载…");
                trace?.Invoke("phase=loading-efmi");
                runtime.Load(process.Id);
                timer.Restart();
                while (timer.Elapsed < (loadTimeout ?? TimeSpan.FromSeconds(15)))
                {
                    token.ThrowIfCancellationRequested();
                    if (runtime.HasExited(process.Id))
                        throw new IOException("游戏在 EFMI 确认加载前退出。请先确认官方启动器能正常启动游戏。");
                    if (runtime.WaitOnce())
                    {
                        token.ThrowIfCancellationRequested();
                        if (runtime.HasExited(process.Id))
                            throw new IOException("游戏在 EFMI 确认时退出，不能确认本次加载成功。");
                        trace?.Invoke("phase=efmi-loaded");
                        status("EFMI 已加载，可以关闭此窗口；触摸效果仍需在游戏内确认。");
                        return;
                    }
                    token.WaitHandle.WaitOne(100);
                }
                throw new TimeoutException("等待 EFMI 超时。请退出游戏，确认官方启动器使用 DirectX 11，且运行权限一致。");
            }
            token.WaitHandle.WaitOne(100);
        }
        throw new TimeoutException("5 分钟内没有检测到游戏。请再次点击“等待官方启动”，再从官方启动器开始游戏。");
    }

    private sealed class NativeManualLaunchRuntime(StandalonePackage package, GameLocation game) : IManualLaunchRuntime, IDisposable
    {
        private OfficialInjector? injector;

        public GameProcess[] FindGames()
        {
            List<GameProcess> result = [];
            Process[] processes = Process.GetProcessesByName("Endfield");
            try
            {
                foreach (Process process in processes)
                {
                    string? path = ProcessImagePath.Read(process.Id);
                    if (path is not null) result.Add(new(process.Id, path));
                }
            }
            finally { foreach (Process process in processes) process.Dispose(); }
            return result.ToArray();
        }

        public bool OtherLoaderRunning()
        {
            Process[] processes = Process.GetProcessesByName("XXMI Launcher");
            try { return processes.Length != 0; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        public bool HasExited(int processId) => ProcessImagePath.HasExited(processId);

        public void Load(int processId)
        {
            // Recheck the selected process before calling the unchanged upstream loader.
            GameProcess? current = FindGames().SingleOrDefault(process => process.Id == processId);
            if (current is null) throw new IOException("游戏进程已退出，本次不再尝试加载。");
            if (!Path.GetFullPath(current.Executable).Equals(game.Executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("游戏进程路径发生变化，本次不加载 EFMI。");
            injector = new(package.LoaderPath);
            injector.Load(processId, package.D3dPath);
        }

        public bool WaitOnce() => injector?.WaitOnce(package.D3dPath) == true;
        public void Dispose() => injector?.Dispose();
    }
}
