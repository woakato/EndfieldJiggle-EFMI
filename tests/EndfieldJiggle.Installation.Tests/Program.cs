using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EndfieldJiggle.Configurator.Core;

if (args.Length < 1) throw new ArgumentException("Pass the v0.2.1 Mod ZIP.");
string root = Path.GetFullPath(Path.Combine("reports", "installation-tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
List<string> checks = [];
void Check(bool success, string name)
{
    if (!success) throw new Exception("FAIL: " + name);
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
    throw new Exception("FAIL: expected rejection: " + name);
}
string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Efmi(string name)
{
    string path = Path.Combine(root, name, "EFMI");
    Directory.CreateDirectory(Path.Combine(path, "Mods"));
    File.WriteAllText(Path.Combine(path, "d3dx.ini"), "[Include]\ninclude_recursive = Mods\nexclude_recursive = DISABLED*\n");
    File.WriteAllText(Path.Combine(path, "d3d11.dll"), "fixture-not-a-loader");
    File.WriteAllText(Path.Combine(path, "Mods", "unrelated.ini"), "; protected fixture");
    return path;
}

InstallablePackage package = InstallablePackage.FromRuntimeArchive(File.ReadAllBytes(args[0]));
string efmi = Efmi("fresh");
Dictionary<string, (string Hash, long Time)> protectedFiles = new();
foreach (string file in new[] { "d3dx.ini", "d3d11.dll", "Mods/unrelated.ini" })
{
    string path = Path.Combine(efmi, file);
    protectedFiles.Add(path, (Digest(path), File.GetLastWriteTimeUtc(path).Ticks));
}
InstallationPreview preview = package.Preview(efmi);
Check(!preview.IsUpgrade && preview.FileCount == 14, "Fresh preview owns the reviewed 14 runtime files");
InstallationResult installed = package.Install(preview, requireStopped: false);
Check(new RuntimeSettingsStore(installed.RuntimeDirectory).Read().Settings.DefaultEnabled == false,
    "Fresh installation starts disabled");
Check(InstallablePackage.HasRestorableInstallation(installed.RuntimeDirectory) &&
      installed.BackupDirectory.StartsWith(Path.Combine(efmi, "EndfieldJiggleBackups")),
    "Recovery record and backups stay outside recursively loaded Mods");
Check(Directory.EnumerateDirectories(Path.Combine(efmi, "Mods")).Count() == 1,
    "Installation creates only one runtime Mod");
foreach ((string path, (string hash, long time)) in protectedFiles)
    Check(Digest(path) == hash && File.GetLastWriteTimeUtc(path).Ticks == time,
        "Global loader and unrelated files untouched: " + Path.GetFileName(path));
Reject(() => package.Install(preview, requireStopped: false), "Stale install preview is rejected");
RuntimeSettingsStore store = new(installed.RuntimeDirectory);
RuntimeSettingsSnapshot original = store.Read();
TouchSettings changed = original.Settings with
{
    DefaultEnabled = true,
    Toggle = new KeyBinding("VK_F8", Alt: true),
    Physics = new PhysicsSettings(0.22m, 0.03m, 1.2m),
};
store.Apply(changed, original.Fingerprint, requireStopped: false);
Dictionary<string, (string Hash, long Time)> configured = new();
foreach (string name in new[] { "EndfieldJiggle.ini", "Passes.ini", "Outfits.ini" })
{
    string path = Path.Combine(store.Root, name);
    configured[path] = (Digest(path), File.GetLastWriteTimeUtc(path).Ticks);
}
InstallationResult upgrade = package.Install(package.Preview(efmi), requireStopped: false);
Check(upgrade.SettingsPreserved && new RuntimeSettingsStore(store.Root).Read().Settings == changed,
    "Upgrade preserves default-enabled state, custom keys and physics");
InstallablePackage.RestoreInstallation(store.Root, requireStopped: false);
foreach ((string path, (string hash, long time)) in configured)
    Check(Digest(path) == hash && File.GetLastWriteTimeUtc(path).Ticks == time,
        "Restore recovers original configured bytes and timestamp: " + Path.GetFileName(path));

string tamper = Efmi("tamper");
InstallationResult tampered = package.Install(package.Preview(tamper), requireStopped: false);
string shader = Directory.EnumerateFiles(Path.Combine(tampered.RuntimeDirectory, "shaders"), "*.hlsl").First();
File.AppendAllText(shader, "\n; external edit");
Reject(() => package.Preview(tamper), "Unknown shader modifications block upgrade");
Reject(() => InstallablePackage.RestoreInstallation(tampered.RuntimeDirectory, requireStopped: false),
    "Externally modified installed files block recovery");

string freshRestore = Efmi("restore-fresh");
InstallationResult fresh = package.Install(package.Preview(freshRestore), requireStopped: false);
InstallablePackage.RestoreInstallation(fresh.RuntimeDirectory, requireStopped: false);
Check(!File.Exists(Path.Combine(fresh.RuntimeDirectory, "EndfieldJiggle.ini")) &&
      File.Exists(Path.Combine(freshRestore, "Mods", "unrelated.ini")),
    "Fresh recovery removes only owned installed files");

string locked = Efmi("locked");
string target = Path.Combine(locked, "Mods", "EndfieldJiggleEFMI");
Directory.CreateDirectory(target);
using (ZipArchive archive = ZipFile.OpenRead(args[0]))
{
    const string marker = "/Mods/EndfieldJiggleEFMI/";
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        int offset = entry.FullName.IndexOf(marker, StringComparison.Ordinal);
        if (offset < 0 || entry.FullName.EndsWith('/')) continue;
        string name = entry.FullName[(offset + marker.Length)..];
        if (name.Contains("..") || name.Contains("Configurator/")) throw new Exception("Unsafe fixture archive");
        string path = Path.Combine(target, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Stream input = entry.Open();
        using FileStream output = File.Create(path);
        input.CopyTo(output);
    }
}
string endfield = Path.Combine(target, "EndfieldJiggle.ini");
string passes = Path.Combine(target, "Passes.ini");
File.WriteAllText(endfield, File.ReadAllText(endfield).Replace("global $enabled = 0", "global $enabled = 1"),
    new UTF8Encoding(false));
File.WriteAllText(passes, File.ReadAllText(passes).Replace("if !$outfit_seen &&", "if "),
    new UTF8Encoding(false));
string endfieldHash = Digest(endfield);
DateTime endfieldTime = File.GetLastWriteTimeUtc(endfield);
InstallationPreview lockedPreview = package.Preview(locked);
using (FileStream blocked = new(passes, FileMode.Open, FileAccess.Read, FileShare.Read))
    Reject(() => package.Install(lockedPreview, requireStopped: false), "Locked target triggers transaction rollback");
Check(Digest(endfield) == endfieldHash && File.GetLastWriteTimeUtc(endfield) == endfieldTime,
    "Failed upgrade restores prior runtime bytes and timestamp");
string nested = Path.Combine(target, "nested");
Directory.CreateDirectory(nested);
File.WriteAllText(Path.Combine(nested, "unexpected.ini"), "[Constants]\nglobal $x = 1");
Reject(() => package.Preview(locked), "Unknown nested active INI blocks upgrade");
File.Move(Path.Combine(nested, "unexpected.ini"), Path.Combine(nested, "unexpected.ini.bak"));
File.WriteAllText(Path.Combine(target, "unexpected.ini"), "[Constants]\nglobal $x = 1");
Reject(() => package.Preview(locked), "Unknown active runtime INI is not overwritten");
Reject(() => package.Preview(root), "Non-EFMI destination is rejected");

File.WriteAllText(Path.Combine(root, "test-report.json"), JsonSerializer.Serialize(new
{
    passed = checks.Count, checks, liveInstallationModified = false, inGameVerified = false,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {checks.Count} installer checks. Report: {root}");
