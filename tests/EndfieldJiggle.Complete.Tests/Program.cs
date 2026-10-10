using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EndfieldJiggle.Configurator.Core;

if (args.Length != 5) throw new ArgumentException("Mod ZIP, read-only source outfit, EFMI, RabbitFX directory, loader host.");
string output = Path.GetFullPath(Path.Combine("reports", "complete-tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(output);
string source = Path.GetFullPath(args[1]);
string sourceEfmi = Path.GetFullPath(args[2]);
string dependencies = Path.GetFullPath(args[3]);
string efmi = Path.Combine(output, "FixtureEFMI");
Directory.CreateDirectory(Path.Combine(efmi, "Mods"));
Directory.CreateDirectory(Path.Combine(efmi, "ShaderFixes"));
List<string> checks = [];
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks.Add(name);
}
Dictionary<string, (string Hash, long Time)> Snapshot(string root) => Directory.EnumerateFiles(root, "*",
    SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path),
    path => (Hash(File.ReadAllBytes(path)), File.GetLastWriteTimeUtc(path).Ticks), StringComparer.OrdinalIgnoreCase);
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
void CopyTree(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (string child in Directory.EnumerateDirectories(from))
    {
        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source");
        CopyTree(child, Path.Combine(to, Path.GetFileName(child)));
    }
    foreach (string file in Directory.EnumerateFiles(from))
    {
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source");
        string target = Path.Combine(to, Path.GetFileName(file));
        File.Copy(file, target);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
    }
}
Dictionary<string, (string Hash, long Time)> originalSource = Snapshot(source);
string outfit = Path.Combine(efmi, "Mods", "SelectedOutfit");
CopyTree(source, outfit);
Dictionary<string, (string Hash, long Time)> originalFixture = Snapshot(outfit);
CopyTree(Path.Combine(sourceEfmi, "Core"), Path.Combine(efmi, "Core"));
CopyTree(Path.Combine(sourceEfmi, "qaqm"), Path.Combine(efmi, "qaqm"));
CopyTree(dependencies, Path.Combine(efmi, "Mods", "RabbitFXDependency"));
foreach (string name in new[] { "d3d11.dll", "d3dcompiler_47.dll" })
    File.Copy(Path.Combine(sourceEfmi, name), Path.Combine(efmi, name));
string loader = Path.Combine(efmi, "loader_host.exe");
File.Copy(args[4], loader);
string configuration = """
[Include]
include = qaqm\qaqm_includer.ini
include = Core\EFMI\main.ini
include_recursive = Mods
exclude_recursive = DISABLED*
exclude_recursive = desktop.ini
[Logging]
log_level = debug
calls = 1
debug = 1
unbuffered = 1
[ClearRenderTargetView]
[ClearDepthStencilView]
[ClearUnorderedAccessViewUint]
[ClearUnorderedAccessViewFloat]
[System]
proxy_d3d11 = C:\Windows\System32\d3d11.dll
skip_early_includes_load = 0
allow_platform_update = 1
allow_check_interface = 1
[Rendering]
override_directory = ShaderFixes
cache_directory = ShaderCache
storage_directory = ShaderFromGame
shader_hash = 3dmigoto
texture_hash = 0
track_region_hashes = 1
cache_shaders = 0
ini_params = 120
assemble_signature_comments = 1
disassemble_undecipherable_custom_data = 1
patch_assembly_cb_offsets = 1
recursive_include = 1
export_shaders = 0
[Hunting]
hunting = 0
[Stereo]
automatic_mode = 0
""";
File.WriteAllText(Path.Combine(efmi, "d3dx.ini"), configuration, new UTF8Encoding(false));
InstallablePackage package = InstallablePackage.FromRuntimeArchive(File.ReadAllBytes(args[0]));
InstallationResult installed = package.Install(package.Preview(efmi), requireStopped: false);
string runtime = installed.RuntimeDirectory;
string shader = Path.Combine(runtime, "shaders", "pick_1479b2b594b9c91a.vs_5_0.8000.bin");
List<object> parserRounds = [];
int RunLoader(string mode)
{
    string logPath = Path.Combine(efmi, "d3d11_log.txt");
    if (File.Exists(logPath))
        File.Move(logPath, Path.Combine(output, "before-" + mode + "-d3d11_log.txt"));
    ProcessStartInfo start = new(loader)
    {
        WorkingDirectory = efmi, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
    };
    start.ArgumentList.Add(Path.Combine(efmi, "d3d11.dll"));
    start.ArgumentList.Add(shader);
    using Process process = Process.Start(start) ?? throw new IOException("Could not start isolated native host.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(60_000))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        throw new TimeoutException("Isolated native loader did not exit.");
    }
    File.WriteAllText(Path.Combine(output, mode + "-host.txt"), stdout.Result + stderr.Result);
    string log = File.ReadAllText(logPath);
    File.Copy(logPath, Path.Combine(output, mode + "-d3d11_log.txt"));
    string[] diagnostics = Regex.Matches(log,
        @"(?im)^(?:.*(?:Unrecognised entry|Syntax Error|Error opening|Failed to|WARNING:).*)$")
        .Select(match => match.Value).Distinct().ToArray();
    bool uiParsed = log.Contains(@"commandlist\mods\selectedoutfit\k\1.ini\click", StringComparison.OrdinalIgnoreCase);
    parserRounds.Add(new { mode, exitCode = process.ExitCode, diagnostics, uiParsed });
    Check(process.ExitCode == 0, "Actual EFMI loader exited normally: " + mode);
    Check(uiParsed, "Outfit UI Click command list parsed: " + mode);
    return diagnostics.Length;
}

QaqmRecoveryStore state = new(outfit, efmi);
QaqmRecoveryPlan plan = state.Inspect();
Check(plan.Issues.Count == 0, "Current real outfit state evidence resolves: " +
    string.Join(" | ", plan.Issues.Select(issue => issue.Message)));
Check(plan.MissingHosts.Count == 1 && plan.LoadedHosts.Any(host => host.Id == "3f2e579b50a9"),
    "Real missing outfit host distinguished from already-loaded UI host");
int baseline = RunLoader("baseline");
Check(baseline > 0, "Baseline missing declarations reproduce loader diagnostics");
state.Apply(plan.Fingerprint, requireStopped: false);
Check(state.Inspect().MissingHosts.Count == 0 && state.Inspect().Issues.Count == 0,
    "Generic recovery resolves real outfit references without fixed Bridge IDs");
OutfitAdapterStore adapter = new(outfit, runtime);
OutfitAdapterInspection inspection = adapter.Inspect();
Check(inspection.SupportedDraws.Count > 0 && inspection.MissingRequiredResources.Count == 0,
    "Real original outfit has eligible indexed draws and complete required resources");
adapter.Apply(inspection.Fingerprint, requireStopped: false);
Check(adapter.Inspect().AlreadyAdapted && Directory.EnumerateDirectories(Path.Combine(efmi, "Mods")).Count() == 3,
    "Real outfit adapted in place without a duplicate EJTouch Mod");
foreach ((string relative, (string hash, long time)) in originalFixture.Where(pair =>
             !Path.GetExtension(pair.Key).Equals(".ini", StringComparison.OrdinalIgnoreCase)))
{
    string path = Path.Combine(outfit, relative);
    Check(Hash(File.ReadAllBytes(path)) == hash && File.GetLastWriteTimeUtc(path).Ticks == time,
        "Non-INI outfit resource unchanged: " + relative);
}
int adapted = RunLoader("adapted");
Check(adapted == 0, "Generated in-place wrappers plus recovered state load without diagnostics");
adapter.Restore(requireStopped: false);
int restored = RunLoader("restored");
Check(restored == 0, "Restored original outfit with recovered state loads without diagnostics");
foreach ((string relative, (string hash, long time)) in originalFixture)
{
    string path = Path.Combine(outfit, relative);
    Check(Hash(File.ReadAllBytes(path)) == hash && File.GetLastWriteTimeUtc(path).Ticks == time,
        "Restored original outfit file bytes and timestamp: " + relative);
}
state.Restore(requireStopped: false);
foreach ((string relative, (string hash, long time)) in originalSource)
{
    string path = Path.Combine(source, relative);
    Check(Hash(File.ReadAllBytes(path)) == hash && File.GetLastWriteTimeUtc(path).Ticks == time,
        "Live source untouched: " + relative);
}
File.WriteAllText(Path.Combine(output, "test-report.json"), JsonSerializer.Serialize(new
{
    passed = checks.Count, checks, supportedDraws = inspection.SupportedDraws.Count,
    unsupportedDraws = inspection.UnsupportedDraws.Count, baseline, adapted, restored, parserRounds,
    liveInstallationModified = false, inGameVerified = false, inProcessHotReloadVerified = false,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {checks.Count} complete workflow checks. Report: {output}");
