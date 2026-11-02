using System.Diagnostics;
using System.IO;

namespace EndfieldJiggle.Launcher;

internal static class InputGuardSession
{
    public static async Task<bool> StartAsync(string helper, string efmi, TimeSpan? readyTimeout = null)
    {
        if (!File.Exists(helper)) return false;
        ProcessStartInfo start = new(helper)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(helper)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--background");
        start.ArgumentList.Add(efmi);
        using Process process = Process.Start(start) ?? throw new IOException("无法启动 Alt 输入模块。");
        using CancellationTokenSource timeout = new(readyTimeout ?? TimeSpan.FromSeconds(20));
        try
        {
            string? ready = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (ready != "EJ_INPUT_GUARD_READY")
            {
                string error = await process.StandardError.ReadToEndAsync(timeout.Token);
                throw new IOException("Alt 输入模块未就绪：" + error.Trim());
            }
            return true;
        }
        catch
        {
            // Only stop this helper; the already-running game and EFMI stay active.
            if (!process.HasExited) process.Kill();
            throw;
        }
    }
}
