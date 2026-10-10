using System.Security.Cryptography;
using System.Text;
using EndfieldJiggle.Configurator.Core;

string repository = Path.GetFullPath(args.Length == 0 ? Environment.CurrentDirectory : args[0]);
string fixture = Path.Combine(repository, "reports", "outfit-adapter-tests",
    DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
string mods = Path.Combine(fixture, "EFMI", "Mods");
string outfit = Path.Combine(mods, "FixtureOutfit");
string runtime = Path.Combine(mods, "EndfieldJiggleEFMI");
Directory.CreateDirectory(outfit);
Directory.CreateDirectory(runtime);
string runtimeFixture = Path.Combine(repository, "reports", "universal", "build-20261008-174720-527",
    "package", "Mods", "EndfieldJiggleEFMI");
foreach (string name in new[] { "EndfieldJiggle.ini", "Passes.ini", "Outfits.ini" })
    File.Copy(Path.Combine(runtimeFixture, name), Path.Combine(runtime, name));

string outfitIni = """
; This ordinary note mentions EJ-OUTFIT/1 without being an adapter marker.
namespace = Fixture

[TextureOverrideBody]
if $variant == 0
    drawindexedinstanced = 3, INSTANCE_COUNT, 9, -1, FIRST_INSTANCE ; keep comment
else
    drawindexed = auto
endif
post drawindexed = 3, 0, 0

[ResourceBody]
filename = missing-unreferenced.dds
include_recursive = includes
""".Replace("\n", "\r\n", StringComparison.Ordinal);
byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(outfitIni)];
string outfitPath = Path.Combine(outfit, "body.ini");
File.WriteAllBytes(outfitPath, original);
Directory.CreateDirectory(Path.Combine(outfit, "includes"));
byte[] includedOriginal = Encoding.UTF8.GetBytes(
    "namespace = Fixture\n[TextureOverrideIncluded]\ndrawindexed = 3, 0, 0\n");
string includedPath = Path.Combine(outfit, "includes", "included.ini");
File.WriteAllBytes(includedPath, includedOriginal);
File.WriteAllText(Path.Combine(outfit, "unsafe-local.ini"), """
namespace = LocalStage
[TextureOverrideCaller]
run = CommandListSetStage
drawindexed = 3, 0, 0
[CommandListSetStage]
vs = custom.hlsl
""");
File.WriteAllText(Path.Combine(outfit, "unsafe-cross-caller.ini"), """
namespace = CallerNs
[TextureOverrideCrossCaller]
run = CommandList\StageNs\SetStage
drawindexed = 3, 0, 0
""");
File.WriteAllText(Path.Combine(outfit, "unsafe-cross-stage.ini"), """
namespace = StageNs
[CommandListSetStage]
post vs = custom.hlsl
""");
File.WriteAllText(Path.Combine(outfit, "unsafe-post-run.ini"), """
namespace = PostRunNs
[TextureOverridePostCaller]
post run = CommandListSetStage
drawindexed = 3, 0, 0
[CommandListSetStage]
vs = custom.hlsl
""");
File.WriteAllText(Path.Combine(outfit, "custom.hlsl"), "// isolated dependency fixture\n");

OutfitAdapterStore store = new(outfit, runtime);
OutfitAdapterInspection inspection = store.Inspect();
Check(inspection.Draws.Count == 7, "indexed draw scan returns supported and unsupported draws");
Check(!inspection.AlreadyAdapted, "plain comment mentions do not trigger already-adapted detection");
Check(inspection.SupportedDraws.Count == 2 &&
    inspection.SupportedDraws.Single(draw => draw.SourceFile == "body.ini").FirstIndex == "9" &&
    inspection.SupportedDraws.Single(draw => draw.SourceFile == "body.ini").BaseVertex == "-1",
    "explicit indexed range is preserved");
Check(inspection.UnsupportedDraws.Any(draw => draw.Command.Contains("drawindexed = auto", StringComparison.Ordinal) &&
    draw.Reason == "explicit-index-range-required"), "auto draw is declined");
Check(inspection.UnsupportedDraws.Any(draw => draw.Command.Contains("post drawindexed", StringComparison.Ordinal) &&
    draw.Reason == "unsupported-draw-command"), "post indexed draws are never wrapped");
Check(inspection.UnsupportedDraws.Any(draw => draw.SourceFile == "unsafe-local.ini" &&
    draw.Command.Contains("drawindexed", StringComparison.Ordinal) && draw.Reason == "custom-vertex-stage"),
    "local caller draw is unsafe when a called command list assigns a vertex stage");
Check(inspection.UnsupportedDraws.Any(draw => draw.SourceFile == "unsafe-cross-caller.ini" &&
    draw.Reason == "custom-vertex-stage"), "cross-file namespace stage assignment propagates to caller draw");
Check(inspection.UnsupportedDraws.Any(draw => draw.SourceFile == "unsafe-post-run.ini" &&
    draw.Reason == "custom-vertex-stage"), "post run stage dependency is conservatively recognized");
Check(inspection.MissingUnreferencedResources.Count == 1,
    "unreferenced missing resources are warning-only");
Check(!inspection.MissingRequiredResources.Any(item => item.Contains("includes", StringComparison.OrdinalIgnoreCase)),
    "include_recursive accepts an existing directory");

string disabledOutfit = Path.Combine(mods, "DISABLED_FixtureOutfit");
Directory.CreateDirectory(disabledOutfit);
File.WriteAllBytes(Path.Combine(disabledOutfit, "body.ini"), original);
OutfitAdapterStore disabledStore = new(disabledOutfit, runtime);
bool rejectedDisabledHotWrite = false;
try { disabledStore.Apply(disabledStore.Inspect().Fingerprint, requireStopped: false); }
catch (InvalidOperationException) { rejectedDisabledHotWrite = true; }
Check(rejectedDisabledHotWrite, "hot adaptation refuses a disabled Mods subtree");

string driftOutfit = Path.Combine(mods, "DriftOutfit");
Directory.CreateDirectory(driftOutfit);
File.WriteAllText(Path.Combine(driftOutfit, "drift.ini"),
    "namespace = Drift\n[TextureOverrideBody]\ndrawindexed = 3, 0, 0\n");
OutfitAdapterStore driftStore = new(driftOutfit, runtime);
OutfitAdapterInspection driftInspection = driftStore.Inspect();
string passesPath = Path.Combine(runtime, "Passes.ini");
byte[] passesBytes = File.ReadAllBytes(passesPath);
DateTime passesTime = File.GetLastWriteTimeUtc(passesPath);
File.AppendAllText(passesPath, "\n");
bool rejectedRuntimeDrift = false;
try { driftStore.Apply(driftInspection.Fingerprint, requireStopped: false); }
catch (IOException) { rejectedRuntimeDrift = true; }
finally
{
    File.WriteAllBytes(passesPath, passesBytes);
    File.SetLastWriteTimeUtc(passesPath, passesTime);
}
Check(rejectedRuntimeDrift, "runtime changes after inspection invalidate the fingerprint");

store.Apply(inspection.Fingerprint, requireStopped: false);
byte[] adapted = File.ReadAllBytes(outfitPath);
string adaptedText = Encoding.UTF8.GetString(adapted);
Check(adaptedText.StartsWith("\uFEFF", StringComparison.Ordinal), "UTF-8 BOM is retained");
Check(adaptedText.Contains("; EJ-OUTFIT/1 BEGIN " + inspection.SupportedDraws[0].Id, StringComparison.Ordinal),
    "draw is wrapped with the stable EJ marker");
Check(adaptedText.Contains("drawindexedinstanced = 3, INSTANCE_COUNT, 9, -1, FIRST_INSTANCE ; keep comment",
    StringComparison.Ordinal), "original draw command and comment are retained");
Check(adaptedText.Contains("$\\EndfieldJiggleEFMI\\outfit_first = 9", StringComparison.Ordinal),
    "helper command list receives the source range");
Check(store.HasRestorableBackup, "applied adaptation has an owned backup");
Check(store.Inspect().AlreadyAdapted, "subsequent inspection reports existing adaptation");

string backupRoot = Path.Combine(outfit, "DISABLED_EJTouchBackups");
string backupDirectory = Directory.EnumerateDirectories(backupRoot).Single();
string backupFile = Path.Combine(backupDirectory, "body.ini.bak");
DateTime legitimateBackupTime = File.GetLastWriteTimeUtc(backupFile);
File.SetLastWriteTimeUtc(backupFile, legitimateBackupTime.AddSeconds(2));
bool rejectedBackupStamp = false;
try { store.Restore(requireStopped: false); }
catch (InvalidDataException) { rejectedBackupStamp = true; }
Check(rejectedBackupStamp, "restore rejects a backup whose timestamp stamp changed");
File.SetLastWriteTimeUtc(backupFile, legitimateBackupTime);

store.Restore(requireStopped: false);
Check(SHA256.HashData(File.ReadAllBytes(outfitPath)).SequenceEqual(SHA256.HashData(original)),
    "restore returns exact original bytes");
Check(SHA256.HashData(File.ReadAllBytes(includedPath)).SequenceEqual(SHA256.HashData(includedOriginal)),
    "included INI draw also restores exact original bytes");
Check(!store.HasRestorableBackup, "restored backup is no longer offered as restorable");

Console.WriteLine($"PASS: outfit adapter tests. Fixture: {fixture}");

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}
