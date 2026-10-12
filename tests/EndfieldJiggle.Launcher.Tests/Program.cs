using System.Security.Cryptography;
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
var command = LaunchSession.CreateStartInfo(game);
Check(command.FileName == gamePath && command.WorkingDirectory == gameDirectory &&
    command.ArgumentList.SequenceEqual(["-force-d3d11"]), "Launch command keeps game path and DX11 separate");

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
