using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    ZipArchiveEntry entryPoint = archive.Entries.Single(entry => entry.FullName == "EndfieldJiggle.ini" ||
        entry.FullName.EndsWith("/EndfieldJiggle.ini", StringComparison.Ordinal));
    string prefix = entryPoint.FullName[..^"EndfieldJiggle.ini".Length];
    ZipArchiveEntry[] files = archive.Entries.Where(entry =>
        entry.FullName.StartsWith(prefix, StringComparison.Ordinal) &&
        (entry.FullName[prefix.Length..] is "EndfieldJiggle.ini" or "Passes.ini" or "Outfits.ini" or "OutfitPreparation.ini" or "JiggleForge-LICENSE" or "Licenses/GPL-3.0.txt" ||
         entry.FullName[prefix.Length..].StartsWith("shaders/", StringComparison.Ordinal)) &&
        !entry.FullName.EndsWith('/')).ToArray();
    Check(files.Any(entry => entry.FullName == prefix + "EndfieldJiggle.ini") &&
          files.Any(entry => entry.FullName == prefix + "Passes.ini"),
        "Official package contains both configurable INIs");

    foreach (ZipArchiveEntry entry in files)
    {
        string relative = SafeRelative(entry.FullName[prefix.Length..]);
        string destination = Path.GetFullPath(Path.Combine(runtime, relative));
        string destinationPrefix = Path.GetFullPath(runtime) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
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
string diagnosticsLog = Path.Combine(testRoot, "configurator.log");
File.WriteAllText(diagnosticsLog, "local configurator failure\n");
string efmiLog = Path.Combine(testRoot, "EFMI", "d3d11_log.txt");
File.WriteAllText(efmiLog, new string('x', 40 * 1024) + "\nrecent loader warning\n");
string receiptPath = Path.Combine(runtime, "EndfieldJiggle.install.json");
string receiptJson = JsonSerializer.Serialize(new
{
    Schema = 1, Version = "v0.2.7",
    Files = new[]
    {
        new { Name = "Passes.ini", Sha256 = Hex(packageFiles["Passes.ini"].Bytes) },
        new { Name = "InputGuard/EndfieldJiggleInputGuard.dll", Sha256 = new string('A', 64) },
    },
});
File.WriteAllText(receiptPath, receiptJson);
DiagnosticSnapshot diagnostic = DiagnosticSnapshotCollector.Capture(
    Path.Combine(testRoot, "EFMI"), runtime, null, diagnosticsLog);
Check(diagnostic.Layout.Value?.Candidates.Count == 1 &&
      diagnostic.SavedSettings.Value == original.Settings &&
      diagnostic.Receipt.Value is { Present: true, Version: "v0.2.7" } &&
      diagnostic.Files.Value?.Single(file => file.Path == Path.Combine(runtime, "Passes.ini")).MatchesReceipt == true &&
      diagnostic.Files.Value?.Single(file => file.Path == Path.Combine(runtime, "InputGuard", "EndfieldJiggleInputGuard.dll")).Present == false &&
      diagnostic.InputGuard.Value is not null && diagnostic.Outfit.Error is not null,
    "Diagnostic export independently includes layout, saved settings, receipt, missing input module and process status");
Check(diagnostic.Logs.Count == 2 && diagnostic.Logs[0].Tail == "local configurator failure\n" &&
      diagnostic.Logs[1].Truncated && diagnostic.Logs[1].Tail?.Contains("recent loader warning") == true,
    "Diagnostic logs include only a bounded recent tail");
string launcherConfig = Path.Combine(testRoot, XxmiDiagnostics.ConfigName);
File.WriteAllText(launcherConfig, JsonSerializer.Serialize(new
{
    Launcher = new { active_importer = "EFMI", enabled_importers = new[] { "EFMI" } },
    Importers = new { EFMI = new { Importer = new { importer_folder = "EFMI/", game_folder = "Game" },
        Migoto = new { input = true } } },
    Packages = new { packages = new { EFMI = new { deployed_version = "fixture" } } },
    privateSetting = "private fixture payload",
}), new UTF8Encoding(true));
File.WriteAllText(Path.Combine(testRoot, "XXMI Launcher Log.txt"),
    new string('a', 40 * 1024) + "\nPermissionError: private fixture payload\n");
diagnostic = DiagnosticSnapshotCollector.Capture(Path.Combine(testRoot, "EFMI"), runtime, null, diagnosticsLog,
    xxmiConfig: launcherConfig);
XxmiStatus xxmi = diagnostic.Xxmi.Value!;
Check(xxmi.Profiles.Single() is { MatchesSelectedDirectory: true, ActiveImporter: "EFMI", EfmiListed: true,
    EfmiDeployedVersion: "fixture", MigotoInput: true, Log: { Truncated: true, ModifiedUtc: not null } } &&
    xxmi.Profiles.Single().Log.ErrorTypes.Contains("PermissionError") &&
    !JsonSerializer.Serialize(xxmi).Contains("private fixture payload"),
    "Explicit XXMI profile resolves relative EFMI paths and exports only allowlisted log evidence");
Check(XxmiDiagnostics.Read(Path.Combine(testRoot, "EFMI"), null, default).Profiles
    .Any(p => p.ConfigPath == launcherConfig && p.MatchesSelectedDirectory == true),
    "Automatic discovery finds an XXMI root above the selected EFMI");
XxmiStatus mismatch = XxmiDiagnostics.Inspect(Path.Combine(testRoot, "different"), [launcherConfig], 0, default);
Check(mismatch.Profiles.Single().MatchesSelectedDirectory == false && mismatch.Summary.Contains("另一套 EFMI"),
    "The wrong EFMI installation is identified even with the launcher closed");
string secondConfig = Path.Combine(testRoot, "other-config.json");
File.WriteAllText(secondConfig, "{");
XxmiStatus multiple = XxmiDiagnostics.Inspect(null, [launcherConfig, secondConfig], 0, default);
Check(multiple.Profiles.Count == 2 && multiple.Profiles[1].ConfigError is not null &&
    multiple.Profiles[0].MatchesSelectedDirectory is null && multiple.Summary.Contains("2 份"),
    "A second, broken launcher profile is separate from the first and missing evidence stays unknown");
File.WriteAllText(secondConfig, """{"Launcher":{"active_importer":"WWMI","enabled_importers":[]},"Importers":{"EFMI":{"Migoto":{"input":false}}}}""");
XxmiStatus off = XxmiDiagnostics.Inspect(null, [secondConfig], 0, default);
Check(off.Summary.Contains("WWMI") && off.Summary.Contains("没有 EFMI") && off.Summary.Contains("input 已关闭") &&
    XxmiDiagnostics.Inspect(null, [], 0, default).Profiles.Count == 0,
    "Disabled or unselected EFMI and input settings are distinguished from a missing launcher");
string iniFixture = Path.Combine(testRoot, "ini-settings");
Directory.CreateDirectory(iniFixture);
File.WriteAllText(Path.Combine(iniFixture, "d3dx.ini"),
    "[Logging]\ninput=0\nlog_level=warn\n[Include]\ninclude_recursive=Mods\nexclude_recursive=DISABLED*\nexclude_recursive=desktop.ini\n");
var efmiSettings = EfmiInputDiagnostics.Read(iniFixture);
Check(efmiSettings.InputAtStartup is null && efmiSettings.LogLevel == "warn" &&
    efmiSettings.IncludeRecursive.SequenceEqual(["Mods"]) && efmiSettings.ExcludeRecursive.SequenceEqual(["DISABLED*", "desktop.ini"]),
    "Loader include rules preserve all values and input logging is not mistaken for disabled controls");
File.WriteAllText(receiptPath, "{");
diagnostic = DiagnosticSnapshotCollector.Capture(Path.Combine(testRoot, "EFMI"), runtime, null, diagnosticsLog);
Check(diagnostic.Receipt.Error is not null && diagnostic.Files.Value is not null &&
      diagnostic.SavedSettings.Value == original.Settings,
    "A corrupt installation receipt does not hide other diagnostic sections");
File.WriteAllText(receiptPath, receiptJson);
int sessionPid = int.MaxValue - Environment.ProcessId;
string mappingName = "Local\\EndfieldJiggle.InputGuard." + sessionPid;
using (MemoryMappedFile mapping = MemoryMappedFile.CreateNew(mappingName, 24))
{
    using MemoryMappedViewAccessor view = mapping.CreateViewAccessor();
    view.Write(0, 1);
    view.Write(4, 1);
    view.Write(8, 1);
    view.Write(12, Environment.ProcessId);
    view.Write(16, Environment.TickCount64);
    Check(DiagnosticSnapshotCollector.ReadSession(sessionPid).Ready,
        "Diagnostic report reads a live guard mapping without writing to it");
    view.Write(16, Environment.TickCount64 - 2000);
    Check(!DiagnosticSnapshotCollector.ReadSession(sessionPid).Ready,
        "Diagnostic report distinguishes stale input guard heartbeat");
}
string genericOutfit = Path.Combine(testRoot, "EFMI", "Mods", "GenericOutfit");
Directory.CreateDirectory(genericOutfit);
File.WriteAllText(Path.Combine(genericOutfit, "body.ini"),
    "namespace = Example\n[TextureOverrideBody]\nhash = 01234567\ndrawindexed = 12, 0, 0\n");
diagnostic = DiagnosticSnapshotCollector.Capture(
    Path.Combine(testRoot, "EFMI"), runtime, genericOutfit, diagnosticsLog);
Check(diagnostic.Outfit.Value is not null &&
      diagnostic.Qaqm.Value is { Applicable: false, Plan: null } &&
      diagnostic.Qaqm.Error is null,
    "Generic outfits are diagnosed without assuming QAQM is installed or required: " +
    $"outfit={diagnostic.Outfit.Error}, qaqm={diagnostic.Qaqm.Error}, " +
    $"applicable={diagnostic.Qaqm.Value?.Applicable}, note={diagnostic.Qaqm.Value?.Note}, " +
    $"issues={string.Join(';', diagnostic.Qaqm.Value?.Plan?.Issues.Select(issue => issue.Code) ?? [])}");
bool includesOutfits = packageFiles.ContainsKey("Outfits.ini");
bool includesPreparation = packageFiles.ContainsKey("OutfitPreparation.ini");
bool unifiedInput = Encoding.UTF8.GetString(packageFiles["EndfieldJiggle.ini"].Bytes)
    .Contains("global $input_profile = 3", StringComparison.Ordinal);
Check(unifiedInput ? original.Settings == TouchSettings.Default : !original.Settings.DefaultEnabled &&
      original.Settings.Toggle == new KeyBinding("VK_F8", Ctrl: true, Shift: true) &&
      original.Settings.Stop == new KeyBinding("VK_F7", Ctrl: true, Shift: true) &&
      original.Settings.Diagnostic == new KeyBinding("VK_F9", Ctrl: true, Shift: true) &&
      original.Settings.Drag == new KeyBinding("VK_LBUTTON", Shift: true),
    "Reader preserves the selected package's documented input defaults");
Check(packageFiles.Count == (includesPreparation ? 15 : includesOutfits ? 14 : 13) &&
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
bool hasReceivers = passesText.Contains("[CommandListReceiveNative1]", StringComparison.Ordinal);
int receiverGates = hasReceivers ? 2 : 0;
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
          passesText.Split("if !$outfit_seen &&", StringSplitOptions.None).Length ==
              (passesText.Contains("[CommandListBindPass13]", StringComparison.Ordinal) ? 14 : 13) + receiverGates &&
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
bool hasSkinPass = File.ReadAllText(Path.Combine(runtime, "Passes.ini"))
    .Contains("[CommandListBindPass13]", StringComparison.Ordinal);
Check(originalGates.Length == (hasSkinPass ? 13 : 12) + receiverGates,
    "The installed pass file has the reviewed native input gates");
string extraGate = File.ReadAllText(Path.Combine(runtime, "Passes.ini")) +
    Environment.NewLine + "if $enabled && $mod_id > 0 && !$ui_mouse && !$cancelled";
string gatePath = Path.Combine(runtime, "Passes.ini");
byte[] passesOriginal = File.ReadAllBytes(gatePath);
File.WriteAllText(gatePath, extraGate, new UTF8Encoding(false));
Reject(() => new RuntimeSettingsStore(runtime).Read(),
    "Unexpected extra input gates make the package unsupported rather than broadly rewriting it");
File.WriteAllBytes(gatePath, passesOriginal);
if (hasSkinPass)
{
    File.WriteAllText(gatePath, Encoding.UTF8.GetString(passesOriginal)
        .Replace("run = CommandListBindPass13", "run = CommandListBindPass12", StringComparison.Ordinal));
    Reject(() => new RuntimeSettingsStore(runtime).Read(),
        "The added skin program cannot silently use the wrong pass");
    File.WriteAllBytes(gatePath, passesOriginal);
}
if (hasReceivers)
{
    Check(passesOriginal.SequenceEqual(packageFiles["Passes.ini"].Bytes),
        "Saving and restoring settings preserves every structural receiver byte");
    string receiverText = Encoding.UTF8.GetString(passesOriginal);
    foreach ((string changed, string reason) in new[]
    {
        (receiverText.Replace("[CommandListReceiveNative2]", "[MissingReceiver]", StringComparison.Ordinal),
            "A missing receiver callback is rejected"),
        (receiverText.Replace("run = CommandListReceiveNative2", "run = CommandListReceiveNative3", StringComparison.Ordinal),
            "A receiver cannot silently bind an unknown command"),
        (receiverText.Replace("ShaderRegexEJ_zzReceiver001", "ShaderRegexEJ_ZReceiver001", StringComparison.Ordinal),
            "Receiver names that steal legacy picker priority are rejected"),
        (receiverText.Replace("&& !$cancelled && $complete", "&& !$cancelled", StringComparison.Ordinal),
            "A receiver cannot bypass the completed touch session")
    })
    {
        File.WriteAllText(gatePath, changed, new UTF8Encoding(false));
        Reject(() => new RuntimeSettingsStore(runtime).Read(), reason);
        File.WriteAllBytes(gatePath, passesOriginal);
    }
}

if (includesPreparation)
{
    string preparation = Path.Combine(runtime, "OutfitPreparation.ini");
    byte[] originalPreparation = File.ReadAllBytes(preparation);
    DateTime preparationTime = File.GetLastWriteTimeUtc(preparation);
    File.Move(preparation, preparation + ".bak");
    Reject(() => new RuntimeSettingsStore(runtime).Read(), "Missing outfit preparation blocks editing a partial installation");
    File.Move(preparation + ".bak", preparation);
    File.AppendAllText(preparation, "\n; external edit\n");
    Reject(() => new RuntimeSettingsStore(runtime).Read(), "Unreviewed preparation cannot silently change callback ordering");
    File.WriteAllBytes(preparation, originalPreparation);
    File.SetLastWriteTimeUtc(preparation, preparationTime);
    Check(new RuntimeSettingsStore(runtime).Read().Settings == original.Settings,
        "Restored preparation remains usable with saved settings");
}

string outfitRuntime = Path.Combine(testRoot, "EFMI", "Mods", "outfits-runtime");
Directory.CreateDirectory(outfitRuntime);
foreach ((string relative, (byte[] bytes, DateTime time)) in packageFiles)
{
    string path = Path.Combine(outfitRuntime, SafeRelative(relative));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
    File.SetLastWriteTimeUtc(path, time);
}
File.Delete(Path.Combine(outfitRuntime, "OutfitPreparation.ini"));
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

TouchSettings.Default.Validate();
Check(TouchSettings.Default.Drag.IniValue == "no_ctrl no_shift alt VK_LBUTTON" &&
      TouchSettings.Default.Drag.DisplayName == "Alt+鼠标左键" &&
      TouchSettings.ParseBinding(TouchSettings.Default.Drag.IniValue, allowMouse: true) == TouchSettings.Default.Drag &&
      TouchSettings.ParseBinding("no_ctrl no_shift VK_MENU", allowMouse: true) == new KeyBinding("VK_MENU"),
    "Default Alt-plus-mouse chord roundtrips while previous primary-Alt settings remain readable");
Reject(() => TouchSettings.ParseBinding("no_ctrl no_shift no_alt VK_MENU", allowMouse: true),
    "Impossible Alt primary binding is rejected");
Reject(() => (TouchSettings.Default with { Drag = new("VK_MENU", Alt: true) }).Validate(),
    "Duplicate Alt primary and modifier is rejected");
Check(SettingsProfile.Import(SettingsProfile.Export(TouchSettings.Default)) == TouchSettings.Default,
    "Alt and default-on settings survive profile export/import");
RuntimeApplyResult defaultsApply = store.Apply(TouchSettings.Default, store.Read().Fingerprint, requireStopped: false);
Check(store.Read().Settings == TouchSettings.Default,
    "Configurator saves the required Alt modifier and left mouse button");
TouchSettings disabledStartup = TouchSettings.Default with { DefaultEnabled = false };
store.Apply(disabledStartup, store.Read().Fingerprint, requireStopped: false);
Check(store.Read().Settings == disabledStartup,
    "Player can disable default-on startup while retaining the enable shortcut");
store.RestoreLast(requireStopped: false);
Check(store.Read().Settings == TouchSettings.Default, "Startup choice restores through the normal backup");
if (defaultsApply.Changed) store.RestoreLast(requireStopped: false);

Console.WriteLine($"PASS: {checks.Count} configurator checks.");
Console.WriteLine($"Fixture: {testRoot}");
