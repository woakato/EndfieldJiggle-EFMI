using System.Text;
using System.Text.RegularExpressions;

namespace EndfieldJiggle.Configurator.Core;

public sealed record ModLibraryEntry(
    string Name,
    string Directory,
    string State,
    string? PreviewPath,
    bool Disabled,
    int IniCount);

public sealed record KeyConflict(
    string Action,
    string Binding,
    string SourceFile,
    int SourceLine,
    string Section,
    string Condition);

public static class ModLibraryStore
{
    private static readonly HashSet<string> ResourceDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "texture", "textures", "resource", "resources" };
    private static readonly HashSet<string> PreviewNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "preview.png", "preview.jpg", "cover.png", "cover.jpg", "logo.png",
        };
    private static readonly Regex SectionPattern = new(@"^\s*\[([^\]]+)\]", RegexOptions.CultureInvariant);
    private static readonly Regex EntryPattern =
        new(@"^\s*([^;#=]+?)\s*=\s*(.*?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex AdapterMarker =
        new(@"^\s*;\s*EJ-OUTFIT/1(?:\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ModLibraryEntry> Scan(string modsDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        string root = Path.GetFullPath(modsDirectory);
        if (!Directory.Exists(root) || IsReparsePoint(root))
            return [];

        List<ModLibraryEntry> entries = [];
        FindProjects(root, root, entries, cancellationToken);
        return entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Directory, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<KeyConflict> FindConflicts(
        string efmiDirectory,
        string runtimeDirectory,
        TouchSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(efmiDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(settings);

        string efmi = Path.GetFullPath(efmiDirectory);
        string runtime = Path.GetFullPath(runtimeDirectory);
        List<IniBinding> bindings = [];
        string d3dx = Path.Combine(efmi, "d3dx.ini");
        ReadBindingsIfEnabled(d3dx, efmi, bindings, cancellationToken);

        string mods = Path.Combine(efmi, "Mods");
        if (Directory.Exists(mods))
            ReadTreeBindings(mods, mods, bindings, cancellationToken);

        string core = Path.Combine(efmi, "Core");
        if (Directory.Exists(core))
            ReadTreeBindings(core, core, bindings, cancellationToken);

        List<KeyConflict> conflicts = [];
        foreach ((string action, KeyBinding touch) in TouchBindings(settings))
        foreach (IniBinding candidate in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.SourceFile.StartsWith(runtime.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            if (BindingsOverlap(touch, candidate) ||
                IsShiftDragMenuOverlap(touch, candidate))
            {
                conflicts.Add(new(action, FormatBinding(candidate), candidate.SourceFile,
                    candidate.SourceLine, candidate.Section, candidate.Condition));
            }
        }

        return conflicts
            .Distinct()
            .OrderBy(conflict => conflict.Action, StringComparer.OrdinalIgnoreCase)
            .ThenBy(conflict => conflict.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(conflict => conflict.SourceLine)
            .ToArray();
    }

    private static void FindProjects(string root, string directory, List<ModLibraryEntry> entries, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsExcluded(root, directory) || IsReparsePoint(directory))
            return;

        string[] directIni = EnumerateDirectIni(directory);
        if (directIni.Length != 0 && !directory.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            List<string> allIni = [];
            CollectIniFiles(root, directory, allIni, token);
            bool disabled = IsDisabled(root, directory);
            bool adapted = !disabled && allIni.Where(path => !IsDisabled(root, path))
                .Any(path => HasAdapterMarker(path, token));
            entries.Add(new(
                Path.GetFileName(directory),
                directory,
                adapted ? "Adapter" : "Enabled",
                FindPreview(directory),
                disabled,
                allIni.Count));
            return;
        }

        foreach (string child in EnumerateSafeDirectories(directory))
        {
            if (ResourceDirectories.Contains(Path.GetFileName(child)) ||
                IsExcluded(root, child))
                continue;
            FindProjects(root, child, entries, token);
        }
    }

    private static string[] EnumerateDirectIni(string directory) =>
        Directory.EnumerateFiles(directory, "*.ini", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsReparsePoint(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

    private static void CollectIniFiles(string root, string directory, List<string> files, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsExcluded(root, directory) || IsReparsePoint(directory))
            return;
        files.AddRange(EnumerateDirectIni(directory));
        foreach (string child in EnumerateSafeDirectories(directory))
            CollectIniFiles(root, child, files, token);
    }

    private static string? FindPreview(string directory)
    {
        foreach (string current in new[] { directory, Path.Combine(directory, "images"), Path.Combine(directory, "Textures") })
        {
            if (!Directory.Exists(current) || IsReparsePoint(current)) continue;
            foreach (string name in PreviewNames)
            {
                string file = Path.Combine(current, name);
                if (File.Exists(file) && !IsReparsePoint(file))
                    return file;
            }
        }
        return null;
    }

    private static void ReadTreeBindings(string root, string directory, List<IniBinding> bindings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory) || IsDisabled(root, directory) ||
            IsExcluded(root, directory) || IsReparsePoint(directory))
            return;
        foreach (string file in EnumerateDirectIni(directory))
            ReadBindingsIfEnabled(file, root, bindings, token);
        foreach (string child in EnumerateSafeDirectories(directory))
            ReadTreeBindings(root, child, bindings, token);
    }

    private static void ReadBindingsIfEnabled(string file, string root, List<IniBinding> bindings, CancellationToken token)
    {
        if (!File.Exists(file) || IsReparsePoint(file) || IsDisabled(root, file) ||
            IsExcluded(root, file) ||
            !Path.GetExtension(file).Equals(".ini", StringComparison.OrdinalIgnoreCase))
            return;

        string section = "";
        string condition = "";
        int index = 0;
        foreach (string raw in File.ReadLines(file, Encoding.UTF8))
        {
            token.ThrowIfCancellationRequested();
            index++;
            Match sectionMatch = SectionPattern.Match(raw);
            if (sectionMatch.Success)
            {
                section = sectionMatch.Groups[1].Value.Trim();
                condition = "";
                continue;
            }

            string uncommented = StripComment(raw).Trim();
            if (uncommented.Length == 0)
                continue;
            if (uncommented.StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
                uncommented.StartsWith("condition", StringComparison.OrdinalIgnoreCase))
                condition = raw.Trim();

            Match entry = EntryPattern.Match(uncommented);
            if (!entry.Success)
                continue;
            string key = entry.Groups[1].Value.Trim();
            if (!key.Equals("key", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("keyback", StringComparison.OrdinalIgnoreCase))
                continue;
            if (TryParseBinding(entry.Groups[2].Value, out IniBinding? binding) && binding is not null)
                bindings.Add(binding with
                {
                    SourceFile = file,
                    SourceLine = index,
                    Section = section,
                    Condition = condition,
                });
        }
    }

    private static bool TryParseBinding(string value, out IniBinding? binding)
    {
        string[] tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string? key = null;
        bool? ctrl = null;
        bool? shift = null;
        bool? alt = null;
        if (tokens.Length == 1 && tokens[0].ToLowerInvariant() is "shift" or "ctrl" or "alt")
        {
            string primary = tokens[0].ToLowerInvariant() switch
            {
                "ctrl" => "VK_CONTROL",
                "alt" => "VK_MENU",
                _ => "VK_SHIFT",
            };
            binding = new(primary, null, null, null, "", 0, "", "");
            return true;
        }
        foreach (string token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "no_ctrl": ctrl = false; break;
                case "shift": shift = true; break;
                case "no_shift": shift = false; break;
                case "alt": alt = true; break;
                case "no_alt": alt = false; break;
                case "no_modifiers":
                    ctrl = shift = alt = false;
                    break;
                default:
                    if (key is not null)
                    {
                        binding = null;
                        return false;
                    }
                    key = token;
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            binding = null;
            return false;
        }
        binding = new(key, ctrl, shift, alt, "", 0, "", "");
        return true;
    }

    private static bool BindingsOverlap(KeyBinding touch, IniBinding candidate) =>
        KeysEqual(touch.Key, candidate.Key) &&
        ModifierCanOverlap(touch.Ctrl, candidate.Ctrl) &&
        ModifierCanOverlap(touch.Shift, candidate.Shift) &&
        ModifierCanOverlap(touch.Key == "VK_MENU" || touch.Alt, candidate.Alt);

    private static bool IsShiftDragMenuOverlap(KeyBinding touch, IniBinding candidate) =>
        touch.Key.Equals("VK_LBUTTON", StringComparison.OrdinalIgnoreCase) &&
        touch.Shift && IsShiftKey(candidate.Key) &&
        ModifierCanOverlap(touch.Ctrl, candidate.Ctrl) &&
        ModifierCanOverlap(touch.Alt, candidate.Alt);

    private static bool ModifierCanOverlap(bool touch, bool? configured) =>
        configured is null || configured.Value == touch;

    private static bool KeysEqual(string left, string right) =>
        CanonicalKey(left).Equals(CanonicalKey(right), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalKey(string key)
    {
        string normalized = key.Trim();
        if (normalized.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];
        return normalized.ToUpperInvariant() switch
        {
            "ALT" => "MENU",
            var value => value,
        };
    }

    private static bool IsShiftKey(string key) =>
        key.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("VK_SHIFT", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("LSHIFT", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("VK_LSHIFT", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("RSHIFT", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("VK_RSHIFT", StringComparison.OrdinalIgnoreCase);

    private static string FormatBinding(IniBinding binding) =>
        string.Join(" ", new[]
        {
            binding.Ctrl switch { true => "ctrl", false => "no_ctrl", null => null },
            binding.Shift switch { true => "shift", false => "no_shift", null => null },
            binding.Alt switch { true => "alt", false => "no_alt", null => null },
            binding.Key,
        }.Where(value => value is not null));

    private static IEnumerable<(string Action, KeyBinding Binding)> TouchBindings(TouchSettings settings)
    {
        yield return ("Toggle", settings.Toggle);
        yield return ("Stop", settings.Stop);
        yield return ("Diagnostic", settings.Diagnostic);
        yield return ("Drag", settings.Drag);
    }

    private static string StripComment(string line)
    {
        int semicolon = line.IndexOf(';');
        int hash = line.IndexOf('#');
        int comment = semicolon < 0 ? hash : hash < 0 ? semicolon : Math.Min(semicolon, hash);
        return comment < 0 ? line : line[..comment];
    }

    private static bool HasAdapterMarker(string path, CancellationToken token)
    {
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                token.ThrowIfCancellationRequested();
                if (line.TrimStart().StartsWith(';') && AdapterMarker.IsMatch(line)) return true;
            }
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateSafeDirectories(string directory) =>
        Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !IsReparsePoint(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static bool IsDisabled(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase) ||
                         part.Equals(".git", StringComparison.OrdinalIgnoreCase));

    private static bool IsExcluded(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        foreach (string part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("EndfieldJiggleEFMI", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("EndfieldJiggleEFMI-", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("ConfiguratorBackups", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("DISABLED_EJTouchBackups", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("EJTouchBackups", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private sealed record IniBinding(
        string Key,
        bool? Ctrl,
        bool? Shift,
        bool? Alt,
        string SourceFile,
        int SourceLine,
        string Section,
        string Condition);
}
