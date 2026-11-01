using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EndfieldJiggle.Configurator.Core;

if (args.Length < 6)
    throw new ArgumentException("Mod ZIP, EFMI directory, dependency Mod, loader host, output directory, source outfits...");
string output = Path.GetFullPath(args[4]);
if (Directory.Exists(output)) throw new IOException("Preserve existing compatibility evidence.");
Directory.CreateDirectory(output);
string efmi = Path.Combine(output, "EFMI");
Directory.CreateDirectory(Path.Combine(efmi, "Mods"));
Directory.CreateDirectory(Path.Combine(efmi, "ShaderFixes"));
List<string> checks = [];
List<object> results = [];
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks.Add(message);
}
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
Dictionary<string, (string Hash, long Time)> Snapshot(string path) =>
    Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToDictionary(
        file => Path.GetRelativePath(path, file), file => (Hash(file), File.GetLastWriteTimeUtc(file).Ticks),
        StringComparer.OrdinalIgnoreCase);
void CopyTree(string source, string target)
{
    if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source refused.");
    Directory.CreateDirectory(target);
    foreach (string child in Directory.EnumerateDirectories(source))
        CopyTree(child, Path.Combine(target, Path.GetFileName(child)));
    foreach (string file in Directory.EnumerateFiles(source))
    {
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source refused.");
        string destination = Path.Combine(target, Path.GetFileName(file));
        File.Copy(file, destination);
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
    }
}
foreach (string name in new[] { "Core", "qaqm" }) CopyTree(Path.Combine(args[1], name), Path.Combine(efmi, name));
CopyTree(args[2], Path.Combine(efmi, "Mods", "Dependency"));
foreach (string name in new[] { "d3d11.dll", "d3dcompiler_47.dll" }) File.Copy(Path.Combine(args[1], name), Path.Combine(efmi, name));
string loader = Path.Combine(efmi, "loader_host.exe");
File.Copy(args[3], loader);
File.WriteAllText(Path.Combine(efmi, "d3dx.ini"), "[Include]\ninclude_recursive = Mods\nexclude_recursive = DISABLED*\n");
InstallablePackage package = InstallablePackage.FromRuntimeArchive(File.ReadAllBytes(args[0]));
string runtime = package.Install(package.Preview(efmi), requireStopped: false).RuntimeDirectory;
List<(string Source, string Target, Dictionary<string, (string Hash, long Time)> Files)> outfits = [];
foreach (string source in args.Skip(5).Select(Path.GetFullPath))
{
    Dictionary<string, (string Hash, long Time)> files = Snapshot(source);
    string target = Path.Combine(efmi, "Mods", "Outfit" + outfits.Count);
    CopyTree(source, target);
    outfits.Add((source, target, files));
}
File.WriteAllText(Path.Combine(efmi, "d3dx.ini"), """
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
    """, new UTF8Encoding(false));
string[] RunLoader(string round)
{
    string log = Path.Combine(efmi, "d3d11_log.txt");
    if (File.Exists(log)) File.Move(log, Path.Combine(output, "before-" + round + "-d3d11_log.txt"));
    ProcessStartInfo start = new(loader)
    {
        WorkingDirectory = efmi, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
    };
    start.ArgumentList.Add(Path.Combine(efmi, "d3d11.dll"));
    start.ArgumentList.Add(Path.Combine(runtime, "shaders", "pick_1479b2b594b9c91a.vs_5_0.8000.bin"));
    using Process process = Process.Start(start) ?? throw new IOException("Could not start isolated loader.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(60_000))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        throw new TimeoutException("Isolated loader timed out.");
    }
    File.WriteAllText(Path.Combine(output, round + "-host.txt"), stdout.Result + stderr.Result);
    string text = File.ReadAllText(log);
    File.Copy(log, Path.Combine(output, round + "-d3d11_log.txt"));
    Check(process.ExitCode == 0, "Actual EFMI loader exits normally: " + round);
    if (round == "adapted") Check(text.Contains("commandlistejoutfit", StringComparison.OrdinalIgnoreCase),
        "Actual EFMI loader parses the generated bridge helpers");
    return Regex.Matches(text, @"(?im)^.*(?:Unrecognised entry|Syntax Error|Error opening|Failed to|WARNING:).*$")
        .Select(match => match.Value.TrimEnd()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
Console.WriteLine("Loading original outfits in isolation...");
string[] baseline = RunLoader("baseline");
foreach ((string source, string target, Dictionary<string, (string Hash, long Time)> files) in outfits)
{
    OutfitAdapterStore adapter = new(target, runtime);
    OutfitAdapterInspection before = adapter.Inspect();
    Check(!before.AlreadyAdapted && before.SupportedDraws.Count > 0 && before.MissingRequiredResources.Count == 0,
        "Original outfit has eligible mesh draws and complete critical dependencies: " + Path.GetFileName(source));
    Check(before.AuxiliaryDraws.All(draw => !draw.Supported), "Auxiliary draws stay out of the bridge: " + target);
    Dictionary<string, byte[]> iniFiles = Directory.EnumerateFiles(target, "*.ini", SearchOption.AllDirectories)
        .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
    adapter.Apply(before.Fingerprint, requireStopped: false, allowMissingTextures: true);
    OutfitAdapterInspection adapted = adapter.Inspect();
    string[] TextureIdentities(OutfitAdapterInspection inspection) => inspection.MissingTextureResources
        .Select(warning => Regex.Replace(warning, @":\d+ \[", " [")).Order(StringComparer.Ordinal).ToArray();
    Check(adapted.AlreadyAdapted && TextureIdentities(adapted).SequenceEqual(TextureIdentities(before)) &&
        adapted.AuxiliaryDraws.Count == before.AuxiliaryDraws.Count, "Author resource warnings persist after adaptation: " + target);
    Dictionary<string, byte[]> adaptedBytes = iniFiles.Keys.ToDictionary(path => path, File.ReadAllBytes);
    adapter.Restore(requireStopped: false);
    Check(iniFiles.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
        "Restore returns every original INI byte: " + target);
    OutfitAdapterInspection restored = adapter.Inspect();
    adapter.Apply(restored.Fingerprint, requireStopped: false, allowMissingTextures: true);
    Check(adaptedBytes.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
        "Re-adaptation is byte-for-byte deterministic: " + target);
    Check(files.Where(pair => !Path.GetExtension(pair.Key).Equals(".ini", StringComparison.OrdinalIgnoreCase))
        .All(pair => Hash(Path.Combine(target, pair.Key)) == pair.Value.Hash),
        "All original non-INI resources are preserved: " + target);
    results.Add(new
    {
        name = Path.GetFileName(source), supported = before.SupportedDraws.Count,
        auxiliary = before.AuxiliaryDraws.Count, unsupported = before.UnsupportedDraws.Count,
        missingTextures = before.MissingTextureResources, before.MissingRequiredResources,
        authorResourcesSubstituted = false, restoreExact = true,
    });
}
Console.WriteLine("Loading generically adapted outfits in isolation...");
string[] finalDiagnostics = RunLoader("adapted");
Check(!finalDiagnostics.Except(baseline, StringComparer.Ordinal).Any(), "Generic adaptation introduces no new loader diagnostics");
foreach (var (source, _, files) in outfits)
    Check(files.All(pair => Hash(Path.Combine(source, pair.Key)) == pair.Value.Hash &&
        File.GetLastWriteTimeUtc(Path.Combine(source, pair.Key)).Ticks == pair.Value.Time),
        "Live author source bytes and timestamps are untouched: " + Path.GetFileName(source));
File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(new
{
    passed = checks.Count, checks, outfits = results, baselineDiagnostics = baseline, finalDiagnostics,
    liveInstallationModified = false, inGameVerified = false,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {checks.Count} real-outfit compatibility checks. Report: {output}");
