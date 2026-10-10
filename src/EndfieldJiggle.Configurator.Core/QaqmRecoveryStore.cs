using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndfieldJiggle.Configurator.Core;

public sealed record QaqmLoadedHost(string Id, string Path, IReadOnlyList<string> Variables, string Sha256);

public sealed record QaqmRecoveryHost(string Id, string FileName, IReadOnlyList<string> Variables);

public sealed record QaqmRecoveryIssue(string Code, string Message);

public sealed record QaqmRecoveryPlan(
    string Fingerprint,
    IReadOnlyList<QaqmRecoveryHost> MissingHosts,
    IReadOnlyList<QaqmLoadedHost> LoadedHosts,
    IReadOnlyList<QaqmRecoveryIssue> Issues)
{
    public bool CanApply => Issues.Count == 0 && MissingHosts.Count > 0;
}

public sealed record QaqmRecoveryResult(bool Changed, IReadOnlyList<string> Files, string? ReceiptPath);

public sealed class QaqmRecoveryStore
{
    private const int MaxIniFiles = 2048;
    private const int MaxIncludeDepth = 64;
    private const string BackupDirectoryName = "DISABLED_EJQaqmBackups";
    private const string ReceiptName = "receipt.json";
    private static readonly Regex BridgeReference = new(
        @"\$\\QAQM\\Persist\\Bridge_(?<id>[0-9a-f]{12})\\(?<variable>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NamespaceLine = new(
        @"^\s*namespace\s*=\s*QAQM\\Persist\\Bridge_(?<id>[0-9a-f]{12})\s*(?:;.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex HostMarker = new(
        @"^\s*;\s*Persisted state is hosted by qaqm_state_(?<id>[0-9a-f]{12})\.ini\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex PersistDeclaration = new(
        @"^\s*global\s+persist\s+\$(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*(?:;.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SectionLine = new(
        @"^\s*\[(?<name>[^\]]+)\]\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Assignment = new(
        @"^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>[^;\r\n]*?)\s*(?:;.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string outfitRoot;
    private readonly string efmiRoot;

    public QaqmRecoveryStore(string outfitDirectory, string efmiDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outfitDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(efmiDirectory);
        outfitRoot = Path.GetFullPath(outfitDirectory);
        efmiRoot = Path.GetFullPath(efmiDirectory);
        EnsureDirectory(outfitRoot);
        EnsureDirectory(efmiRoot);
        string modsRoot = Path.Combine(efmiRoot, "Mods");
        EnsurePathSafe(modsRoot);
        if (PathsEqual(outfitRoot, modsRoot) || !IsWithin(modsRoot, outfitRoot))
            throw new ArgumentException("The selected outfit must be inside EFMI's Mods directory.", nameof(outfitDirectory));
    }

    public bool HasRestorableBackup
    {
        get
        {
            string directory = BackupDirectory;
            EnsurePathSafe(directory);
            if (!Directory.Exists(directory))
                return false;
            foreach (string sessionDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    EnsurePathSafe(sessionDirectory);
                    string receiptPath = Path.Combine(sessionDirectory, ReceiptName);
                    if (!File.Exists(receiptPath))
                        continue;
                    EnsurePathSafe(receiptPath);
                    Receipt? receipt = ReadReceipt(receiptPath);
                    if (receipt is { Schema: 1, Status: "applied" } &&
                        PathsEqual(receipt.OutfitRoot, outfitRoot) && receipt.Files.Length > 0)
                        return true;
                }
                catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
                {
                    continue;
                }
            }
            return false;
        }
    }

    public QaqmRecoveryPlan Inspect()
    {
        List<QaqmRecoveryIssue> issues = [];
        Dictionary<string, QaqmLoadedHost> loadedHosts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> references = new(StringComparer.OrdinalIgnoreCase);
        List<string> evidenceFiles = [];

        try
        {
            ActiveEfmiScan activeScan = ReadActiveEfmiInis(evidenceFiles);
            List<string> activeEfmiInis = activeScan.Files;
            foreach (string path in activeEfmiInis)
            {
                string text = ReadText(path);
                MatchCollection namespaces = NamespaceLine.Matches(text);
                if (namespaces.Count > 1)
                {
                    issues.Add(new("ambiguous-host", $"Multiple QAQM Bridge namespaces occur in {path}."));
                    continue;
                }
                if (namespaces.Count == 0)
                    continue;

                string id = namespaces[0].Groups["id"].Value.ToLowerInvariant();
                string[] variables = ReadDeclaredVariables(text);
                QaqmLoadedHost host = new(id, path, variables, HashFile(path));
                if (loadedHosts.TryGetValue(id, out QaqmLoadedHost? existing))
                    issues.Add(new("duplicate-loaded-host",
                        $"Bridge {id} is loaded by both {existing.Path} and {path}."));
                else
                    loadedHosts.Add(id, host);
            }

            if (!CanLoadFromRecursiveScope(outfitRoot, activeScan.RecursiveScopes))
                issues.Add(new("outfit-not-proven-loaded",
                    "The active EFMI include graph does not prove the selected outfit and its root state hosts are recursively loadable."));

            string[] activeOutfitInis = [];
            if (issues.All(issue => issue.Code != "outfit-not-proven-loaded"))
            {
                activeOutfitInis = EnumerateActiveOutfitInis(activeEfmiInis, evidenceFiles);
                foreach (string path in activeOutfitInis)
                {
                    string text = ReadText(path);
                    foreach (Match match in BridgeReference.Matches(text))
                    {
                        string id = match.Groups["id"].Value.ToLowerInvariant();
                        if (!references.TryGetValue(id, out HashSet<string>? variables))
                            references.Add(id, variables = new(StringComparer.OrdinalIgnoreCase));
                        variables.Add(match.Groups["variable"].Value);
                    }
                }
            }

            List<QaqmRecoveryHost> missing = [];
            foreach ((string id, HashSet<string> variables) in references.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (loadedHosts.TryGetValue(id, out QaqmLoadedHost? active))
                {
                    EnsureCoverage(id, variables, active.Variables, issues, active.Path);
                    continue;
                }

                string hostPath = Path.Combine(outfitRoot, $"qaqm_state_{id}.ini");
                if (File.Exists(hostPath))
                {
                    EnsurePathSafe(hostPath);
                    string hostText = ReadText(hostPath);
                    string[] localNamespaces = NamespaceLine.Matches(hostText)
                        .Select(match => match.Groups["id"].Value.ToLowerInvariant()).ToArray();
                    if (localNamespaces.Length != 1 || localNamespaces[0] != id)
                    {
                        issues.Add(new("invalid-local-host", $"Selected outfit host has a namespace mismatch: {hostPath}."));
                        continue;
                    }
                    if (!activeEfmiInis.Contains(hostPath, StringComparer.OrdinalIgnoreCase))
                    {
                        issues.Add(new("local-host-not-proven-loaded",
                            $"The selected outfit host cannot be proven active: {hostPath}."));
                        continue;
                    }
                    EnsureCoverage(id, variables, ReadDeclaredVariables(hostText), issues, hostPath);
                    continue;
                }

                if (!CanLoadFromRecursiveScope(hostPath, activeScan.RecursiveScopes))
                {
                    issues.Add(new("host-not-loadable",
                        $"The active recursive include rules exclude or do not cover the required root host: {hostPath}."));
                    continue;
                }

                string[] markers = FindMarkedSourceFiles(id, activeOutfitInis, issues, evidenceFiles);
                if (markers.Length != 1)
                {
                    issues.Add(new(markers.Length == 0 ? "missing-evidence" : "ambiguous-evidence",
                        markers.Length == 0
                            ? $"Bridge {id} has no matching marker and source backup."
                            : $"Multiple selected outfit INIs claim missing Bridge host {id}."));
                    continue;
                }

                string sourcePath = markers[0];
                string backupPath = sourcePath + ".qaqm-persistbak";
                if (!File.Exists(backupPath))
                {
                    issues.Add(new("missing-backup", $"Matching persisted-state backup is missing: {backupPath}."));
                    continue;
                }
                EnsurePathSafe(backupPath);
                evidenceFiles.Add(backupPath);
                Dictionary<string, string> declarations = ParseBackupDeclarations(backupPath, issues);
                if (!declarations.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).IsSupersetOf(variables))
                {
                    string absent = string.Join(", ", variables.Except(declarations.Keys, StringComparer.OrdinalIgnoreCase));
                    issues.Add(new("unproven-reference",
                        $"Backup for Bridge {id} does not evidence every referenced variable: {absent}."));
                    continue;
                }
                if (declarations.Count == 0)
                    continue;
                missing.Add(new(id, $"qaqm_state_{id}.ini", declarations.Keys.OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase).ToArray()));
            }

            if (missing.Select(host => host.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != missing.Count)
                issues.Add(new("duplicate-plan", "More than one source claims the same missing Bridge host."));

            string fingerprint = Fingerprint(evidenceFiles);
            return new(fingerprint, missing, loadedHosts.Values.OrderBy(host => host.Id, StringComparer.Ordinal).ToArray(), issues);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                                      DecoderFallbackException or JsonException or ArgumentException)
        {
            issues.Add(new("inspection-failed", error.Message));
            return new(Fingerprint(evidenceFiles), [], loadedHosts.Values.ToArray(), issues);
        }
    }

    public QaqmRecoveryResult Apply(string expectedFingerprint, bool requireStopped = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        if (requireStopped)
            EnsureGameStopped();
        QaqmRecoveryPlan plan = Inspect();
        if (!string.Equals(plan.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new IOException("QAQM recovery evidence changed after inspection. Inspect again before applying.");
        if (plan.Issues.Count > 0)
            throw new InvalidDataException("QAQM recovery is blocked: " +
                string.Join(" ", plan.Issues.Select(issue => issue.Message)));
        if (plan.MissingHosts.Count == 0)
            return new(false, [], null);

        string backupDirectory = BackupDirectory;
        EnsurePathSafe(backupDirectory);
        Directory.CreateDirectory(backupDirectory);
        EnsurePathSafe(backupDirectory);
        string sessionDirectory = Path.Combine(backupDirectory,
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sessionDirectory);
        EnsurePathSafe(sessionDirectory);
        string receiptPath = Path.Combine(sessionDirectory, ReceiptName);

        List<ReceiptFile> receiptFiles = [];
        foreach (QaqmRecoveryHost host in plan.MissingHosts)
        {
            string target = Path.Combine(outfitRoot, host.FileName);
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException($"Refusing to overwrite existing selected outfit host: {target}");
            byte[] content = BuildHost(host.Id, GetDeclarationsForHost(host.Id));
            string backup = Path.Combine(sessionDirectory, host.FileName + ".bak");
            receiptFiles.Add(new(host.FileName, Hash(content), Path.GetFileName(backup), Hash(content)));
        }

        Receipt receipt = new(1, "prepared", outfitRoot, plan.Fingerprint, receiptFiles.ToArray());
        List<string> created = [];
        List<(string Target, string Temporary)> staged = [];
        bool receiptWritten = false;
        try
        {
            WriteJsonAtomic(receiptPath, receipt);
            receiptWritten = true;
            foreach (ReceiptFile file in receipt.Files)
            {
                string target = Path.Combine(outfitRoot, file.Name);
                string backup = Path.Combine(sessionDirectory, file.BackupName);
                if (File.Exists(target) || Directory.Exists(target))
                    throw new IOException($"Refusing to overwrite existing selected outfit host: {target}");
                QaqmRecoveryHost host = plan.MissingHosts.Single(item =>
                    item.FileName.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
                byte[] content = BuildHost(host.Id, GetDeclarationsForHost(host.Id));
                WriteNewAtomic(backup, content);
                string temporary = Path.Combine(outfitRoot,
                    "." + file.Name + "." + Guid.NewGuid().ToString("N") + ".tmp");
                WriteNewAtomic(temporary, content);
                staged.Add((target, temporary));
            }
            if (requireStopped)
                EnsureGameStopped();
            QaqmRecoveryPlan current = Inspect();
            if (!string.Equals(current.Fingerprint, expectedFingerprint, StringComparison.Ordinal) ||
                current.Issues.Count != 0)
                throw new IOException("QAQM recovery evidence changed during apply.");
            foreach ((string target, string temporary) in staged)
            {
                if (requireStopped)
                    EnsureGameStopped();
                if (File.Exists(target) || Directory.Exists(target))
                    throw new IOException($"Refusing to overwrite existing selected outfit host: {target}");
                File.Move(temporary, target);
                created.Add(target);
            }
            if (requireStopped)
                EnsureGameStopped();
            WriteJsonAtomic(receiptPath, receipt with { Status = "applied" });
            return new(true, receipt.Files.Select(file => Path.Combine(outfitRoot, file.Name)).ToArray(), receiptPath);
        }
        catch (Exception applyError)
        {
            try
            {
                foreach (string path in created.AsEnumerable().Reverse())
                {
                    ReceiptFile file = receipt.Files.Single(item =>
                        Path.Combine(outfitRoot, item.Name).Equals(path, StringComparison.OrdinalIgnoreCase));
                    DeleteGeneratedIfUnchanged(path, file.GeneratedSha256,
                        "Refusing to remove a host changed during apply rollback");
                }
                if (receiptWritten && File.Exists(receiptPath))
                    WriteJsonAtomic(receiptPath, receipt with { Status = "rolled-back" });
            }
            catch (Exception rollbackError)
            {
                throw new IOException($"QAQM recovery failed: {applyError.Message}. Rollback failed: {rollbackError.Message}",
                    new AggregateException(applyError, rollbackError));
            }
            throw;
        }
        finally
        {
            foreach ((_, string temporary) in staged)
                if (File.Exists(temporary))
                    File.Delete(temporary);
        }
    }

    public QaqmRecoveryResult Restore(bool requireStopped = true)
    {
        if (requireStopped)
            EnsureGameStopped();
        string backupDirectory = BackupDirectory;
        EnsurePathSafe(backupDirectory);
        string[] receipts = [];
        if (Directory.Exists(backupDirectory))
        {
            List<string> candidates = [];
            foreach (string sessionDirectory in Directory.EnumerateDirectories(
                         backupDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                EnsurePathSafe(sessionDirectory);
                string candidate = Path.Combine(sessionDirectory, ReceiptName);
                if (!File.Exists(candidate))
                    continue;
                EnsurePathSafe(candidate);
                Receipt? value = ReadReceipt(candidate);
                if (value is { Schema: 1, Status: "applied" } && PathsEqual(value.OutfitRoot, outfitRoot))
                    candidates.Add(candidate);
            }
            receipts = candidates.OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        }
        if (receipts.Length == 0)
            throw new FileNotFoundException("No applied QAQM recovery receipt is available to restore.", backupDirectory);
        string receiptPath = receipts[0];
        Receipt receipt = ReadReceipt(receiptPath)
            ?? throw new FileNotFoundException("No QAQM recovery receipt is available to restore.", receiptPath);
        if (receipt.Schema != 1 || receipt.Status != "applied" || !PathsEqual(receipt.OutfitRoot, outfitRoot))
            throw new InvalidDataException("QAQM recovery receipt does not match this selected outfit.");

        Dictionary<string, byte[]> generated = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReceiptFile file in receipt.Files)
        {
            ValidateHostFileName(file.Name);
            ValidateBackupName(file);
            string target = Path.Combine(outfitRoot, file.Name);
            string backup = Path.Combine(Path.GetDirectoryName(receiptPath)!, file.BackupName);
            EnsurePathSafe(target);
            EnsurePathSafe(backup);
            if (!File.Exists(target) || !File.Exists(backup))
                throw new IOException($"Generated QAQM host or its receipt backup is missing: {file.Name}");
            byte[] current = File.ReadAllBytes(target);
            byte[] saved = File.ReadAllBytes(backup);
            if (Hash(current) != file.GeneratedSha256 || Hash(saved) != file.BackupSha256 ||
                Hash(current) != Hash(saved))
                throw new IOException($"Refusing to remove externally modified QAQM host: {target}");
            generated.Add(target, current);
        }

        List<string> removed = [];
        try
        {
            foreach (string path in generated.Keys)
            {
                if (requireStopped)
                    EnsureGameStopped();
                ReceiptFile file = receipt.Files.Single(item =>
                    Path.Combine(outfitRoot, item.Name).Equals(path, StringComparison.OrdinalIgnoreCase));
                string backup = Path.Combine(Path.GetDirectoryName(receiptPath)!, file.BackupName);
                EnsurePathSafe(backup);
                if (HashFile(backup) != file.BackupSha256)
                    throw new IOException($"Refusing restore because the receipt backup changed: {backup}");
                if (!generated.ContainsKey(path))
                    throw new IOException($"Host is not part of the validated restore set: {path}");
                DeleteGeneratedIfUnchanged(path, file.GeneratedSha256,
                    "Refusing to remove a QAQM host changed during restore");
                removed.Add(path);
            }
            if (requireStopped)
                EnsureGameStopped();
            WriteJsonAtomic(receiptPath, receipt with { Status = "restored" });
            return new(true, removed, receiptPath);
        }
        catch (Exception restoreError)
        {
            try
            {
                foreach (string path in removed.AsEnumerable().Reverse())
                {
                    if (requireStopped)
                        EnsureGameStopped();
                    WriteNewAtomic(path, generated[path]);
                }
            }
            catch (Exception rollbackError)
            {
                throw new IOException($"QAQM restore failed: {restoreError.Message}. Rollback failed: {rollbackError.Message}",
                    new AggregateException(restoreError, rollbackError));
            }
            throw;
        }
    }

    private string BackupDirectory => Path.Combine(outfitRoot, BackupDirectoryName);

    private static void EnsureGameStopped()
    {
        if (RuntimeSettingsStore.IsGameRunning())
            throw new InvalidOperationException("请先退出游戏和 XXMI，再恢复 QAQM 状态。");
    }

    private ActiveEfmiScan ReadActiveEfmiInis(List<string> evidence)
    {
        string rootIni = Path.Combine(efmiRoot, "d3dx.ini");
        if (!File.Exists(rootIni))
            throw new FileNotFoundException("EFMI d3dx.ini was not found.", rootIni);

        Queue<FileWork> pendingFiles = new();
        Queue<RecursiveScope> pendingScopes = new();
        HashSet<string> parsedFiles = new(StringComparer.OrdinalIgnoreCase);
        List<string> activeFiles = [];
        List<RecursiveScope> scopes = [];
        int discoveredFiles = 0;
        pendingFiles.Enqueue(new(rootIni, 0, []));

        while (pendingFiles.Count > 0 || pendingScopes.Count > 0)
        {
            while (pendingFiles.Count > 0)
            {
                FileWork work = pendingFiles.Dequeue();
                if (work.Depth > MaxIncludeDepth)
                    throw new InvalidDataException("EFMI include graph exceeded the depth limit.");
                EnsureContained(efmiRoot, work.Path);
                EnsurePathSafe(work.Path);
                if (!File.Exists(work.Path))
                    throw new FileNotFoundException("An active EFMI include is missing.", work.Path);

                string fullPath = Path.GetFullPath(work.Path);
                if (!parsedFiles.Add(fullPath))
                    continue;
                if (++discoveredFiles > MaxIniFiles)
                    throw new InvalidDataException("EFMI include graph exceeded the INI-file limit.");
                activeFiles.Add(fullPath);
                evidence.Add(fullPath);

                List<string> localExcludes = [];
                List<string> explicitIncludes = [];
                List<string> recursiveDirectories = [];
                string section = "";
                foreach (string raw in ReadText(fullPath).Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    Match sectionMatch = SectionLine.Match(line);
                    if (sectionMatch.Success)
                    {
                        section = sectionMatch.Groups["name"].Value.Trim();
                        continue;
                    }
                    if (section.Length == 0 || line.TrimStart().StartsWith(';') ||
                        line.TrimStart().StartsWith('#'))
                        continue;
                    Match assignment = Assignment.Match(line);
                    if (!assignment.Success)
                        continue;

                    string key = assignment.Groups["key"].Value;
                    string value = assignment.Groups["value"].Value.Trim().Trim('"');
                    if (key.Equals("exclude_recursive", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value.Length == 0 || value.IndexOfAny(['$', '\r', '\n']) >= 0)
                            throw new InvalidDataException($"Ambiguous recursive exclusion in {fullPath}: {line}");
                        localExcludes.Add(value);
                    }
                    else if (key.Equals("include", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value.Length == 0 || value.IndexOfAny(['*', '?', '$']) >= 0)
                            throw new InvalidDataException($"Ambiguous explicit include in {fullPath}: {line}");
                        explicitIncludes.Add(value);
                    }
                    else if (key.Equals("include_recursive", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value.Length == 0 || value.IndexOfAny(['*', '?', '$']) >= 0)
                            throw new InvalidDataException($"Ambiguous recursive include in {fullPath}: {line}");
                        string directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath)!, value));
                        EnsureContained(efmiRoot, directory);
                        EnsurePathSafe(directory);
                        if (!Directory.Exists(directory))
                            throw new DirectoryNotFoundException($"Recursive include directory is missing: {directory}");
                        recursiveDirectories.Add(directory);
                    }
                }

                string[] effectiveExcludes = MergeExcludes(work.InheritedExcludes, localExcludes);
                foreach (string include in explicitIncludes)
                    pendingFiles.Enqueue(new(ResolveInclude(fullPath, include, efmiRoot),
                        work.Depth + 1, effectiveExcludes));
                foreach (string directory in recursiveDirectories)
                {
                    RecursiveScope recursiveScope = new(directory, work.Depth + 1, effectiveExcludes);
                    scopes.Add(recursiveScope);
                    pendingScopes.Enqueue(recursiveScope);
                }
            }

            if (pendingScopes.Count == 0)
                continue;
            RecursiveScope scope = pendingScopes.Dequeue();
            string[] excludes = MergeExcludes(scope.Excludes, ["DISABLED*", "desktop.ini"]);
            foreach (string ini in EnumerateRecursiveInis(scope.Directory, excludes, MaxIniFiles - discoveredFiles))
            {
                pendingFiles.Enqueue(new(ini, scope.Depth, excludes));
            }
        }

        return new(activeFiles, scopes);
    }

    private string[] EnumerateActiveOutfitInis(IEnumerable<string> activeFiles, List<string> evidence)
    {
        string[] files = activeFiles.Where(path => IsWithin(outfitRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        evidence.AddRange(files);
        return files;
    }

    private string[] FindMarkedSourceFiles(
        string id, IEnumerable<string> activeOutfitInis, List<QaqmRecoveryIssue> issues, List<string> evidence)
    {
        List<string> found = [];
        foreach (string path in activeOutfitInis)
        {
            evidence.Add(path);
            string text = ReadText(path);
            MatchCollection markers = HostMarker.Matches(text);
            foreach (Match marker in markers)
            {
                if (!marker.Groups["id"].Value.Equals(id, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (markers.Count != 1)
                {
                    issues.Add(new("ambiguous-marker", $"Multiple state-host markers occur in {path}."));
                    continue;
                }
                found.Add(path);
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private Dictionary<string, string> ParseBackupDeclarations(string path, List<QaqmRecoveryIssue> issues)
    {
        string section = "";
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in ReadText(path).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match sectionMatch = SectionLine.Match(line);
            if (sectionMatch.Success)
            {
                section = sectionMatch.Groups["name"].Value.Trim();
                continue;
            }
            if (!section.Equals("Constants", StringComparison.OrdinalIgnoreCase) ||
                !line.TrimStart().StartsWith("global", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!line.Contains("persist", StringComparison.OrdinalIgnoreCase))
                continue;
            Match declaration = PersistDeclaration.Match(line);
            if (!declaration.Success)
            {
                issues.Add(new("invalid-backup-value", $"Refusing a non-numeric or implicit persisted value in {path}."));
                continue;
            }
            string variable = declaration.Groups["variable"].Value;
            if (!result.TryAdd(variable, declaration.Groups["value"].Value))
                issues.Add(new("duplicate-backup-variable", $"Backup declares ${variable} more than once: {path}."));
        }
        return result;
    }

    private static string[] ReadDeclaredVariables(string text)
    {
        List<string> variables = [];
        string section = "";
        foreach (string line in text.Split('\n'))
        {
            Match sectionMatch = SectionLine.Match(line.TrimEnd('\r'));
            if (sectionMatch.Success)
            {
                section = sectionMatch.Groups["name"].Value.Trim();
                continue;
            }
            if (!section.Equals("Constants", StringComparison.OrdinalIgnoreCase))
                continue;
            Match match = Regex.Match(line, @"^\s*global(?:\s+persist)?\s+\$(?<name>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
                variables.Add(match.Groups["name"].Value);
        }
        return variables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void EnsureCoverage(string id, IEnumerable<string> references, IEnumerable<string> declarations,
        List<QaqmRecoveryIssue> issues, string path)
    {
        string[] missing = references.Except(declarations, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            issues.Add(new("uncovered-reference",
                $"Loaded host {path} for Bridge {id} does not declare: {string.Join(", ", missing)}."));
    }

    private static string ResolveInclude(string includingFile, string include, string root)
    {
        string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(includingFile)!, include));
        EnsureContained(root, target);
        EnsurePathSafe(target);
        return target;
    }

    private static IEnumerable<string> EnumerateRecursiveInis(string root, IReadOnlyList<string> excludes, int limit)
    {
        Queue<(string Directory, int Depth)> pending = new();
        pending.Enqueue((root, 0));
        int count = 0;
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Dequeue();
            if (depth > MaxIncludeDepth)
                throw new InvalidDataException("Recursive include exceeded the directory-depth limit.");
            EnsurePathSafe(directory);
            foreach (string file in Directory.EnumerateFiles(directory, "*.ini", SearchOption.TopDirectoryOnly))
            {
                EnsurePathSafe(file);
                string relativeFile = Path.GetRelativePath(root, file);
                if (Path.GetFileName(file).EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                    IsExcludedRelative(relativeFile, excludes))
                    continue;
                if (++count > limit)
                    throw new InvalidDataException("Recursive include exceeded the INI-file limit.");
                yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                EnsurePathSafe(child);
                string relative = Path.GetRelativePath(root, child);
                if (IsExcludedRelative(relative, root, excludes))
                    continue;
                pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool IsExcludedRelative(string path, IReadOnlyList<string> patterns)
    {
        string[] segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string pattern in patterns)
        {
            string normalized = pattern.Trim().Trim('"', '\\', '/');
            if (normalized.Length == 0)
                continue;
            Regex regex = new("^" + Regex.Escape(normalized).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (segments.Any(segment => regex.IsMatch(segment)) || regex.IsMatch(path.Replace('\\', '/')))
                return true;
        }
        return false;
    }

    private static void AddDefaultExclude(List<string> excludes, string value)
    {
        if (!excludes.Contains(value, StringComparer.OrdinalIgnoreCase))
            excludes.Add(value);
    }

    private static bool IsExcludedRelative(string path, string root, IReadOnlyList<string> patterns)
    {
        string relative = Path.IsPathRooted(path) ? Path.GetRelativePath(root, path) : path;
        return IsExcludedRelative(relative, patterns);
    }

    private static bool CanLoadFromRecursiveScope(string filePath, IEnumerable<RecursiveScope> scopes)
    {
        foreach (RecursiveScope scope in scopes)
        {
            if (!IsWithin(scope.Directory, filePath))
                continue;
            string relative = Path.GetRelativePath(scope.Directory, filePath);
            if (!IsExcludedRelative(relative, MergeExcludes(scope.Excludes, ["DISABLED*", "desktop.ini"])))
                return true;
        }
        return false;
    }

    private static string[] MergeExcludes(IEnumerable<string> first, IEnumerable<string> second) =>
        first.Concat(second).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private string Fingerprint(IEnumerable<string> paths)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(outfitRoot.ToUpperInvariant()));
        hash.AppendData(Encoding.UTF8.GetBytes(efmiRoot.ToUpperInvariant()));
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            string full = Path.GetFullPath(path);
            hash.AppendData(Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
            hash.AppendData([0]);
            if (File.Exists(full))
                hash.AppendData(SHA256.HashData(File.ReadAllBytes(full)));
            else
                hash.AppendData(Encoding.UTF8.GetBytes("<missing>"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private Dictionary<string, string> GetDeclarationsForHost(string id)
    {
        ActiveEfmiScan scan = ReadActiveEfmiInis([]);
        string[] activeOutfitInis = EnumerateActiveOutfitInis(scan.Files, []);
        string[] sources = FindMarkedSourceFiles(id, activeOutfitInis, [], []);
        if (sources.Length != 1)
            throw new InvalidDataException($"Expected one backup source for Bridge {id}.");
        List<QaqmRecoveryIssue> issues = [];
        Dictionary<string, string> declarations = ParseBackupDeclarations(sources[0] + ".qaqm-persistbak", issues);
        if (issues.Count > 0 || declarations.Count == 0)
            throw new InvalidDataException(string.Join(" ", issues.Select(issue => issue.Message)));
        return declarations;
    }

    private static byte[] BuildHost(string id, IReadOnlyDictionary<string, string> declarations)
    {
        StringBuilder text = new();
        text.Append("namespace = QAQM\\Persist\\Bridge_").Append(id).Append("\n\n[Constants]\n");
        foreach ((string variable, string value) in declarations.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            text.Append("global persist $").Append(variable).Append(" = ").Append(value).Append('\n');
        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void DeleteGeneratedIfUnchanged(string path, string expectedSha256, string refusalMessage)
    {
        EnsurePathSafe(path);
        if (!File.Exists(path) ||
            !string.Equals(HashFile(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"{refusalMessage}: {path}");
        File.Delete(path);
    }
    private static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
    }

    private static Receipt? ReadReceipt(string path)
    {
        EnsurePathSafe(path);
        return JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path), JsonOptions);
    }

    private static void WriteJsonAtomic(string path, Receipt receipt)
    {
        EnsurePathSafe(path);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, receipt, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void WriteNewAtomic(string path, byte[] bytes)
    {
        EnsurePathSafe(path);
        string temp = Path.Combine(Path.GetDirectoryName(path)!,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory does not exist: {path}");
        EnsurePathSafe(path);
    }

    private static void EnsurePathSafe(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Paths through reparse points are not supported: {current}");
        }
    }

    private static void EnsureContained(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes its configured root: {path}");
    }

    private static bool IsWithin(string root, string path) =>
        PathsEqual(root, path) || Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateHostFileName(string name)
    {
        if (!Regex.IsMatch(name, @"^qaqm_state_[0-9a-f]{12}\.ini$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Receipt contains an invalid QAQM host filename.");
    }

    private static void ValidateBackupName(ReceiptFile file)
    {
        if (!Path.GetFileName(file.BackupName).Equals(file.BackupName, StringComparison.Ordinal) ||
            !file.BackupName.Equals(file.Name + ".bak", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Receipt contains an invalid backup filename.");
    }

    private sealed record FileWork(string Path, int Depth, string[] InheritedExcludes);
    private sealed record RecursiveScope(string Directory, int Depth, string[] Excludes);
    private sealed record ActiveEfmiScan(List<string> Files, List<RecursiveScope> RecursiveScopes);

    private sealed record Receipt(
        int Schema, string Status, string OutfitRoot, string EvidenceFingerprint, ReceiptFile[] Files);
    private sealed record ReceiptFile(string Name, string GeneratedSha256, string BackupName, string BackupSha256);
}
