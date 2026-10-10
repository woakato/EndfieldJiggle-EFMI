using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EndfieldJiggle.Configurator.Core;

if (args.Length < 1)
    throw new ArgumentException("Pass the official Windows Mod ZIP as the first argument.");

string archivePath = Path.GetFullPath(args[0]);
string outputRoot = Path.GetFullPath(args.Length > 1 ? args[1] :
    Path.Combine(Environment.CurrentDirectory, "reports", "configurator", "test-runs"));
Directory.CreateDirectory(outputRoot);
string testRoot = Path.Combine(outputRoot, "runtime-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
List<string> checks = [];

void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException("FAIL: " + name);
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

Check(!RuntimeSettingsStore.IsGameOrLoaderProcess(
        Environment.ProcessId, "EndfieldJiggleConfigurator", Environment.ProcessId),
    "The configurator does not classify its own process as the game");
Check(RuntimeSettingsStore.IsGameOrLoaderProcess(
        int.MaxValue, "Endfield-Win64-Shipping", Environment.ProcessId) &&
      RuntimeSettingsStore.IsGameOrLoaderProcess(
        int.MaxValue, "XXMI Launcher", Environment.ProcessId),
    "External game and XXMI processes still block runtime edits");
Check(!RuntimeSettingsStore.IsGameOrLoaderProcess(
        int.MaxValue, "ConfiguratorTests", Environment.ProcessId),
    "Unrelated external processes do not block runtime edits");

byte[] HashBytes(byte[] bytes) => SHA256.HashData(bytes);
string Hex(byte[] bytes) => Convert.ToHexString(HashBytes(bytes));

static string SafeRelative(string path)
{
    string[] parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
    if (Path.IsPathRooted(path) || parts.Any(part => part is "." or ".."))
        throw new InvalidDataException("Unsafe path in test archive.");
    return Path.Combine(parts);
}

string runtime = Path.Combine(testRoot, "EFMI", "Mods", "EndfieldJiggleEFMI");
Directory.CreateDirectory(runtime);
Dictionary<string, (byte[] Bytes, DateTime Time)> packageFiles = new(StringComparer.OrdinalIgnoreCase);
using (ZipArchive archive = ZipFile.OpenRead(archivePath))
{
    const string marker = "/Mods/EndfieldJiggleEFMI/";
    ZipArchiveEntry[] files = archive.Entries.Where(entry =>
        entry.FullName.Contains(marker, StringComparison.Ordinal) &&
        !entry.FullName.Contains(marker + "Configurator/", StringComparison.Ordinal) &&
        !entry.FullName.EndsWith('/')).ToArray();
    Check(files.Any(entry => entry.FullName.EndsWith("/EndfieldJiggle.ini", StringComparison.Ordinal)) &&
          files.Any(entry => entry.FullName.EndsWith("/Passes.ini", StringComparison.Ordinal)),
        "Official package contains both configurable INIs");

    foreach (ZipArchiveEntry entry in files)
    {
        int markerIndex = entry.FullName.IndexOf(marker, StringComparison.Ordinal);
        string relative = SafeRelative(entry.FullName[(markerIndex + marker.Length)..]);
        string destination = Path.GetFullPath(Path.Combine(runtime, relative));
        string prefix = Path.GetFullPath(runtime) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive path leaves the fixture runtime.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using Stream source = entry.Open();
        using MemoryStream content = new();
        source.CopyTo(content);
        byte[] bytes = content.ToArray();
        File.WriteAllBytes(destination, bytes);
        DateTime time = entry.LastWriteTime.UtcDateTime;
        File.SetLastWriteTimeUtc(destination, time);
        packageFiles.Add(relative, (bytes, time));
    }
}

foreach (string name in new[] { "d3dx.ini", "d3d11.dll", "Mods/unrelated.ini" })
{
    string path = Path.Combine(testRoot, "EFMI", name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "fixture:" + name, new UTF8Encoding(false));
}
string globalConfig = Path.Combine(testRoot, "EFMI", "d3dx.ini");
string loader = Path.Combine(testRoot, "EFMI", "d3d11.dll");
string unrelated = Path.Combine(testRoot, "EFMI", "Mods", "unrelated.ini");
Dictionary<string, (string Hash, long Time)> protectedFiles = new(StringComparer.OrdinalIgnoreCase);
foreach (string path in new[] { globalConfig, loader, unrelated })
    protectedFiles[path] = (Hex(File.ReadAllBytes(path)), File.GetLastWriteTimeUtc(path).Ticks);
string randomShader = packageFiles.Keys.First(path => path.StartsWith(
    "shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

RuntimeSettingsStore store = new(runtime);
RuntimeSettingsSnapshot original = store.Read();
bool includesOutfits = packageFiles.ContainsKey("Outfits.ini");
Check(!original.Settings.DefaultEnabled &&
      original.Settings.Toggle == new KeyBinding("VK_F8", Ctrl: true, Shift: true) &&
      original.Settings.Stop == new KeyBinding("VK_F7", Ctrl: true, Shift: true) &&
      original.Settings.Diagnostic == new KeyBinding("VK_F9", Ctrl: true, Shift: true) &&
      original.Settings.Drag == new KeyBinding("VK_LBUTTON", Shift: true),
    "Reader reflects the documented default-off settings");
Check(packageFiles.Count == (includesOutfits ? 14 : 13) &&
      original.Settings.Physics == new PhysicsSettings(0.12m, 0.04m, 0.75m),
    "The configurator recognizes both native-only and outfit-enabled install payloads");

TouchSettings configured = new(
    true,
    new KeyBinding("VK_F8"),
    new KeyBinding("VK_F7"),
    new KeyBinding("VK_F9"),
    new KeyBinding("VK_XBUTTON2", Ctrl: true),
    new PhysicsSettings(0.23m, 0.071m, 1.7m));
RuntimeApplyResult applied = store.Apply(configured, original.Fingerprint, requireStopped: false);
string[] expectedChangedFiles = packageFiles.Keys.Where(name =>
    name == "EndfieldJiggle.ini" ||
    name is "Passes.ini" or "Outfits.ini" &&
    Encoding.UTF8.GetString(packageFiles[name].Bytes).Contains("&& !$ui_mouse &&", StringComparison.Ordinal))
    .Order().ToArray();
Check(applied.Changed && applied.ChangedFiles.Order().SequenceEqual(
        expectedChangedFiles, StringComparer.OrdinalIgnoreCase),
    "Apply updates only owned configuration files whose settings actually change");
Check(store.Read().Settings == configured,
    "Startup, bindings and physics persist in the Mod folder after the app closes");
Check(!File.Exists(Path.Combine(runtime, "EndfieldJiggle")) &&
      !File.Exists(Path.Combine(runtime, "Passes")) &&
      !Directory.EnumerateFiles(runtime, ".*.tmp", SearchOption.TopDirectoryOnly).Any(),
    "Atomic updates do not leave sibling files or temporary files in the Mod folder");
Check(applied.BackupDirectory is not null &&
      expectedChangedFiles.All(name => File.Exists(Path.Combine(applied.BackupDirectory, name + ".bak"))),
    "Apply creates a per-file backup before updating runtime INIs");

string runtimeText = File.ReadAllText(Path.Combine(runtime, "EndfieldJiggle.ini"));
string passesText = File.ReadAllText(Path.Combine(runtime, "Passes.ini"));
Check(runtimeText.Contains("global $enabled = 1", StringComparison.Ordinal) &&
      runtimeText.Contains("key = no_ctrl no_shift no_alt VK_F8", StringComparison.Ordinal) &&
      runtimeText.Contains("key = ctrl no_shift no_alt VK_XBUTTON2", StringComparison.Ordinal) &&
      runtimeText.Contains("global $radius = 0.23", StringComparison.Ordinal) &&
      runtimeText.Contains("global $max_offset = 0.071", StringComparison.Ordinal) &&
      runtimeText.Contains("global $drag_scale = 1.7", StringComparison.Ordinal),
    "The actual game runtime INI receives the selected settings");
Check(passesText.Contains("&& !($ui_mouse && !$drag) &&", StringComparison.Ordinal) &&
      !passesText.Contains("&& !$ui_mouse &&", StringComparison.Ordinal),
    "Drag input remains available without bypassing the explicit drag key");
Check(runtimeText.Contains(" || ($ui_mouse && !$drag) ||", StringComparison.Ordinal) &&
      !runtimeText.Contains(" || $ui_mouse ||", StringComparison.Ordinal) &&
      runtimeText.Contains("[KeyUIMouse]\nkey = VK_LBUTTON", StringComparison.Ordinal),
    "Mouse navigation and session liveness agree with custom drag bindings");
if (includesOutfits)
{
    Check(runtimeText.Contains("if $outfit_active\n    drawindexedinstanced = $outfit_count", StringComparison.Ordinal) &&
          passesText.Split("if !$outfit_seen &&", StringSplitOptions.None).Length == 13 &&
          File.ReadAllBytes(Path.Combine(runtime, "Outfits.ini")).SequenceEqual(packageFiles["Outfits.ini"].Bytes),
        "Configuring the exported release preserves outfit picking, exclusions and callbacks");
}
foreach ((string relative, (byte[] bytes, DateTime time)) in packageFiles)
{
    if (expectedChangedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
        continue;
    string path = Path.Combine(runtime, SafeRelative(relative));
    Check(Hex(File.ReadAllBytes(path)) == Hex(bytes) &&
          File.GetLastWriteTimeUtc(path).Ticks == time.Ticks,
        $"Unowned runtime payload remains byte- and FILETIME-identical: {relative}");
}
foreach ((string path, (string hash, long time)) in protectedFiles)
    Check(Hex(File.ReadAllBytes(path)) == hash && File.GetLastWriteTimeUtc(path).Ticks == time,
        $"EFMI and unrelated files remain unchanged: {Path.GetFileName(path)}");

RuntimeSettingsSnapshot afterApply = store.Read();
RuntimeApplyResult idempotent = store.Apply(configured, afterApply.Fingerprint, requireStopped: false);
Check(!idempotent.Changed && idempotent.BackupDirectory is null,
    "Applying the same configuration again is idempotent");
Check(store.HasRestorableBackup, "An applied configuration is offered for restoration");

byte[] appliedRuntime = File.ReadAllBytes(Path.Combine(runtime, "EndfieldJiggle.ini"));
DateTime appliedRuntimeTime = File.GetLastWriteTimeUtc(Path.Combine(runtime, "EndfieldJiggle.ini"));
File.SetLastWriteTimeUtc(Path.Combine(runtime, "EndfieldJiggle.ini"), appliedRuntimeTime.AddSeconds(3));
Reject(() => store.RestoreLast(requireStopped: false),
    "A timestamp-only edit blocks restoration instead of being overwritten");
File.SetLastWriteTimeUtc(Path.Combine(runtime, "EndfieldJiggle.ini"), appliedRuntimeTime);
Check(File.ReadAllBytes(Path.Combine(runtime, "EndfieldJiggle.ini")).SequenceEqual(appliedRuntime),
    "Rejected restore leaves the runtime bytes unchanged");

RuntimeApplyResult restored = store.RestoreLast(requireStopped: false);
Check(restored.Changed && !store.HasRestorableBackup,
    "Restore last returns the exact previous package and consumes that backup state");
foreach ((string relative, (byte[] bytes, DateTime time)) in packageFiles)
{
    string path = Path.Combine(runtime, SafeRelative(relative));
    Check(File.ReadAllBytes(path).SequenceEqual(bytes) &&
          File.GetLastWriteTimeUtc(path).Ticks == time.Ticks,
        $"Restore returns original package bytes and FILETIME: {relative}");
}
Reject(() => store.RestoreLast(requireStopped: false), "A consumed backup cannot be restored twice");

TouchSettings duplicate = configured with { Stop = configured.Toggle };
Reject(duplicate.Validate, "Duplicate key chords are rejected");
Reject(() => (configured with { Toggle = new KeyBinding("VK_F10") }).Validate(),
    "F10 is rejected because EFMI uses it for reload");
Reject(() => (configured with { Toggle = new KeyBinding("VK_F11") }).Validate(),
    "F11 is rejected because EFMI uses it for the Mod toggle");
Reject(() => (configured with { Toggle = new KeyBinding("VK_F12") }).Validate(),
    "EFMI guide keys are rejected");
Reject(() => (configured with { Toggle = new KeyBinding("A;run = injected") }).Validate(),
    "Raw INI injection text is rejected");
Reject(() => (configured with { Toggle = new KeyBinding("Q") }).Validate(),
    "Unmodified character-navigation keys are rejected");
Reject(() => (configured with { Drag = new KeyBinding("VK_LBUTTON") with { Key = "VK_F08" } }).Validate(),
    "Noncanonical key names are rejected");
Reject(() => (configured with { Physics = new PhysicsSettings(0.8m, 0.04m, 0.75m) }).Validate(),
    "Out-of-range physics values are rejected");

string[] originalGates = File.ReadAllLines(Path.Combine(runtime, "Passes.ini"))
    .Where(line => line.Contains("&& !", StringComparison.Ordinal) &&
                   line.Contains("$ui_mouse", StringComparison.Ordinal)).ToArray();
Check(originalGates.Length == 12, "The installed pass file has exactly twelve recognized input gates");
string extraGate = File.ReadAllText(Path.Combine(runtime, "Passes.ini")) +
    Environment.NewLine + "if $enabled && $mod_id > 0 && !$ui_mouse && !$cancelled";
string gatePath = Path.Combine(runtime, "Passes.ini");
byte[] passesOriginal = File.ReadAllBytes(gatePath);
File.WriteAllText(gatePath, extraGate, new UTF8Encoding(false));
Reject(() => new RuntimeSettingsStore(runtime).Read(),
    "Unexpected extra input gates make the package unsupported rather than broadly rewriting it");
File.WriteAllBytes(gatePath, passesOriginal);

string outfitRuntime = Path.Combine(testRoot, "EFMI", "Mods", "outfits-runtime");
Directory.CreateDirectory(outfitRuntime);
foreach ((string relative, (byte[] bytes, DateTime time)) in packageFiles)
{
    string path = Path.Combine(outfitRuntime, SafeRelative(relative));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
    File.SetLastWriteTimeUtc(path, time);
}
File.WriteAllText(Path.Combine(outfitRuntime, "Outfits.ini"),
    """
    namespace = EndfieldJiggleEFMI
    [CommandListBeginOutfitDraw]
    if $enabled && $mod_id > 0 && $\EFMIv1\enable_mods && !$ui_mouse && !$ui_navigation && !$cancelled
    endif
    """, new UTF8Encoding(false));
RuntimeSettingsStore outfitStore = new(outfitRuntime);
RuntimeSettingsSnapshot outfitSnapshot = outfitStore.Read();
RuntimeApplyResult outfitApply = outfitStore.Apply(configured, outfitSnapshot.Fingerprint, requireStopped: false);
Check(outfitApply.ChangedFiles.Contains("Outfits.ini", StringComparer.OrdinalIgnoreCase) &&
      File.ReadAllText(Path.Combine(outfitRuntime, "Outfits.ini"))
          .Contains("&& !($ui_mouse && !$drag) &&", StringComparison.Ordinal),
    "The optional outfit runtime receives the same mouse-drag input rule");
outfitStore.RestoreLast(requireStopped: false);
Check(File.ReadAllText(Path.Combine(outfitRuntime, "Outfits.ini"))
      .Contains("&& !$ui_mouse &&", StringComparison.Ordinal),
    "The optional outfit input rule restores exactly");

Console.WriteLine($"PASS: {checks.Count} configurator checks.");
Console.WriteLine($"Fixture: {testRoot}");
