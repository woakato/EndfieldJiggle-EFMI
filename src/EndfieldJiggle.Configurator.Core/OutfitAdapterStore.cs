using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndfieldJiggle.Configurator.Core;

public sealed record OutfitAdapterDraw(
    string Id,
    string SourceFile,
    string Section,
    int SourceLine,
    string Command,
    string Branch,
    string Count,
    string FirstIndex,
    string BaseVertex,
    string InstanceCount,
    string FirstInstance,
    bool Supported,
    string Reason);

public sealed record OutfitAdapterInspection(
    string Fingerprint,
    IReadOnlyList<OutfitAdapterDraw> Draws,
    IReadOnlyList<string> DisabledIniFiles,
    IReadOnlyList<string> MissingRequiredResources,
    IReadOnlyList<string> MissingUnreferencedResources,
    bool AlreadyAdapted)
{
    internal string RuntimeFingerprint { get; init; } = "";
    public IReadOnlyList<OutfitAdapterDraw> SupportedDraws => Draws.Where(draw => draw.Supported).ToArray();
    public IReadOnlyList<OutfitAdapterDraw> UnsupportedDraws => Draws.Where(draw => !draw.Supported).ToArray();
}

public sealed class OutfitAdapterStore
{
    private const string BackupDirectoryName = "DISABLED_EJTouchBackups";
    private const long MaximumExactInteger = 16_777_216;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string root;
    private readonly string runtime;
    private readonly string modsRoot;

    public string Root => root;
    public bool HasRestorableBackup => EnumerateAppliedBackups().Any();

    public OutfitAdapterStore(string outfitDirectory, string runtimeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outfitDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        root = Path.GetFullPath(outfitDirectory);
        runtime = Path.GetFullPath(runtimeDirectory);
        modsRoot = Path.GetDirectoryName(runtime)
            ?? throw new InvalidDataException("Runtime directory must be directly inside EFMI Mods.");
        EnsureUnlinked(root);
        EnsureUnlinked(runtime);
        EnsureUnlinked(modsRoot);
        if (!Path.GetFileName(modsRoot).Equals("Mods", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(root) || !Directory.Exists(modsRoot) ||
            !IsWithin(modsRoot, root) || SamePath(root, runtime) ||
            IsWithin(runtime, root) || IsWithin(root, runtime))
            throw new InvalidDataException("Select an existing outfit directory inside this EFMI Mods folder, separate from the runtime.");
        ValidateRuntime();
    }

    public OutfitAdapterInspection Inspect()
    {
        List<IniDocument> documents = ReadProject();
        RuntimeFileSnapshot[] runtimeFiles = ValidateRuntime();
        string runtimeFingerprint = RuntimeFingerprint(runtimeFiles);
        bool alreadyAdapted = documents.Any(document => HasAdapterMarker(document));
        if (alreadyAdapted)
        {
            IReadOnlyList<OutfitAdapterDraw> existing = ScanDraws(documents);
            return new(Fingerprint(documents, runtimeFiles), existing, DisabledIniFiles(), [], [], true)
            {
                RuntimeFingerprint = runtimeFingerprint,
            };
        }

        HashSet<string> unsafeSections = FindUnsafeSections(documents);
        (List<string> missing, List<string> unreferencedMissing) = ValidateDependencies(documents);
        List<OutfitAdapterDraw> draws = ScanDraws(documents, unsafeSections);
        return new(Fingerprint(documents, runtimeFiles), draws, DisabledIniFiles(), missing, unreferencedMissing, false)
        {
            RuntimeFingerprint = runtimeFingerprint,
        };
    }

    public OutfitAdapterInspection Apply(string expectedFingerprint, bool requireStopped = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        EnsureProcesses(requireStopped);
        if (!requireStopped && IsDisabled(Path.GetRelativePath(modsRoot, root)))
            throw new InvalidOperationException("This outfit is disabled. Re-enable it and restart the loader before hot adaptation.");
        OutfitAdapterInspection inspection = Inspect();
        if (inspection.AlreadyAdapted)
            throw new InvalidOperationException("This outfit already contains EJ-OUTFIT/1 markers; double adaptation is refused.");
        if (!string.Equals(inspection.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new IOException("The outfit changed after inspection. Inspect it again before applying.");
        if (inspection.MissingRequiredResources.Count != 0)
            throw new InvalidDataException("Required outfit resources are missing: " +
                string.Join("; ", inspection.MissingRequiredResources));
        if (inspection.SupportedDraws.Count == 0)
            throw new InvalidDataException("No supported indexed draws were found.");

        List<IniDocument> documents = ReadProject();
        if (!string.Equals(Fingerprint(documents, ReadRuntimeFiles()), expectedFingerprint, StringComparison.Ordinal))
            throw new IOException("The outfit changed during inspection. Inspect it again before applying.");
        Dictionary<string, byte[]> originals = documents.ToDictionary(document => document.RelativePath,
            document => document.Bytes, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, byte[]> updated = new(StringComparer.OrdinalIgnoreCase);
        foreach (IniDocument document in documents)
        {
            OutfitAdapterDraw[] selected = inspection.SupportedDraws
                .Where(draw => draw.SourceFile.Equals(document.RelativePath, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length != 0)
                updated.Add(document.RelativePath, Encode(Patch(document, selected), document.Bytes));
        }

        string backupRoot = Path.Combine(root, BackupDirectoryName);
        EnsureUnlinked(backupRoot);
        Directory.CreateDirectory(backupRoot);
        EnsureUnlinked(backupRoot);
        string backup = Path.Combine(backupRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
            "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        Dictionary<string, DateTime> timestamps = updated.Keys.ToDictionary(path => path,
            path => File.GetLastWriteTimeUtc(Resolve(path)), StringComparer.OrdinalIgnoreCase);
        List<(string RelativePath, string Temporary)> staged = [];
        bool mutationStarted = false;
        Manifest manifest = new(1, "prepared", root, inspection.RuntimeFingerprint, updated.Keys.Select(path =>
            new ManifestFile(path, Hash(originals[path]), timestamps[path].ToString("O"),
                Hash(updated[path]), timestamps[path].ToString("O"))).ToArray());
        try
        {
            foreach (string relative in updated.Keys)
            {
                string backupFile = SafeBackupPath(backup, relative + ".bak");
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                WriteNewFile(backupFile, originals[relative]);
                File.SetLastWriteTimeUtc(backupFile, timestamps[relative]);
                string target = Resolve(relative);
                string temporary = Path.Combine(Path.GetDirectoryName(target)!,
                    "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                staged.Add((relative, temporary));
                WriteNewFile(temporary, updated[relative]);
            }
            WriteManifest(backup, manifest);
            EnsureProcesses(requireStopped);
            if (!string.Equals(Fingerprint(ReadProject(), ReadRuntimeFiles()), expectedFingerprint, StringComparison.Ordinal))
                throw new IOException("The outfit changed while backups were being prepared.");
            foreach ((string relative, string temporary) in staged)
            {
                string target = Resolve(relative);
                EnsureUnlinked(target);
                EnsureProcesses(requireStopped);
                EnsureRuntimeFingerprint(inspection.RuntimeFingerprint);
                if (!File.Exists(target) ||
                    !Hash(File.ReadAllBytes(target)).Equals(Hash(originals[relative]), StringComparison.Ordinal) ||
                    File.GetLastWriteTimeUtc(target) != timestamps[relative])
                    throw new IOException($"The outfit changed during apply: {relative}");
                File.Move(temporary, target, overwrite: true);
                mutationStarted = true;
                File.SetLastWriteTimeUtc(target, timestamps[relative]);
            }
            EnsureProcesses(requireStopped);
            EnsureRuntimeFingerprint(inspection.RuntimeFingerprint);
            WriteManifest(backup, manifest with { Status = "applied" });
            return Inspect();
        }
        catch (Exception error)
        {
            if (!mutationStarted)
            {
                TrySetStatus(backup, manifest with { Status = "not-applied" });
                throw;
            }
            try
            {
                foreach (string relative in updated.Keys)
                {
                    EnsureProcesses(requireStopped);
                    string target = Resolve(relative);
                    string currentHash = File.Exists(target) ? Hash(File.ReadAllBytes(target)) : "";
                    bool originalOwned = currentHash.Equals(Hash(originals[relative]), StringComparison.Ordinal) &&
                        File.GetLastWriteTimeUtc(target) == timestamps[relative];
                    bool appliedOwned = currentHash.Equals(Hash(updated[relative]), StringComparison.Ordinal) &&
                        File.GetLastWriteTimeUtc(target) == timestamps[relative];
                    if (!originalOwned && !appliedOwned)
                        throw new IOException($"Refusing to roll back externally changed file: {relative}");
                    if (!originalOwned)
                    {
                        AtomicWrite(target, originals[relative]);
                        File.SetLastWriteTimeUtc(target, timestamps[relative]);
                    }
                }
                WriteManifest(backup, manifest with { Status = "rolled-back" });
            }
            catch (Exception rollbackError)
            {
                throw new IOException($"Apply failed: {error.Message}. Rollback also failed: {rollbackError.Message}. Backup: {backup}",
                    new AggregateException(error, rollbackError));
            }
            throw;
        }
        finally
        {
            foreach ((_, string temporary) in staged)
                if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Restore(bool requireStopped = true)
    {
        EnsureProcesses(requireStopped);
        if (!requireStopped && IsDisabled(Path.GetRelativePath(modsRoot, root)))
            throw new InvalidOperationException("This outfit is disabled. Re-enable it and restart the loader before hot restore.");
        string? backup = EnumerateAppliedBackups().OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
        if (backup is null)
            throw new DirectoryNotFoundException("No restorable outfit adaptation backup was found.");
        EnsureUnlinked(backup);
        Manifest manifest = ReadManifest(backup);
        if (manifest.Schema != 1 || manifest.Status != "applied" || !SamePath(manifest.Root, root) ||
            manifest.Files.Length == 0)
            throw new InvalidDataException("The outfit backup manifest is invalid.");

        Dictionary<string, byte[]> current = [];
        Dictionary<string, DateTime> currentTimes = [];
        Dictionary<string, byte[]> originals = [];
        foreach (ManifestFile file in manifest.Files)
        {
            string target = Resolve(file.Path);
            EnsureUnlinked(target);
            if (!File.Exists(target))
                throw new IOException($"Refusing to restore a missing file: {file.Path}");
            byte[] bytes = File.ReadAllBytes(target);
            if (!Hash(bytes).Equals(file.AppliedSha256, StringComparison.OrdinalIgnoreCase) ||
                !File.GetLastWriteTimeUtc(target).ToString("O").Equals(file.AppliedLastWriteTimeUtc, StringComparison.Ordinal))
                throw new IOException($"Refusing to overwrite an externally changed file: {file.Path}");
            current.Add(file.Path, bytes);
            currentTimes.Add(file.Path, File.GetLastWriteTimeUtc(target));
            string source = SafeBackupPath(backup, file.Path + ".bak");
            EnsureUnlinked(source);
            byte[] original = File.ReadAllBytes(source);
            DateTime backupTime = File.GetLastWriteTimeUtc(source);
            DateTime originalTime = DateTime.Parse(file.OriginalLastWriteTimeUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            if (!Hash(original).Equals(file.OriginalSha256, StringComparison.OrdinalIgnoreCase) ||
                backupTime != originalTime)
                throw new InvalidDataException($"Backup content is damaged: {file.Path}");
            originals.Add(file.Path, original);
        }

        bool changed = false;
        try
        {
            foreach (ManifestFile file in manifest.Files)
            {
                EnsureProcesses(requireStopped);
                string target = Resolve(file.Path);
                if (!File.Exists(target) ||
                    !Hash(File.ReadAllBytes(target)).Equals(file.AppliedSha256, StringComparison.OrdinalIgnoreCase) ||
                    !File.GetLastWriteTimeUtc(target).ToString("O").Equals(file.AppliedLastWriteTimeUtc, StringComparison.Ordinal))
                    throw new IOException($"Refusing to overwrite an externally changed file: {file.Path}");
                AtomicWrite(target, originals[file.Path]);
                changed = true;
                File.SetLastWriteTimeUtc(target,
                    DateTime.Parse(file.OriginalLastWriteTimeUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
            }
            EnsureProcesses(requireStopped);
            WriteManifest(backup, manifest with { Status = "restored" });
        }
        catch (Exception error)
        {
            if (changed)
            {
                try
                {
                    foreach (string relative in current.Keys)
                    {
                        EnsureProcesses(requireStopped);
                        string target = Resolve(relative);
                        if (!File.Exists(target))
                            throw new IOException($"Refusing to roll back a missing file: {relative}");
                        byte[] bytes = File.ReadAllBytes(target);
                        if (Hash(bytes).Equals(Hash(originals[relative]), StringComparison.Ordinal) &&
                            File.GetLastWriteTimeUtc(target) ==
                            DateTime.Parse(manifest.Files.Single(file => file.Path.Equals(relative, StringComparison.OrdinalIgnoreCase))
                                .OriginalLastWriteTimeUtc, null,
                                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime())
                            continue;
                        ManifestFile owner = manifest.Files.Single(file =>
                            file.Path.Equals(relative, StringComparison.OrdinalIgnoreCase));
                        if (!Hash(bytes).Equals(owner.AppliedSha256, StringComparison.OrdinalIgnoreCase) ||
                            !File.GetLastWriteTimeUtc(target).ToString("O").Equals(owner.AppliedLastWriteTimeUtc,
                                StringComparison.Ordinal))
                            throw new IOException($"Refusing to roll back an externally changed file: {relative}");
                        AtomicWrite(target, current[relative]);
                        File.SetLastWriteTimeUtc(target, currentTimes[relative]);
                    }
                }
                catch (Exception rollbackError)
                {
                    throw new IOException($"Restore failed: {error.Message}. Rollback also failed: {rollbackError.Message}. Backup: {backup}",
                        new AggregateException(error, rollbackError));
                }
            }
            throw;
        }
    }

    private List<IniDocument> ReadProject()
    {
        EnsureUnlinked(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Select an existing outfit directory.");
        List<string> inis = [];
        foreach (string path in EnumerateFilesSafely())
        {
            string relative = Relative(path);
            if (IsDisabled(relative)) continue;
            if (Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                inis.Add(path);
        }
        if (inis.Count == 0)
            throw new InvalidDataException("No active outfit INI files were found.");
        return inis.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => ParseIni(path, Relative(path), File.ReadAllBytes(path))).ToList();
    }

    private List<string> DisabledIniFiles()
    {
        if (!Directory.Exists(root)) return [];
        return EnumerateFilesSafely()
            .Where(path => Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
            .Select(Relative)
            .Where(IsDisabled).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<string> EnumerateFilesSafely()
    {
        Stack<string> directories = new();
        directories.Push(root);
        while (directories.Count != 0)
        {
            string directory = directories.Pop();
            EnsureUnlinked(directory);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureUnlinked(file);
                yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureUnlinked(child);
                directories.Push(child);
            }
        }
    }

    private static bool HasAdapterMarker(IniDocument document) =>
        document.Lines.Any(line => Regex.IsMatch(StripEol(line),
            @"^[ \t]*;\s*EJ-OUTFIT/1 (?:BEGIN|END) D[0-9A-F]{16}\s*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));

    private List<OutfitAdapterDraw> ScanDraws(IReadOnlyList<IniDocument> documents, HashSet<string>? unsafeSections = null)
    {
        List<OutfitAdapterDraw> result = [];
        foreach (IniDocument document in documents)
        foreach (IniSection section in document.Sections)
        {
            List<string> branches = [];
            foreach (int lineIndex in section.LineIndices)
            {
                string line = StripEol(document.Lines[lineIndex]);
                string trimmed = line.Trim();
                Match branch = Regex.Match(trimmed, @"^(if|elif|else if)\s+(.+)$", RegexOptions.IgnoreCase);
                if (branch.Success)
                {
                    if (!branch.Groups[1].Value.Equals("if", StringComparison.OrdinalIgnoreCase) && branches.Count > 0)
                        branches.RemoveAt(branches.Count - 1);
                    branches.Add(trimmed);
                }
                else if (trimmed.Equals("else", StringComparison.OrdinalIgnoreCase) && branches.Count > 0)
                    branches[^1] = "else";
                else if (trimmed.Equals("endif", StringComparison.OrdinalIgnoreCase) && branches.Count > 0)
                    branches.RemoveAt(branches.Count - 1);

                Match match = Regex.Match(line,
                    @"^(?<indent>[ \t]*)(?<key>(?:post\s+)?draw\w*)\s*=\s*(?<args>[^;]*?)(?<comment>\s*;.*)?$",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                string key = match.Groups["key"].Value.ToLowerInvariant();
                string[] args = Regex.Split(match.Groups["args"].Value.Trim(), @"\s*,\s*");
                string reason = "";
                string count = "", first = "", @base = "", instances = "1", firstInstance = "0";
                if (unsafeSections?.Contains(SectionKey(document, section)) == true) reason = "custom-vertex-stage";
                else if (!Regex.IsMatch(section.Name, @"^(TextureOverride|CommandList|CustomShader)", RegexOptions.IgnoreCase))
                    reason = "non-mesh-section";
                else if (key is not ("drawindexed" or "drawindexedinstanced")) reason = "unsupported-draw-command";
                else if (key == "drawindexed" && args.Length != 3 || key == "drawindexedinstanced" && args.Length != 5)
                    reason = "explicit-index-range-required";
                else
                {
                    count = args[0];
                    if (key == "drawindexedinstanced")
                    {
                        instances = args[1]; first = args[2]; @base = args[3]; firstInstance = args[4];
                    }
                    else { first = args[1]; @base = args[2]; }
                    if (!ulong.TryParse(count, out ulong countValue) ||
                        !ulong.TryParse(first, out ulong firstValue) ||
                        !long.TryParse(@base, out long baseValue) ||
                        instances is not ("1" or "INSTANCE_COUNT") ||
                        firstInstance is not ("0" or "FIRST_INSTANCE"))
                        reason = "unsupported-range-or-instance-expression";
                    else if (countValue < 3) reason = "empty-or-short-draw";
                    else if (countValue > MaximumExactInteger || firstValue > MaximumExactInteger ||
                        Math.Abs((double)baseValue) > MaximumExactInteger || firstValue + countValue > MaximumExactInteger)
                        reason = "range-exceeds-exact-ini-integer";
                }
                string identity = document.RelativePath.Replace('\\', '/') + ":" + (lineIndex + 1) + ":" + line.Trim();
                string id = "D" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
                result.Add(new(id, document.RelativePath, section.Name, lineIndex + 1, line, string.Join(" / ", branches),
                    count, first, @base, instances, firstInstance, reason.Length == 0, reason));
            }
        }
        return result;
    }

    private HashSet<string> FindUnsafeSections(IReadOnlyList<IniDocument> documents)
    {
        Dictionary<string, HashSet<string>> connectedSections = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (IniDocument Document, IniSection Section)> sections = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> unsafeSections = new(StringComparer.OrdinalIgnoreCase);
        foreach (IniDocument document in documents)
        foreach (IniSection section in document.Sections)
        {
            string key = SectionKey(document, section);
            sections.Add(key, (document, section));
            connectedSections.Add(key, new(StringComparer.OrdinalIgnoreCase));
            if (section.LineIndices.Any(index => Regex.IsMatch(StripEol(document.Lines[index]),
                    @"^\s*(?:post\s+)?(?:vs|gs|hs|ds)\s*=", RegexOptions.IgnoreCase)))
                unsafeSections.Add(key);
        }

        foreach ((string callerKey, (IniDocument document, IniSection section)) in sections)
        foreach (string call in section.Calls)
        foreach ((IniDocument targetDocument, IniSection targetSection) in ResolveCallTargets(document, call, documents))
        {
            string targetKey = SectionKey(targetDocument, targetSection);
            connectedSections[callerKey].Add(targetKey);
            connectedSections[targetKey].Add(callerKey);
        }

        Queue<string> queue = new(unsafeSections);
        while (queue.Count != 0)
        {
            foreach (string connected in connectedSections[queue.Dequeue()])
                if (unsafeSections.Add(connected))
                    queue.Enqueue(connected);
        }
        return unsafeSections;
    }

    private static IEnumerable<(IniDocument Document, IniSection Section)> ResolveCallTargets(
        IniDocument caller, string call, IReadOnlyList<IniDocument> documents)
    {
        Match qualified = Regex.Match(call, @"^CommandList\\(.+)\\([^\\]+)$", RegexOptions.IgnoreCase);
        string name = qualified.Success ? "CommandList" + qualified.Groups[2].Value : call;
        IEnumerable<IniDocument> candidates = qualified.Success
            ? documents.Where(document => document.Namespace.Equals(qualified.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            : caller.Namespace.Length == 0
                ? [caller]
                : documents.Where(document => document.Namespace.Equals(caller.Namespace, StringComparison.OrdinalIgnoreCase));
        foreach (IniDocument document in candidates)
        foreach (IniSection section in document.Sections.Where(section =>
                     section.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            yield return (document, section);
    }

    private (List<string> Missing, List<string> UnreferencedMissing) ValidateDependencies(IReadOnlyList<IniDocument> documents)
    {
        Dictionary<string, HashSet<string>> resourceEdges = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> used = new();
        foreach (IniDocument document in documents)
        foreach (IniSection section in document.Sections)
        {
            bool resource = Regex.IsMatch(section.Name, @"^Resource", RegexOptions.IgnoreCase);
            if (resource && !resourceEdges.ContainsKey(section.Name))
                resourceEdges.Add(section.Name, new(StringComparer.OrdinalIgnoreCase));
            foreach (int index in section.LineIndices)
            foreach (string reference in ResourceReferences(StripEol(document.Lines[index])))
            {
                if (resource) resourceEdges[section.Name].Add(reference);
                else used.Enqueue(reference);
            }
        }
        HashSet<string> requiredResources = new(StringComparer.OrdinalIgnoreCase);
        while (used.Count != 0)
        {
            string name = used.Dequeue();
            if (!requiredResources.Add(name) || !resourceEdges.TryGetValue(name, out HashSet<string>? dependencies)) continue;
            foreach (string dependency in dependencies) used.Enqueue(dependency);
        }

        List<string> missing = [], unreferenced = [];
        foreach (IniDocument document in documents)
        foreach (IniSection section in document.Sections)
        foreach (int index in section.LineIndices)
        {
            Match reference = Regex.Match(StripEol(document.Lines[index]),
                @"^\s*(?<key>filename|include|include_recursive|vs|ps|cs|gs|hs|ds)\s*=\s*(?<path>[^;\r\n]+)",
                RegexOptions.IgnoreCase);
            if (!reference.Success) continue;
            string key = reference.Groups["key"].Value;
            string value = reference.Groups["path"].Value.Trim().Trim('"');
            if (Regex.IsMatch(key, @"^(vs|ps|cs|gs|hs|ds)$", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(value, @"^(null|from_caller)$|^(ref|copy)\s+", RegexOptions.IgnoreCase)) continue;
            if (value.IndexOfAny(['*', '$']) >= 0)
                throw new InvalidDataException($"Dynamic file reference requires manual review: {document.RelativePath}:{index + 1}");
            string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document.FullPath)!, value));
            if (!IsWithin(root, target))
                throw new InvalidDataException($"File dependency leaves the outfit directory: {document.RelativePath} -> {value}");
            EnsureUnlinked(target);
            bool required = !(key.Equals("filename", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(section.Name, @"^Resource[-\p{L}\p{N}_.]+$") && !requiredResources.Contains(section.Name));
            bool exists = key.Equals("include_recursive", StringComparison.OrdinalIgnoreCase)
                ? Directory.Exists(target)
                : File.Exists(target);
            if (exists) continue;
            string description = $"{document.RelativePath}:{index + 1} [{section.Name}] -> {value}";
            (required ? missing : unreferenced).Add(description);
        }
        return (missing, unreferenced);
    }

    private static IEnumerable<string> ResourceReferences(string line)
    {
        string code = line.Trim();
        if (Regex.IsMatch(code, @"^(;|#|//)") ||
            Regex.IsMatch(code, @"^(filename|include|include_recursive|data|vs|ps|cs|gs|hs|ds)\s*=", RegexOptions.IgnoreCase))
            yield break;
        code = code.Split(';', 2)[0].Replace("->", " ", StringComparison.Ordinal);
        foreach (Match match in Regex.Matches(code,
            @"(?i)(?<![\p{L}\p{N}_$])Resource(?:\\[^\s,=;()\[\]><!]+|[-\p{L}\p{N}_.]+)"))
        {
            string token = match.Value;
            yield return token.Contains('\\') ? "Resource" + token.Split('\\')[^1] : token;
        }
    }

    private string Patch(IniDocument document, IReadOnlyList<OutfitAdapterDraw> draws)
    {
        Dictionary<int, OutfitAdapterDraw> selected = draws.ToDictionary(draw => draw.SourceLine - 1);
        HashSet<string> sectionNames = document.Sections.Select(section => section.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (OutfitAdapterDraw draw in draws)
            if (sectionNames.Contains("CommandListEJOutfit" + draw.Id))
                throw new InvalidDataException($"Generated command-list collision in {document.RelativePath}.");
        string eol = document.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" :
            document.Text.Contains('\r') ? "\r" : "\n";
        StringBuilder output = new(document.Text.Length + draws.Count * 400);
        for (int index = 0; index < document.Lines.Length; index++)
        {
            string line = document.Lines[index];
            if (!selected.TryGetValue(index, out OutfitAdapterDraw? draw))
            {
                output.Append(line);
                continue;
            }
            string indent = Regex.Match(draw.Command, @"^[ \t]*").Value;
            output.Append(indent).Append("; EJ-OUTFIT/1 BEGIN ").Append(draw.Id).Append(eol)
                .Append(indent).Append("run = CommandListEJOutfit").Append(draw.Id).Append(eol)
                .Append(line);
            if (!line.EndsWith('\n') && !line.EndsWith('\r')) output.Append(eol);
            output.Append(indent).Append("run = CommandList\\EndfieldJiggleEFMI\\EndOutfitDraw").Append(eol)
                .Append(indent).Append("; EJ-OUTFIT/1 END ").Append(draw.Id).Append(eol);
        }
        foreach (OutfitAdapterDraw draw in draws)
        {
            output.Append(eol).Append("[CommandListEJOutfit").Append(draw.Id).Append(']').Append(eol)
                .Append("$\\EndfieldJiggleEFMI\\outfit_count = ").Append(draw.Count).Append(eol)
                .Append("$\\EndfieldJiggleEFMI\\outfit_first = ").Append(draw.FirstIndex).Append(eol)
                .Append("$\\EndfieldJiggleEFMI\\outfit_base = ").Append(draw.BaseVertex).Append(eol)
                .Append("$\\EndfieldJiggleEFMI\\outfit_instances = ").Append(draw.InstanceCount).Append(eol)
                .Append("$\\EndfieldJiggleEFMI\\outfit_first_instance = ").Append(draw.FirstInstance).Append(eol)
                .Append("$\\EndfieldJiggleEFMI\\outfit_enabled = 1").Append(eol)
                .Append("run = CommandList\\EndfieldJiggleEFMI\\BeginOutfitDraw").Append(eol);
        }
        return output.ToString();
    }

    private string Fingerprint(IReadOnlyList<IniDocument> documents, IReadOnlyList<RuntimeFileSnapshot> runtimeFiles)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (IniDocument document in documents.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(document.RelativePath.ToLowerInvariant()));
            hash.AppendData([0]);
            hash.AppendData(SHA256.HashData(document.Bytes));
            hash.AppendData(BitConverter.GetBytes(File.GetLastWriteTimeUtc(document.FullPath).Ticks));
        }
        hash.AppendData(Convert.FromHexString(RuntimeFingerprint(runtimeFiles)));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private RuntimeFileSnapshot[] ValidateRuntime()
    {
        RuntimeFileSnapshot[] before = ReadRuntimeFiles();
        RuntimeSettingsStore settingsStore = new(runtime);
        _ = settingsStore.Read();
        RuntimeFileSnapshot[] files = ReadRuntimeFiles();
        if (!RuntimeFingerprint(before).Equals(RuntimeFingerprint(files), StringComparison.Ordinal))
            throw new IOException("Runtime files changed while the reviewed adapter contract was being validated.");
        Dictionary<string, RuntimeDocument> inis = files.ToDictionary(file => file.Name,
            file => ParseRuntimeIni(file.Name, Decode(file.Bytes)), StringComparer.OrdinalIgnoreCase);
        foreach ((string name, RuntimeDocument document) in inis)
        {
            if (document.NamespaceCount != 1 || document.Namespace != "EndfieldJiggleEFMI")
                throw new InvalidDataException($"{name} must have exactly one EndfieldJiggleEFMI namespace.");
        }

        RuntimeDocument outfits = inis["Outfits.ini"];
        RequireSection(outfits, "CommandListBeginOutfitDraw");
        RequireSection(outfits, "CommandListEndOutfitDraw");
        foreach (string variable in new[] { "outfit_count", "outfit_first", "outfit_base",
                     "outfit_instances", "outfit_first_instance", "outfit_enabled" })
            if (!outfits.Text.Contains("$" + variable, StringComparison.Ordinal))
                throw new InvalidDataException($"Runtime Outfits.ini is missing ${variable}.");
        if (!Regex.IsMatch(inis["EndfieldJiggle.ini"].Text,
                @"(?m)^\s*drawindexedinstanced\s*=\s*\$outfit_count\s*,\s*1\s*,\s*\$outfit_first\s*,\s*\$outfit_base\s*,\s*0\s*$",
                RegexOptions.IgnoreCase))
            throw new InvalidDataException("Runtime picker does not consume the explicit outfit index range.");
        foreach (int picker in new[] { 5, 7 })
        {
            RuntimeSection[] pickers = inis["EndfieldJiggle.ini"].Sections.Where(section =>
                section.Name.Equals("CustomShaderPick" + picker, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pickers.Length != 1 ||
                !pickers[0].Lines.Any(line => Regex.IsMatch(line,
                    @"^\s*drawindexedinstanced\s*=\s*\$outfit_count\s*,\s*1\s*,\s*\$outfit_first\s*,\s*\$outfit_base\s*,\s*0\s*$",
                    RegexOptions.IgnoreCase)) ||
                !pickers[0].Lines.Any(line => line.Trim().Equals("draw = from_caller", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Runtime CustomShaderPick{picker} does not preserve the caller draw fallback.");
        }

        List<RuntimeSection> passPrograms = inis["Passes.ini"].Sections
            .Where(section => Regex.IsMatch(section.Name, @"^ShaderRegexEJ_[0-9a-f]{16}$", RegexOptions.IgnoreCase))
            .ToList();
        Dictionary<int, string> hashesByProgram = [];
        for (int program = 1; program <= 12; program++)
        {
            string commandName = "CommandListBindPass" + program.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RuntimeSection[] bindCommands = inis["Passes.ini"].Sections.Where(section =>
                section.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (bindCommands.Length != 1 || !bindCommands[0].Lines.Any(line =>
                    Regex.IsMatch(line, @"^\s*if\s+!\$outfit_seen\s*&&\s*INDEX_COUNT\s*>=\s*3\b",
                        RegexOptions.IgnoreCase)))
                throw new InvalidDataException($"Runtime {commandName} is missing its outfit exclusion.");
            RuntimeSection[] matching = passPrograms.Where(section => section.Lines.Count(line =>
                Regex.IsMatch(line, @"^\s*run\s*=\s*" + Regex.Escape(commandName) + @"\s*$",
                    RegexOptions.IgnoreCase)) == 1).ToArray();
            if (matching.Length != 1)
                throw new InvalidDataException($"Runtime Passes.ini must map {commandName} to exactly one shader hash.");
            hashesByProgram.Add(program, matching[0].Name["ShaderRegexEJ_".Length..]);
        }
        int passCount = inis["Passes.ini"].Sections.Count(section =>
            section.Name.StartsWith("CommandListBindPass", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(section.Name, @"^CommandListBindPass(?:[1-9]|1[0-2])$",
                RegexOptions.IgnoreCase));
        if (passCount != 12 || Regex.Matches(inis["Passes.ini"].Text,
                @"(?m)^\s*if\s+!\$outfit_seen\s*&&\s*INDEX_COUNT\s*>=\s*3\b",
                RegexOptions.IgnoreCase).Count != 12)
            throw new InvalidDataException("Runtime Passes.ini does not have all 12 reviewed outfit exclusions.");

        for (int program = 1; program <= 12; program++)
        {
            RuntimeSection[] callbacks = outfits.Sections.Where(section =>
                section.Name.Equals("ShaderOverrideEJOutfitProgram" + program,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            string hash = hashesByProgram[program];
            if (callbacks.Length != 1 ||
                !callbacks[0].Lines.Any(line => line.Trim().Equals("hash = " + hash, StringComparison.OrdinalIgnoreCase)) ||
                !callbacks[0].Lines.Any(line => line.Trim().Equals("$outfit_program = " + program,
                    StringComparison.OrdinalIgnoreCase)) ||
                !callbacks[0].Lines.Any(line => line.Trim().Equals("allow_duplicate_hash = true",
                    StringComparison.OrdinalIgnoreCase)) ||
                !callbacks[0].Lines.Any(line => line.Trim().Equals("post $outfit_program = 0",
                    StringComparison.OrdinalIgnoreCase)) ||
                !callbacks[0].Lines.Any(line => line.Trim().Equals("$outfit_seen = 0",
                    StringComparison.OrdinalIgnoreCase)) ||
                callbacks[0].Lines.Any(line => Regex.IsMatch(line, @"^\s*filter_index\s*=",
                    RegexOptions.IgnoreCase)))
                throw new InvalidDataException($"Runtime outfit callback {program} does not match its native pass.");
        }
        return files;
    }

    private RuntimeFileSnapshot[] ReadRuntimeFiles()
    {
        string[] names = ["EndfieldJiggle.ini", "Passes.ini", "Outfits.ini"];
        return names.Select(name =>
        {
            string path = Path.Combine(runtime, name);
            EnsureUnlinked(path);
            if (!File.Exists(path)) throw new FileNotFoundException($"Runtime directory is missing {name}.");
            return new RuntimeFileSnapshot(name, File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));
        }).ToArray();
    }

    private void EnsureRuntimeFingerprint(string expected)
    {
        if (!RuntimeFingerprintMatches(expected))
            throw new IOException("The EFMI runtime changed after outfit inspection; no further writes are permitted.");
    }

    private bool RuntimeFingerprintMatches(string expected)
    {
        try { return RuntimeFingerprint(ReadRuntimeFiles()).Equals(expected, StringComparison.Ordinal); }
        catch (IOException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private static string RuntimeFingerprint(IReadOnlyList<RuntimeFileSnapshot> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RuntimeFileSnapshot file in files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Name.ToLowerInvariant()));
            hash.AppendData([0]);
            hash.AppendData(SHA256.HashData(file.Bytes));
            hash.AppendData(BitConverter.GetBytes(file.LastWriteTimeUtc.Ticks));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static RuntimeDocument ParseRuntimeIni(string name, string text)
    {
        int namespaceCount = 0;
        string? namespaceName = null;
        List<RuntimeSection> sections = [];
        RuntimeSection? section = null;
        foreach (string raw in Regex.Split(text, @"\r\n|\n|\r"))
        {
            Match header = Regex.Match(raw, @"^\s*\[([^\]]+)\]\s*(?:;.*)?$");
            if (header.Success)
            {
                section = new(header.Groups[1].Value.Trim(), []);
                sections.Add(section);
                continue;
            }
            if (section is not null)
            {
                section.Lines.Add(raw);
                continue;
            }
            Match ns = Regex.Match(raw, @"^\s*namespace\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            if (ns.Success)
            {
                namespaceCount++;
                namespaceName = ns.Groups[1].Value.Trim();
            }
        }
        return new(name, text, namespaceCount, namespaceName ?? "", sections);
    }

    private static void RequireSection(RuntimeDocument document, string name)
    {
        if (document.Sections.Count(section => section.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidDataException($"Runtime Outfits.ini must contain exactly one [{name}] section.");
    }

    private void EnsureProcesses(bool requireStopped)
    {
        if (requireStopped && RuntimeSettingsStore.IsGameRunning())
            throw new InvalidOperationException("Exit the game and XXMI before applying or restoring an outfit.");
        int current = Environment.ProcessId;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == current) continue;
                string name;
                try { name = process.ProcessName; }
                catch (InvalidOperationException) { continue; }
                if (name.Contains("XXMI", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("loader_host", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Exit XXMI before writing outfit INI files.");
            }
        }
    }

    private IReadOnlyList<string> EnumerateAppliedBackups()
    {
        List<string> result = [];
        string directory = Path.Combine(root, BackupDirectoryName);
        EnsureUnlinked(directory);
        if (!Directory.Exists(directory)) return result;
        foreach (string item in Directory.EnumerateDirectories(directory))
        {
            EnsureUnlinked(item);
            try
            {
                Manifest manifest = ReadManifest(item);
                if (manifest.Schema == 1 && manifest.Status == "applied" && SamePath(manifest.Root, root))
                    result.Add(item);
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return result;
    }

    private static IniDocument ParseIni(string path, string relative, byte[] bytes)
    {
        string text = Decode(bytes);
        string[] lines = Regex.Matches(text, @"[^\r\n]*(?:\r\n|\n|\r|$)")
            .Select(match => match.Value).Where(value => value.Length != 0).ToArray();
        List<IniSection> sections = [];
        IniSection? section = null;
        string ns = "";
        for (int i = 0; i < lines.Length; i++)
        {
            string line = StripEol(lines[i]);
            Match header = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*(?:;.*)?$");
            if (header.Success)
            {
                section = new(header.Groups[1].Value.Trim(), i, [], []);
                sections.Add(section);
            }
            else if (section is not null)
            {
                section.LineIndices.Add(i);
                Match call = Regex.Match(line, @"^\s*(?:post\s+)?run\s*=\s*(CommandList[^\s;]+)\s*(?:;.*)?$", RegexOptions.IgnoreCase);
                if (call.Success) section.Calls.Add(call.Groups[1].Value);
            }
            else
            {
                Match namespaceMatch = Regex.Match(line, @"^\s*namespace\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
                if (namespaceMatch.Success) ns = namespaceMatch.Groups[1].Value.Trim();
            }
        }
        return new(path, relative, bytes, text, lines, sections, ns);
    }

    private string Resolve(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsWithin(root, path)) throw new InvalidDataException("Path leaves the selected outfit directory.");
        EnsureUnlinked(path);
        return path;
    }

    private string Relative(string path) => Path.GetRelativePath(root, path);
    private static string SectionKey(IniDocument document, IniSection section) =>
        document.RelativePath.Replace('\\', '/') + "\0" + document.Namespace + "\0" +
        section.Name + "\0" + section.FirstLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static bool IsDisabled(string relative) => relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
        .Any(part => part.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".git", StringComparison.OrdinalIgnoreCase));
    private static bool SamePath(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    private static bool IsWithin(string parent, string path) =>
        path.StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    private static string StripEol(string line) => line.TrimEnd('\r', '\n');

    private static void EnsureUnlinked(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Reparse points are not allowed: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string SafeBackupPath(string backup, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(backup, relative));
        if (!IsWithin(backup, path)) throw new InvalidDataException("Backup path escaped its directory.");
        EnsureUnlinked(path);
        return path;
    }

    private static void WriteNewFile(string path, byte[] bytes)
    {
        EnsureUnlinked(path);
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void AtomicWrite(string target, byte[] bytes)
    {
        EnsureUnlinked(target);
        string temp = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteNewFile(temp, bytes);
            EnsureUnlinked(target);
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Decode(byte[] bytes)
    {
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
    }

    private static byte[] Encode(string text, byte[] original)
    {
        bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
        byte[] encoded = new UTF8Encoding(false, true).GetBytes(text);
        if (!bom) return encoded;
        return [0xEF, 0xBB, 0xBF, .. encoded];
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private void WriteManifest(string directory, Manifest manifest)
    {
        string path = Path.Combine(directory, "manifest.json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteNewFile(temporary, new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(manifest, JsonOptions)));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void TrySetStatus(string directory, Manifest manifest)
    {
        try { WriteManifest(directory, manifest); }
        catch (IOException) { }
    }

    private Manifest ReadManifest(string directory)
    {
        string path = Path.Combine(directory, "manifest.json");
        EnsureUnlinked(path);
        return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Backup manifest is empty.");
    }

    private sealed record Manifest(int Schema, string Status, string Root, string RuntimeFingerprint,
        ManifestFile[] Files);
    private sealed record ManifestFile(string Path, string OriginalSha256, string OriginalLastWriteTimeUtc,
        string AppliedSha256, string AppliedLastWriteTimeUtc);
    private sealed record RuntimeFileSnapshot(string Name, byte[] Bytes, DateTime LastWriteTimeUtc);
    private sealed record RuntimeDocument(string Name, string Text, int NamespaceCount, string Namespace,
        List<RuntimeSection> Sections);
    private sealed record RuntimeSection(string Name, List<string> Lines);
    private sealed record IniSection(string Name, int FirstLine, List<int> LineIndices, List<string> Calls);
    private sealed record IniDocument(string FullPath, string RelativePath, byte[] Bytes, string Text,
        string[] Lines, List<IniSection> Sections, string Namespace);
}
