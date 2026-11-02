using System.Security.Cryptography;
using System.Text.Json;
using EndfieldJiggle.Configurator.Core;

if (args.Length is < 1 or > 3) throw new ArgumentException("Pass the Mod ZIP and optional isolated loader fixture.");
string root = Path.GetFullPath(Path.Combine("reports", "layout-repair-tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
InstallablePackage package = InstallablePackage.FromRuntimeArchive(File.ReadAllBytes(args[0]));
if (args.Length == 3 && args[1] == "--inspect")
{
    RuntimeLayout layout = RuntimeLocationLocator.Inspect(args[2]);
    LayoutRepairPreview inspectionPreview = package.PreviewLayoutRepair(args[2]);
    string report = Path.Combine(root, "read-only-inspection.json");
    File.WriteAllText(report, JsonSerializer.Serialize(new
        { layout, preview = inspectionPreview, readOnly = true, liveInstallationModified = false },
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"READ_ONLY_REPORT={report}");
    Console.WriteLine($"Candidates={layout.Candidates.Count}; quarantineInis={inspectionPreview.QuarantineFiles.Length}; missingFiles={inspectionPreview.MissingFiles.Length}");
    return;
}
if (args.Length == 3) throw new ArgumentException("Unknown inspection mode.");
List<string> checks = [];
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks.Add(name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is IOException or InvalidDataException)
    {
        checks.Add(name);
        return;
    }
    throw new Exception("Expected rejection: " + name);
}
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Efmi(string name)
{
    string efmi = Path.Combine(root, name, "EFMI");
    Directory.CreateDirectory(Path.Combine(efmi, "Mods"));
    File.WriteAllText(Path.Combine(efmi, "d3dx.ini"), "[Include]\ninclude_recursive = Mods\n");
    File.WriteAllText(Path.Combine(efmi, "d3d11.dll"), "isolated-fixture");
    return efmi;
}
void CopyRuntime(string source, string target)
{
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string name = Path.GetRelativePath(source, file);
        if (name == "EndfieldJiggle.install.json") continue;
        string path = Path.Combine(target, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(file, path);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(file));
    }
}
string efmi = Efmi("duplicate");
string runtime = package.Install(package.Preview(efmi), requireStopped: false).RuntimeDirectory;
RuntimeSettingsStore store = new(runtime);
TouchSettings settings = store.Read().Settings with { Physics = new(0.18m, 0.035m, 0.85m) };
store.Apply(settings, store.Read().Fingerprint, requireStopped: false);
string old = Path.Combine(efmi, "Mods", "功能", "旧触摸");
CopyRuntime(runtime, old);
Directory.Move(Path.Combine(old, "shaders"), Path.Combine(old, "DISABLED_shaders"));
var oldFiles = Directory.EnumerateFiles(old, "*", SearchOption.AllDirectories)
    .ToDictionary(path => path, path => (Hash: Hash(path), Time: File.GetLastWriteTimeUtc(path)));
RuntimeLayout inspected = RuntimeLocationLocator.Inspect(efmi);
Check(inspected.Candidates.Count == 2 && inspected.Candidates.Single(item => item.Directory == old)
    .Problem!.Contains("shaders/"), "An enabled INI with renamed shader directory remains a conflicting runtime");
Reject(() => RuntimeLocationLocator.Resolve(efmi), "Strict discovery rejects incomplete duplicate");
Check(RuntimeLocationLocator.Discover(Path.Combine(runtime, "Configurator"), root, allowProblems: true)
    is { Problem: not null, EfmiDirectory: var found } && found == efmi,
    "UI discovery retains the actual broken workspace and its diagnosis");
LayoutRepairPreview preview = package.PreviewLayoutRepair(runtime);
Check(preview.QuarantineFiles.Length == 4 && preview.RuntimeDirectory == runtime,
    "Repair previews the four conflicting owned INIs");
LayoutRepairResult repaired = package.RepairLayout(preview, requireStopped: false);
Check(RuntimeLocationLocator.Resolve(efmi).RuntimeDirectory == runtime &&
    store.Read().Settings == settings, "Repair leaves one complete runtime and preserves settings");
Check(oldFiles.All(pair =>
{
    string path = File.Exists(pair.Key) ? pair.Key : Path.Combine(repaired.LayoutBackupDirectory,
        Path.GetRelativePath(efmi, pair.Key));
    return File.Exists(path) && Hash(path) == pair.Value.Hash && File.GetLastWriteTimeUtc(path) == pair.Value.Time;
}), "All previous files retain exact bytes and times, including quarantined entries");
Check(repaired.LayoutBackupDirectory.StartsWith(Path.Combine(efmi, "EndfieldJiggleBackups")) &&
    File.Exists(Path.Combine(repaired.LayoutBackupDirectory, "layout-transaction.json")),
    "Quarantine and manifest stay outside loaded Mods");
Reject(() => package.RepairLayout(preview, requireStopped: false), "Stale repair preview is rejected");

string brokenEfmi = Efmi("missing-shaders");
string broken = package.Install(package.Preview(brokenEfmi), requireStopped: false).RuntimeDirectory;
Directory.Move(Path.Combine(broken, "shaders"), Path.Combine(broken, "DISABLED_shaders"));
Reject(() => RuntimeLocationLocator.Resolve(brokenEfmi), "A single incomplete runtime is not reported healthy");
LayoutRepairPreview missingPreview = package.PreviewLayoutRepair(broken);
Check(missingPreview.MissingFiles.Any(name => name.StartsWith("shaders/")), "Repair lists absent shader resources");
LayoutRepairResult missingRepair = package.RepairLayout(missingPreview, requireStopped: false);
Check(RuntimeLocationLocator.Resolve(brokenEfmi).RuntimeDirectory == broken &&
    Directory.Exists(Path.Combine(broken, "DISABLED_shaders")), "Repair restores missing resources and preserves renamed files");
InstallablePackage.RestoreInstallation(broken, requireStopped: false);
Check(!Directory.Exists(Path.Combine(broken, "shaders")) || !Directory.EnumerateFiles(Path.Combine(broken, "shaders")).Any(),
    "Installer restore recovers the exact prior missing-resource state");
Check(InstallablePackage.HasRestorableInstallation(broken), "Prior installer receipt is restored");

string partial = Path.Combine(efmi, "Mods", "Orphan");
Directory.CreateDirectory(partial);
File.Copy(Path.Combine(runtime, "OutfitPreparation.ini"), Path.Combine(partial, "OutfitPreparation.ini"));
Check(RuntimeLocationLocator.Inspect(efmi).Candidates.Count == 2, "A preparation-only orphan is detected");
package.RepairLayout(package.PreviewLayoutRepair(runtime), requireStopped: false);
Check(RuntimeLocationLocator.Resolve(efmi).RuntimeDirectory == runtime, "Known partial orphan can be quarantined");
File.Copy(Path.Combine(runtime, "Passes.ini"), Path.Combine(partial, "Passes.ini"));
File.AppendAllText(Path.Combine(partial, "Passes.ini"), "\n; unknown partial change\n");
Reject(() => package.PreviewLayoutRepair(runtime), "Unverifiable partial INI is left untouched");
File.Move(Path.Combine(partial, "Passes.ini"), Path.Combine(partial, "Passes.ini.bak"));
File.WriteAllText(Path.Combine(runtime, "foreign.ini"), "[Constants]\nglobal $foreign = 1\n");
Reject(() => package.PreviewLayoutRepair(runtime), "Unknown active INI blocks repair");
File.Move(Path.Combine(runtime, "foreign.ini"), Path.Combine(runtime, "foreign.ini.bak"));

string rollbackEfmi = Efmi("rollback");
string rollbackTarget = package.Install(package.Preview(rollbackEfmi), requireStopped: false).RuntimeDirectory;
string rollbackOld = Path.Combine(rollbackEfmi, "Mods", "Previous");
CopyRuntime(rollbackTarget, rollbackOld);
string targetIni = Path.Combine(rollbackTarget, "Passes.ini");
File.AppendAllText(targetIni, "\n; rollback fixture\n");
LayoutRepairPreview rollbackPreview = package.PreviewLayoutRepair(rollbackTarget);
var before = Directory.EnumerateFiles(rollbackOld, "*.ini").ToDictionary(path => path,
    path => (Hash: Hash(path), Time: File.GetLastWriteTimeUtc(path)));
using (FileStream locked = new(targetIni, FileMode.Open, FileAccess.Read, FileShare.Read))
    Reject(() => package.RepairLayout(rollbackPreview, requireStopped: false), "Failed primary install triggers rollback");
Check(before.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value.Hash &&
    File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Time), "Failure restores every quarantined entry");

if (args.Length == 2)
{
    string fixture = Path.GetFullPath(args[1]);
    string reports = Path.GetFullPath("reports") + Path.DirectorySeparatorChar;
    if (!fixture.StartsWith(reports, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Loader repair fixture must stay under reports.");
    string chosen = Path.Combine(fixture, "Mods", "EndfieldJiggleEFMI-v0.2.6-drop-in-win64");
    LayoutRepairResult result = package.RepairLayout(package.PreviewLayoutRepair(chosen), requireStopped: false);
    Check(RuntimeLocationLocator.Resolve(fixture).RuntimeDirectory == chosen, "Real-loader reproduction fixture repaired");
    Console.WriteLine("LOADER_FIXTURE_BACKUP=" + result.LayoutBackupDirectory);
}
File.WriteAllText(Path.Combine(root, "test-report.json"), JsonSerializer.Serialize(new
    { passed = checks.Count, checks, inGameVerified = false, liveInstallationModified = false },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {checks.Count} layout repair checks. {root}");
