using System.Text.Json;
using EndfieldJiggle.Configurator.Core;

if (args.Length != 1) throw new ArgumentException("Pass the runtime ZIP.");
string fixture = Path.GetFullPath(Path.Combine("reports", "discovery-tests", Guid.NewGuid().ToString("N")));
string efmi = Path.Combine(fixture, "Launcher", "Packages", "EFMI");
Directory.CreateDirectory(Path.Combine(efmi, "Mods"));
File.WriteAllText(Path.Combine(efmi, "d3d11.dll"), "isolated-fixture");
File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
    "[Hunting]\nreload_config = no_modifiers VK_F10\nshow_original = ctrl no_shift no_alt VK_F11\n");
Directory.CreateDirectory(Path.Combine(efmi, "Core", "EFMI"));
File.WriteAllText(Path.Combine(efmi, "Core", "EFMI", "KeyBindings.ini"),
    "[KeyToggleMods]\nkey = no_modifiers VK_F11\nrun = CommandListToggleMods\n");
InstallablePackage package = InstallablePackage.FromRuntimeArchive(File.ReadAllBytes(args[0]));
string runtime = package.Install(package.Preview(efmi), requireStopped: false).RuntimeDirectory;
int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
}
Check(RuntimeLocationLocator.Resolve(efmi).RuntimeDirectory == runtime, "EFMI root discovers installed runtime");
Check(RuntimeLocationLocator.Resolve(Path.Combine(efmi, "Mods")).RuntimeDirectory == runtime, "Mods root resolves");
string exeDirectory = Path.Combine(runtime, "Configurator");
Directory.CreateDirectory(exeDirectory);
Check(RuntimeLocationLocator.Discover(exeDirectory, fixture)?.RuntimeDirectory == runtime,
    "portable EXE resolves its own location without remembered paths");

string versionedEfmi = Path.Combine(fixture, "VersionedEFMI");
string versionedRuntime = Path.Combine(versionedEfmi, "Mods", "EndfieldJiggleEFMI-v0.2.3-drop-in-win64");
Directory.CreateDirectory(versionedRuntime);
File.Copy(Path.Combine(efmi, "d3dx.ini"), Path.Combine(versionedEfmi, "d3dx.ini"));
File.Copy(Path.Combine(efmi, "d3d11.dll"), Path.Combine(versionedEfmi, "d3d11.dll"));
foreach (string file in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories))
{
    string relative = Path.GetRelativePath(runtime, file);
    if (relative == "EndfieldJiggle.install.json") continue;
    string destination = Path.Combine(versionedRuntime, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(file, destination);
    File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
}
Check(RuntimeLocationLocator.Discover(Path.Combine(versionedRuntime, "Configurator"), fixture)
        ?.RuntimeDirectory == versionedRuntime,
    "flat version-named package discovers its actual runtime");
InstallationPreview versionedPreview = package.Preview(versionedEfmi);
Check(versionedPreview.RuntimeDirectory == versionedRuntime && versionedPreview.IsUpgrade,
    "upgrade targets the version-named runtime instead of creating a duplicate");
package.Install(versionedPreview, requireStopped: false);
Check(Directory.EnumerateDirectories(Path.Combine(versionedEfmi, "Mods")).Count() == 1,
    "version-named upgrade keeps one runtime directory");
InstallablePackage.RestoreInstallation(versionedRuntime, requireStopped: false);
Check(File.Exists(Path.Combine(versionedRuntime, "Passes.ini")),
    "version-named upgrade restores its original installation");
Check(ModLibraryStore.Scan(Path.Combine(versionedEfmi, "Mods")).Count == 0,
    "version-named touch runtime is excluded from the outfit library");

string holding = Path.Combine(fixture, "runtime-staging");
Directory.Move(runtime, holding);
Directory.CreateDirectory(runtime);
string nested = Path.Combine(runtime, "EndfieldJiggleEFMI");
Directory.Move(holding, nested);
RuntimeLocation location = RuntimeLocationLocator.Resolve(efmi);
Check(location.RuntimeDirectory == nested && location.Nested, "double nested drop-in is recognized");
Check(RuntimeLocationLocator.Discover(Path.Combine(nested, "Configurator"), fixture)?.RuntimeDirectory == nested,
    "nested EXE finds actual INI directory");
string outfit = Path.Combine(efmi, "Mods", "FixtureOutfit");
Directory.CreateDirectory(outfit);
File.WriteAllText(Path.Combine(outfit, "body.ini"), "namespace = Sample\n[TextureOverrideBody]\ndrawindexed = 3, 0, 0\n");
Check(new OutfitAdapterStore(outfit, nested).Inspect().SupportedDraws.Count == 1,
    "nested runtime supports adaptation in the actual Mods root");

string launcher = Path.Combine(fixture, "Launcher");
File.WriteAllText(Path.Combine(launcher, "XXMI Launcher Config.json"), JsonSerializer.Serialize(new
{
    Importers = new { EFMI = new { Importer = new { importer_folder = "Packages/EFMI" } } },
}));
Check(RuntimeLocationLocator.Discover(Path.Combine(fixture, "Downloaded"), fixture, launcherDirectories: [launcher])
    ?.RuntimeDirectory == nested, "launcher JSON uses a structured relative importer path");
EfmiInputStatus input = EfmiInputDiagnostics.Read(efmi);
Check(input.ModsDisplay == "no_modifiers VK_F11" && input.ShowOriginal == "ctrl no_shift no_alt VK_F11" &&
    input.Reload == "no_modifiers VK_F10", "Mods Display and show_original are separate from touch controls");

string duplicate = Path.Combine(efmi, "Mods", "EndfieldJiggleDuplicate");
Directory.CreateDirectory(duplicate);
foreach (string name in new[] { "EndfieldJiggle.ini", "Passes.ini", "Outfits.ini" })
    File.Copy(Path.Combine(nested, name), Path.Combine(duplicate, name));
bool rejectedDuplicate = false;
try { RuntimeLocationLocator.Resolve(efmi); }
catch (InvalidDataException) { rejectedDuplicate = true; }
Check(rejectedDuplicate, "multiple active runtimes are rejected");
using CancellationTokenSource cancelled = new();
cancelled.Cancel();
bool scanCancelled = false;
try { ModLibraryStore.Scan(Path.Combine(efmi, "Mods"), cancelled.Token); }
catch (OperationCanceledException) { scanCancelled = true; }
Check(scanCancelled, "library scans honor cancellation");
bool conflictsCancelled = false;
try
{
    ModLibraryStore.FindConflicts(efmi, nested, new RuntimeSettingsStore(nested).Read().Settings, cancelled.Token);
}
catch (OperationCanceledException) { conflictsCancelled = true; }
Check(conflictsCancelled, "key scans honor cancellation");
Console.WriteLine($"PASS: {checks} discovery, nested-runtime, input-diagnostic and cancellation checks. {fixture}");
