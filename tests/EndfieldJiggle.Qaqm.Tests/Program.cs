using System.Text;
using System.Security.Cryptography;
using System.Reflection;
using EndfieldJiggle.Configurator.Core;

string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", ".."));
string root = Path.Combine(repositoryRoot, "reports", "qaqm-recovery-tests",
    DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);
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

(string Efmi, string Outfit) Fixture(string name)
{
    string basePath = Path.Combine(root, name);
    string efmi = Path.Combine(basePath, "EFMI");
    string outfit = Path.Combine(efmi, "Mods", "SelectedOutfit");
    Directory.CreateDirectory(outfit);
    File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
        "[Include]\ninclude_recursive = Mods\nexclude_recursive = DISABLED*\nexclude_recursive = CUSTOM_DISABLED*\n");
    return (efmi, outfit);
}

void WriteRecoveryEvidence(string outfit, string id, string declaration = "global persist $size = 0.75")
{
    string source = Path.Combine(outfit, "body.ini");
    File.WriteAllText(source,
        $"namespace = Outfit\n[Constants]\n; Persisted state is hosted by qaqm_state_{id}.ini\n" +
        $"global $read = $\\QAQM\\Persist\\Bridge_{id}\\size\n");
    File.WriteAllText(source + ".qaqm-persistbak", $"namespace = Outfit\n[Constants]\n{declaration}\n");
}

try
{
    const string id = "0123456789ab";
    (string efmi, string outfit) = Fixture("apply-restore");
    WriteRecoveryEvidence(outfit, id);
    string disabled = Path.Combine(efmi, "Mods", "DISABLED_fixture");
    Directory.CreateDirectory(disabled);
    File.WriteAllText(Path.Combine(disabled, "ignored.ini"),
        $"namespace = QAQM\\Persist\\Bridge_{id}\n[Constants]\nglobal persist $wrong = 0\n");
    string customDisabled = Path.Combine(efmi, "Mods", "CUSTOM_DISABLED_fixture");
    Directory.CreateDirectory(customDisabled);
    File.WriteAllText(Path.Combine(customDisabled, "ignored.ini"),
        $"namespace = QAQM\\Persist\\Bridge_{id}\n[Constants]\nglobal persist $wrong = 0\n");
    File.WriteAllText(Path.Combine(efmi, "Mods", "desktop.ini"),
        $"namespace = QAQM\\Persist\\Bridge_{id}\n[Constants]\nglobal persist $wrong = 0\n");
    QaqmRecoveryStore store = new(outfit, efmi);
    QaqmRecoveryPlan plan = store.Inspect();
    Check(plan.Issues.Count == 0 && plan.MissingHosts.Count == 1 && plan.MissingHosts[0].Id == id,
        "recursive include scans selected outfit and excludes DISABLED* trees: " +
        string.Join(" | ", plan.Issues.Select(issue => issue.Code + ": " + issue.Message)) +
        $"; missing={plan.MissingHosts.Count}");
    QaqmRecoveryResult applied = store.Apply(plan.Fingerprint, requireStopped: false);
    string hostPath = Path.Combine(outfit, $"qaqm_state_{id}.ini");
    Check(applied.Changed && File.Exists(hostPath) && store.HasRestorableBackup,
        "missing host is created with a restorable receipt");
    QaqmRecoveryPlan repeated = store.Inspect();
    Check(repeated.Issues.Count == 0 && repeated.MissingHosts.Count == 0,
        "repeat inspection recognizes the already-loaded generated host");
    QaqmRecoveryResult restored = store.Restore(requireStopped: false);
    Check(restored.Changed && !File.Exists(hostPath) && !store.HasRestorableBackup,
        "restore removes only the unchanged generated host");
    Check(File.Exists(Path.Combine(outfit, "body.ini")) &&
          File.Exists(Path.Combine(outfit, "body.ini.qaqm-persistbak")),
        "restore preserves source INI and evidence backup");
    QaqmRecoveryPlan reappliedPlan = store.Inspect();
    QaqmRecoveryResult reapplied = store.Apply(reappliedPlan.Fingerprint, requireStopped: false);
    Check(reapplied.Changed && File.Exists(hostPath),
        "a new recovery session can apply after a prior session was restored");
    store.Restore(requireStopped: false);

    (efmi, outfit) = Fixture("loaded-host");
    WriteRecoveryEvidence(outfit, id);
    File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
        "[Include]\ninclude = Mods\\include.ini\ninclude_recursive = Mods\nexclude_recursive = DISABLED*\nexclude_recursive = CUSTOM_DISABLED*\n");
    File.WriteAllText(Path.Combine(efmi, "Mods", "include.ini"), "include = external.ini\n");
    string loaded = Path.Combine(efmi, "Mods", "external.ini");
    File.WriteAllBytes(loaded, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(
        $"namespace = QAQM\\Persist\\Bridge_{id}\n[Constants]\nglobal persist $size = 1\n" +
        "[Other]\nglobal persist $phantom = 1\n")).ToArray());
    plan = new QaqmRecoveryStore(outfit, efmi).Inspect();
    Check(plan.Issues.Count == 0 && plan.MissingHosts.Count == 0 &&
          plan.LoadedHosts.Any(host => host.Id == id &&
              host.Variables.Contains("size", StringComparer.OrdinalIgnoreCase) &&
              !host.Variables.Contains("phantom", StringComparer.OrdinalIgnoreCase)),
        "BOM-prefixed host reached through an explicit child include is not duplicated");

    File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
        "[Include]\ninclude_recursive = Mods\nexclude_recursive = SelectedOutfit\n");
    plan = new QaqmRecoveryStore(outfit, efmi).Inspect();
    Check(plan.Issues.Any(issue => issue.Code == "outfit-not-proven-loaded"),
        "custom recursive exclusion prevents treating the selected outfit as active");

    (efmi, outfit) = Fixture("nested-recursive");
    WriteRecoveryEvidence(outfit, id);
    string core = Path.Combine(efmi, "Core");
    string extra = Path.Combine(efmi, "Extra");
    string extraNested = Path.Combine(efmi, "ExtraNested");
    Directory.CreateDirectory(core);
    Directory.CreateDirectory(extra);
    Directory.CreateDirectory(extraNested);
    File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
        "[Include]\ninclude = Core\\main.ini\ninclude_recursive = Mods\n");
    File.WriteAllText(Path.Combine(core, "main.ini"), "[Include]\ninclude_recursive = ..\\Extra\n");
    File.WriteAllText(Path.Combine(extra, "entry.ini"), "[Include]\ninclude_recursive = ..\\ExtraNested\n");
    string nestedHostPath = Path.Combine(extraNested, "state.ini");
    File.WriteAllText(nestedHostPath,
        $"namespace = QAQM\\Persist\\Bridge_{id}\n[Constants]\nglobal persist $size = 1\n");
    plan = new QaqmRecoveryStore(outfit, efmi).Inspect();
    Check(plan.Issues.Count == 0 && plan.MissingHosts.Count == 0 &&
          plan.LoadedHosts.Any(host => host.Id == id &&
              host.Path.Equals(nestedHostPath, StringComparison.OrdinalIgnoreCase)),
        "nested recursive include scopes are traversed to their active host");

    (efmi, outfit) = Fixture("duplicate-host");
    WriteRecoveryEvidence(outfit, id);
    foreach (string name in new[] { "one.ini", "two.ini" })
        File.WriteAllText(Path.Combine(efmi, "Mods", name),
            $"namespace = QAQM\\Persist\\Bridge_{id}\n[Constants]\nglobal persist $size = 1\n");
    plan = new QaqmRecoveryStore(outfit, efmi).Inspect();
    Check(plan.Issues.Any(issue => issue.Code == "duplicate-loaded-host"),
        "duplicate active Bridge hosts block recovery");

    (efmi, outfit) = Fixture("missing-backup");
    WriteRecoveryEvidence(outfit, id);
    File.Delete(Path.Combine(outfit, "body.ini.qaqm-persistbak"));
    plan = new QaqmRecoveryStore(outfit, efmi).Inspect();
    Check(plan.Issues.Any(issue => issue.Code == "missing-backup"),
        "missing source backup is reported as unresolved");

    (efmi, outfit) = Fixture("invalid-default");
    WriteRecoveryEvidence(outfit, id, "global persist $size = run = injected");
    plan = new QaqmRecoveryStore(outfit, efmi).Inspect();
    Check(plan.Issues.Any(issue => issue.Code == "invalid-backup-value") &&
          plan.MissingHosts.Count == 0,
        "invalid or non-numeric persisted values are never emitted");

    (efmi, outfit) = Fixture("edited-host");
    WriteRecoveryEvidence(outfit, id);
    store = new(outfit, efmi);
    plan = store.Inspect();
    store.Apply(plan.Fingerprint, requireStopped: false);
    File.AppendAllText(Path.Combine(outfit, $"qaqm_state_{id}.ini"), "; external edit\n", new UTF8Encoding(false));
    Reject(() => store.Restore(requireStopped: false), "restore rejects externally edited generated hosts");
    Check(File.Exists(Path.Combine(outfit, $"qaqm_state_{id}.ini")),
        "rejected restore leaves externally edited host untouched");

    (efmi, outfit) = Fixture("stale-plan");
    WriteRecoveryEvidence(outfit, id);
    store = new(outfit, efmi);
    plan = store.Inspect();
    File.AppendAllText(Path.Combine(outfit, "body.ini"), "; changed after inspect\n");
    Reject(() => store.Apply(plan.Fingerprint, requireStopped: false), "apply rejects stale evidence fingerprint");

    string outside = Path.Combine(root, "outside");
    Directory.CreateDirectory(outside);
    Reject(() => new QaqmRecoveryStore(outside, efmi),
        "constructor rejects outfit directories outside EFMI Mods");

    string rollbackPath = Path.Combine(root, "rollback-guard.ini");
    byte[] originalGenerated = Encoding.UTF8.GetBytes("generated-host");
    File.WriteAllBytes(rollbackPath, Encoding.UTF8.GetBytes("externally changed"));
    MethodInfo rollbackGuard = typeof(QaqmRecoveryStore).GetMethod(
        "DeleteGeneratedIfUnchanged", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Rollback guard method not found.");
    bool preserved = false;
    try
    {
        rollbackGuard.Invoke(null,
            [rollbackPath, Convert.ToHexString(SHA256.HashData(originalGenerated)), "rollback guard test"]);
    }
    catch (TargetInvocationException error) when (error.InnerException is IOException)
    {
        preserved = File.Exists(rollbackPath);
    }
    Check(preserved, "apply rollback guard preserves a host changed externally before deletion");

    Console.WriteLine($"PASS: {checks.Count} QAQM recovery checks.");
    foreach (string check in checks)
        Console.WriteLine(" - " + check);
}
finally
{
    Console.WriteLine("QAQM fixture artifacts: " + root);
}
