using System.Security.Cryptography;
using System.ComponentModel;
using System.Text.Json;
using EndfieldJiggle.Launcher.Core;
using EndfieldJiggle.Configurator.Core;

string output = Path.GetFullPath(args.Length > 0 ? args[0] : "reports/standalone/tests");
string fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
List<string> checks = [];
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException("FAIL: " + name);
    checks.Add(name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
    {
        checks.Add(name);
        return;
    }
    throw new InvalidOperationException("FAIL: expected rejection: " + name);
}

string gameDirectory = Path.Combine(fixture, "游戏", "EndField Game");
Directory.CreateDirectory(Path.Combine(gameDirectory, "Endfield_Data"));
File.WriteAllBytes(Path.Combine(gameDirectory, "GameAssembly.dll"), [1]);
byte[] executable = new byte[256];
executable[0] = (byte)'M';
executable[1] = (byte)'Z';
BitConverter.GetBytes(128).CopyTo(executable, 0x3c);
BitConverter.GetBytes(0x00004550u).CopyTo(executable, 128);
BitConverter.GetBytes((ushort)0x8664).CopyTo(executable, 132);
string gamePath = Path.Combine(gameDirectory, "Endfield.exe");
File.WriteAllBytes(gamePath, executable);
GameLocation game = GameLocationStore.Resolve(gameDirectory);
Check(game.Executable == gamePath && game.Directory == gameDirectory,
    "A Unicode game directory resolves the validated x64 executable");
Check(GameLocationStore.Resolve('"' + gamePath + '"') == game, "Quoted file paths resolve");
Reject(() => GameLocationStore.Resolve(Path.Combine(gameDirectory, "launcher.exe")),
    "The launcher cannot be mistaken for the game");
Reject(() => GameLocationStore.Resolve(Path.Combine(fixture, "missing")), "Missing game is rejected");
File.Move(Path.Combine(gameDirectory, "GameAssembly.dll"), Path.Combine(gameDirectory, "GameAssembly.backup"));
Reject(() => GameLocationStore.Resolve(gamePath), "An incomplete game directory is rejected");
File.Move(Path.Combine(gameDirectory, "GameAssembly.backup"), Path.Combine(gameDirectory, "GameAssembly.dll"));
executable[133] = 1;
File.WriteAllBytes(gamePath, executable);
Reject(() => GameLocationStore.Resolve(gamePath), "A non-x64 executable is rejected");
executable[133] = 0x86;
File.WriteAllBytes(gamePath, executable);
Check(GameLocationStore.Discover([Path.Combine(fixture, "游戏")]) == game,
    "Bounded discovery finds the common game subdirectory");
Check(GameLocationStore.Discover([Path.Combine(fixture, "absent")]) is null, "Discovery returns no false game");

string bundle = Path.Combine(fixture, "独立包");
Directory.CreateDirectory(bundle);
GameLocationStore store = new(bundle);
Check(store.Read() is null, "First run has no invented game path");
store.Save(gamePath);
Check(store.Read() == gamePath, "Game selection persists without touching the game");
string settingsPath = Path.Combine(bundle, "launch-settings.json");
byte[] settingsBefore = File.ReadAllBytes(settingsPath);
Reject(() => store.Save(Path.Combine(fixture, "missing")), "Invalid selection cannot overwrite saved settings");
Check(File.ReadAllBytes(settingsPath).SequenceEqual(settingsBefore), "Failed saves preserve the last selection");
File.WriteAllText(settingsPath, "{\"schema\":2,\"gameExecutable\":\"wrong\"}");
Reject(() => store.Read(), "Unknown settings schema is rejected");
store.Save(gamePath);
Check(File.ReadAllBytes(gamePath).SequenceEqual(executable), "Selection and settings leave game EXE bytes unchanged");
Win32Exception elevation = new(740, "Raw English error with a private path");
Check(LaunchSession.DescribeWindowsFailure(elevation).Contains("权限一致") &&
    !LaunchSession.DescribeWindowsFailure(elevation).Contains("private path"),
    "Access errors advise matching official launcher privileges without requesting an elevation retry");
Check(LaunchSession.DescribeWindowsFailure(new Win32Exception(1223)).Contains("已取消") &&
    LaunchSession.DescribeWindowsFailure(new Win32Exception(999)).Contains("999"),
    "Cancelled authorization and unrecognized errors have useful user messages");
Check(LaunchSession.DescribeWindowsFailure(new Win32Exception(299)).Contains("进程信息"),
    "Partial-copy errors identify the process-query stage instead of claiming a shader failure");
Check(string.Equals(ProcessImagePath.Read(Environment.ProcessId), Environment.ProcessPath,
    StringComparison.OrdinalIgnoreCase), "Limited-query Win32 API retrieves the current test process image");
Check(!ProcessImagePath.HasExited(Environment.ProcessId),
    "Limited-query status detects the running test process without module enumeration");
Check(ProcessImagePath.Read(int.MaxValue) is null && ProcessImagePath.HasExited(int.MaxValue),
    "A missing process between enumeration and querying is treated as exited");
Reject(() => ProcessImagePath.Read(0), "A zero process ID cannot query a different target");
Reject(() => ProcessImagePath.HasExited(-1), "A negative process ID cannot query a different target");

GameProcess target = new(123, gamePath);
List<string> phases = [];
FakeManualRuntime manual = new() { Games = call => call < 3 ? [] : [target] };
LaunchSession.RunManual(game, manual, _ => { }, CancellationToken.None, phases.Add);
Check(manual.LoadCalls == 1 && manual.LoadedProcess == 123 && manual.WaitCalls == 1,
    "Manual mode waits for the selected game and invokes the existing loader exactly once");
Check(phases.SequenceEqual(["phase=waiting-for-official-launch", "phase=game-detected",
        "phase=loading-efmi", "phase=efmi-loaded"]),
    "Manual launch reports ordered diagnostic phases without claiming touch success");
FakeManualRuntime alreadyRunning = new() { Games = _ => [target] };
Reject(() => LaunchSession.RunManual(game, alreadyRunning, _ => { }, CancellationToken.None),
    "A game that was already running is rejected before loading");
Check(alreadyRunning.LoadCalls == 0, "An existing game is never retroactively loaded");
FakeManualRuntime wrongPath = new()
    { Games = call => call == 1 ? [] : [target with { Executable = Path.Combine(fixture, "other", "Endfield.exe") }] };
Reject(() => LaunchSession.RunManual(game, wrongPath, _ => { }, CancellationToken.None),
    "A different game installation cannot receive the bundle");
Check(wrongPath.LoadCalls == 0, "Mismatched paths stop before native loading");
FakeManualRuntime multiple = new() { Games = call => call == 1 ? [] : [target, target with { Id = 124 }] };
Reject(() => LaunchSession.RunManual(game, multiple, _ => { }, CancellationToken.None),
    "Ambiguous multiple game processes are rejected");
FakeManualRuntime otherLoader = new() { Loader = () => true };
Reject(() => LaunchSession.RunManual(game, otherLoader, _ => { }, CancellationToken.None),
    "Another XXMI prevents waiting and loading");
int loaderQueries = 0;
FakeManualRuntime lateLoader = new() { Loader = () => ++loaderQueries > 1 };
Reject(() => LaunchSession.RunManual(game, lateLoader, _ => { }, CancellationToken.None),
    "Another XXMI started while waiting stops this session");
FakeManualRuntime exited = new() { Games = call => call == 1 ? [] : [target], Exited = () => true };
Reject(() => LaunchSession.RunManual(game, exited, _ => { }, CancellationToken.None),
    "A game exiting before injection is not loaded");
Check(exited.LoadCalls == 0, "Exited process cannot invoke the injector");
int exitQueries = 0;
FakeManualRuntime exitDuringLoad = new()
    { Games = call => call == 1 ? [] : [target], Exited = () => ++exitQueries > 1 };
Reject(() => LaunchSession.RunManual(game, exitDuringLoad, _ => { }, CancellationToken.None),
    "A game exiting during loader confirmation does not report success");
FakeManualRuntime failedLoad = new()
    { Games = call => call == 1 ? [] : [target], LoadError = new IOException("loader failed") };
Reject(() => LaunchSession.RunManual(game, failedLoad, _ => { }, CancellationToken.None),
    "Native loader failures propagate without automatic retry");
Check(failedLoad.LoadCalls == 1 && failedLoad.WaitCalls == 0, "A failing injector is called only once");
FakeManualRuntime noGame = new();
try
{
    LaunchSession.RunManual(game, noGame, _ => { }, CancellationToken.None,
        processTimeout: TimeSpan.Zero);
    throw new InvalidOperationException("Missing process timeout");
}
catch (TimeoutException) { checks.Add("Waiting for a game has a bounded timeout without loading"); }
FakeManualRuntime noConfirmation = new() { Games = call => call == 1 ? [] : [target] };
try
{
    LaunchSession.RunManual(game, noConfirmation, _ => { }, CancellationToken.None,
        loadTimeout: TimeSpan.Zero);
    throw new InvalidOperationException("Missing load timeout");
}
catch (TimeoutException) { checks.Add("Waiting for EFMI confirmation has a separate bounded timeout"); }
using (CancellationTokenSource cancelled = new())
{
    cancelled.Cancel();
    FakeManualRuntime cancelledRuntime = new();
    try
    {
        LaunchSession.RunManual(game, cancelledRuntime, _ => { }, cancelled.Token);
        throw new InvalidOperationException("Missing pre-cancellation");
    }
    catch (OperationCanceledException) { Check(cancelledRuntime.FindCalls == 0, "Pre-cancellation does not query or load a game"); }
}
using (CancellationTokenSource cancellation = new())
{
    FakeManualRuntime waiting = new();
    try
    {
        LaunchSession.RunManual(game, waiting, _ => cancellation.Cancel(), cancellation.Token);
        throw new InvalidOperationException("Missing waiting cancellation");
    }
    catch (OperationCanceledException) { Check(waiting.LoadCalls == 0, "Cancelling manual waiting never loads or kills a process"); }
}
using (CancellationTokenSource cancellation = new())
{
    FakeManualRuntime loading = new() { Games = call => call == 1 ? [] : [target] };
    try
    {
        LaunchSession.RunManual(game, loading, _ => { }, cancellation.Token, phase =>
        {
            if (phase == "phase=game-detected") cancellation.Cancel();
        });
        throw new InvalidOperationException("Missing pre-load cancellation");
    }
    catch (OperationCanceledException) { Check(loading.LoadCalls == 0, "Cancellation after detecting a game still prevents native loading"); }
}
using (CancellationTokenSource cancellation = new())
{
    FakeManualRuntime confirming = new()
    {
        Games = call => call == 1 ? [] : [target],
        Confirmation = () => { cancellation.Cancel(); return true; },
    };
    List<string> cancelledPhases = [];
    try
    {
        LaunchSession.RunManual(game, confirming, _ => { }, cancellation.Token, cancelledPhases.Add);
        throw new InvalidOperationException("Missing confirmation cancellation");
    }
    catch (OperationCanceledException)
    {
        Check(!cancelledPhases.Contains("phase=efmi-loaded"),
            "Cancellation during native confirmation cannot log a successful load");
    }
}
int finalExitQueries = 0;
FakeManualRuntime exitAtConfirmation = new()
    { Games = call => call == 1 ? [] : [target], Exited = () => ++finalExitQueries >= 3 };
List<string> exitPhases = [];
Reject(() => LaunchSession.RunManual(game, exitAtConfirmation, _ => { }, CancellationToken.None, exitPhases.Add),
    "A game exiting at the moment of confirmation does not report success");
Check(!exitPhases.Contains("phase=efmi-loaded"), "Exit detection wins over a stale native success result");
FakeManualRuntime deniedQuery = new()
{
    Games = call => call == 1 ? [] : throw new Win32Exception(5, "limited query denied"),
};
try
{
    LaunchSession.RunManual(game, deniedQuery, _ => { }, CancellationToken.None);
    throw new InvalidOperationException("Missing denied-query failure");
}
catch (Win32Exception error) when (error.NativeErrorCode == 5)
{
    Check(deniedQuery.LoadCalls == 0, "Denied process metadata does not fall back to unverified loading");
}
FakeManualRuntime partialQuery = new()
{
    Games = call => call == 1 ? [] : throw new Win32Exception(299, "limited query incomplete"),
};
try
{
    LaunchSession.RunManual(game, partialQuery, _ => { }, CancellationToken.None);
    throw new InvalidOperationException("Missing partial-query failure");
}
catch (Win32Exception error) when (error.NativeErrorCode == 299)
{
    Check(partialQuery.LoadCalls == 0, "Unresolved process metadata cannot bypass path checks");
}
Check(File.ReadAllBytes(gamePath).SequenceEqual(executable),
    "Manual workflow tests leave all fixture game bytes unchanged");

List<LibraryFile> libraries = [];
foreach (string name in new[] { "3dmloader.dll", "d3d11.dll", "d3dcompiler_47.dll" })
{
    string path = Path.Combine(bundle, "EFMI", name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, [1, 2, 3]);
    libraries.Add(new("EFMI/" + name, Convert.ToHexString(SHA256.HashData([1, 2, 3]))));
}
foreach (string relative in new[] { "EFMI/d3dx.ini", "EFMI/Core/EFMI/main.ini",
             "EFMI/Mods/EndfieldJiggleEFMI/EndfieldJiggle.ini",
             "EFMI/Mods/EndfieldJiggleEFMI/Passes.ini",
             "EFMI/Mods/EndfieldJiggleEFMI/Outfits.ini" })
{
    string path = Path.Combine(bundle, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "test fixture");
}
string manifestPath = Path.Combine(bundle, "standalone-manifest.json");
StandaloneManifest manifest = new(1, "v0.2.2", "1.4.8", "1.2.2", libraries.ToArray());
void WriteManifest(StandaloneManifest value) =>
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(value, StandalonePackage.JsonOptions));
WriteManifest(manifest);
StandalonePackage package = new(bundle);
Check(package.Validate().Libraries.Length == 3, "Standalone manifest requires all three official libraries");
Check(ShaderCachePathGuard.MaximumPathLength == 259 &&
    ShaderCachePathGuard.PredictCachePath(Path.Combine(bundle, "test.hlsl")).EndsWith("test.cs_5_0.ffffffff.bin"),
    "Cache path budget reserves profile and eight hex compile-flag digits within the upstream buffer");
string longShaderDirectory = Path.Combine(bundle, "EFMI", "Core", "Shaders");
Directory.CreateDirectory(longShaderDirectory);
int shaderNameLength = 250 - longShaderDirectory.Length - 1 - ".hlsl".Length;
if (shaderNameLength < 1 || shaderNameLength > 220) throw new InvalidOperationException("Use a shorter test output directory.");
string longShader = Path.Combine(longShaderDirectory, new string('x', shaderNameLength) + ".hlsl");
File.WriteAllText(longShader, "// This valid-length source produces an overlong cache filename.");
ShaderCachePathRisk? risk = ShaderCachePathGuard.FindLongest(package.EfmiDirectory);
Check(longShader.Length == 250 && risk?.Source == longShader && risk.Length > 259,
    "An HLSL path under MAX_PATH can still overflow after adding the cache suffix");
Reject(() => package.Validate(), "The package refuses overlong cache paths before loading any runtime DLL");
File.Delete(longShader);
string safeShader = Path.Combine(longShaderDirectory, "safe.hlsl");
File.WriteAllText(safeShader, "// short shader fixture");
Check(package.Validate().Libraries.Length == 3, "A short package remains valid without altering library files");
ShaderCachePathGuard.Validate(longShaderDirectory);
Check(File.ReadAllText(safeShader) == "// short shader fixture",
    "Path validation does not rewrite shader source, cache or timestamps");
File.Delete(safeShader);
File.WriteAllBytes(package.D3dPath, [4]);
Reject(() => package.Validate(), "Changed DLL bytes cannot be loaded");
File.WriteAllBytes(package.D3dPath, [1, 2, 3]);
WriteManifest(manifest with { Libraries = [libraries[0], libraries[0], libraries[1]] });
Reject(() => package.Validate(), "Duplicate library entries cannot replace a missing library");
WriteManifest(manifest with { Libraries = [libraries[0], libraries[1], new("../outside.dll", "00")] });
Reject(() => package.Validate(), "Manifest paths cannot leave the bundle");
WriteManifest(manifest);
File.Delete(Path.Combine(bundle, "EFMI/Mods/EndfieldJiggleEFMI/Outfits.ini"));
Reject(() => package.Validate(), "A missing outfit bridge blocks a partial standalone bundle");

if (args.Length > 1)
{
    StandalonePackage real = new(args[1]);
    StandaloneManifest actual = real.Validate();
    Check(actual.EfmiVersion == "1.4.8" && actual.LibrariesVersion == "1.2.2",
        "Built package contains the pinned official runtime versions");
    RuntimeLocation? located = RuntimeLocationLocator.Discover(
        Path.GetDirectoryName(real.ConfiguratorPath)!, real.EfmiDirectory);
    Check(located?.EfmiDirectory == real.EfmiDirectory &&
        located.RuntimeDirectory == Path.Combine(real.ModsDirectory, "EndfieldJiggleEFMI"),
        "The unchanged configurator automatically locates the standalone runtime");
    Check(File.Exists(real.ConfiguratorPath), "The standalone bundle includes the existing optional configurator");
}
File.WriteAllText(Path.Combine(fixture, "test-report.json"), JsonSerializer.Serialize(
    new { checks, gameStarted = false, liveInstallationModified = false }, StandalonePackage.JsonOptions));
Console.WriteLine($"Standalone launcher checks passed: {checks.Count}. No game was started.");

sealed class FakeManualRuntime : IManualLaunchRuntime
{
    public Func<int, GameProcess[]> Games { get; init; } = _ => [];
    public Func<bool> Loader { get; init; } = () => false;
    public Func<bool> Exited { get; init; } = () => false;
    public Func<bool> Confirmation { get; init; } = () => true;
    public Exception? LoadError { get; init; }
    public int FindCalls { get; private set; }
    public int LoadCalls { get; private set; }
    public int LoadedProcess { get; private set; }
    public int WaitCalls { get; private set; }
    public GameProcess[] FindGames() => Games(++FindCalls);
    public bool OtherLoaderRunning() => Loader();
    public bool HasExited(int processId) => Exited();
    public void Load(int processId)
    {
        LoadCalls++;
        LoadedProcess = processId;
        if (LoadError is not null) throw LoadError;
    }
    public bool WaitOnce() { WaitCalls++; return Confirmation(); }
}
