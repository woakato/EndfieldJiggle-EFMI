using System.Diagnostics;
using EndfieldJiggle.Launcher;

if (args.Length != 1) throw new ArgumentException("Pass the native input_companion_fixture.exe.");
string helper = Path.GetFullPath(args[0]);
string root = Path.GetFullPath(Path.Combine("reports", "input-guard", "companion-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool result, string name)
{
    if (!result) throw new Exception("FAIL: " + name);
    ++checks;
}
Check(!await InputGuardSession.StartAsync(Path.Combine(root, "missing.exe"), root),
    "Older standalone packages without the optional helper remain supported");
foreach (string mode in new[] { "ready", "failed", "timeout" })
{
    string directory = Path.Combine(root, mode);
    Directory.CreateDirectory(directory);
    Exception? failure = null;
    bool started = false;
    try { started = await InputGuardSession.StartAsync(helper, directory, TimeSpan.FromSeconds(1)); }
    catch (Exception error) when (error is IOException or OperationCanceledException) { failure = error; }
    if (mode == "ready")
    {
        Check(started && failure is null, "Launcher recognizes the helper's readiness handshake");
        using Process child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(directory, "pid.txt"))));
        Check(!child.HasExited, "Ready companion survives disposal of the launcher's process handle");
        File.WriteAllText(Path.Combine(directory, "stop"), "");
        Check(child.WaitForExit(3000), "Fixture companion exits cleanly after its session");
    }
    else
    {
        Check(failure is not null && !started, "Launcher rejects " + mode + " initialization");
        if (mode == "failed")
            Check(failure!.Message.Contains("Fixture rejected", StringComparison.Ordinal),
                "Initialization diagnostics reach the launcher");
        int pid = int.Parse(File.ReadAllText(Path.Combine(directory, "pid.txt")));
        bool exited;
        try
        {
            using Process child = Process.GetProcessById(pid);
            exited = child.WaitForExit(3000);
        }
        catch (ArgumentException) { exited = true; }
        Check(exited, "Failed or timed-out fixture leaves no background helper");
    }
}
Console.WriteLine($"PASS: {checks} launcher-companion handshake and lifecycle checks; no game process opened.");
