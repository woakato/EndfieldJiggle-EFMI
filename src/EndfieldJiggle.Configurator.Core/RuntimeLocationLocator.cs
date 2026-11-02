using System.Text.Json;

namespace EndfieldJiggle.Configurator.Core;

public sealed record RuntimeLocation(string EfmiDirectory, string? RuntimeDirectory, bool Nested)
{
    public string? Problem { get; init; }
}

public sealed record RuntimeCandidate(string Directory, string[] ActiveInis, string? Problem);
public sealed record RuntimeLayout(string EfmiDirectory, IReadOnlyList<RuntimeCandidate> Candidates);

public static class RuntimeLocationLocator
{
    public static RuntimeLocation Resolve(string selectedDirectory, CancellationToken cancellationToken = default,
        bool allowProblems = false)
    {
        RuntimeLayout layout = Inspect(selectedDirectory, cancellationToken);
        string efmi = layout.EfmiDirectory;
        string mods = Path.Combine(efmi, "Mods");
        string? problem = layout.Candidates.Count > 1
            ? "检测到多份仍会加载的触摸 INI：" + string.Join("；",
                layout.Candidates.Select(item => item.Directory +
                    (item.Problem is null ? "" : "（" + item.Problem + "）")))
            : layout.Candidates.SingleOrDefault()?.Problem;
        if (problem is not null && !allowProblems) throw new InvalidDataException(problem);
        string? runtime = problem is null ? layout.Candidates.SingleOrDefault()?.Directory : null;
        return new(efmi, runtime, runtime is not null &&
            !Path.GetDirectoryName(runtime)!.Equals(mods, StringComparison.OrdinalIgnoreCase)) { Problem = problem };
    }

    public static RuntimeLayout Inspect(string selectedDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string efmi = InstallablePackage.ResolveEfmiDirectory(Path.GetFullPath(selectedDirectory));
        List<RuntimeCandidate> found = [];
        FindRuntimes(Path.Combine(efmi, "Mods"), found, cancellationToken);
        return new(efmi, found.OrderBy(item => item.Directory, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static RuntimeLocation? Discover(string executableDirectory, string workingDirectory,
        string? rememberedDirectory = null, IEnumerable<string>? launcherDirectories = null,
        CancellationToken cancellationToken = default, bool allowProblems = false)
    {
        List<string> candidates = [];
        // Adjacent paths have priority over an installation remembered on another machine.
        foreach (string path in new[] { executableDirectory, workingDirectory })
            AddAncestors(path, candidates);
        if (!string.IsNullOrWhiteSpace(rememberedDirectory)) candidates.Add(rememberedDirectory);
        foreach (string launcher in launcherDirectories ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.AddRange(ReadLauncherCandidates(launcher));
        }
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RuntimeLocation location = Resolve(candidate, cancellationToken, allowProblems);
                if (location.RuntimeDirectory is not null || location.Problem is not null) return location;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }

    public static IEnumerable<string> ReadLauncherCandidates(string launcherDirectory)
    {
        string config = Path.Combine(launcherDirectory, "XXMI Launcher Config.json");
        if (!File.Exists(config) || new FileInfo(config).Length > 2 * 1024 * 1024) return [];
        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(config));
            if (json.RootElement.TryGetProperty("Importers", out JsonElement importers) &&
                importers.TryGetProperty("EFMI", out JsonElement efmi))
            {
                List<string> paths = [];
                CollectPaths(efmi, launcherDirectory, paths);
                return paths;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        return [];
    }

    private static void CollectPaths(JsonElement element, string launcher, List<string> paths)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name is "importer_folder" or "importer_path" && property.Value.ValueKind == JsonValueKind.String)
            {
                string? value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    paths.Add(Path.GetFullPath(Path.Combine(launcher, value)));
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
                CollectPaths(property.Value, launcher, paths);
        }
    }

    private static void AddAncestors(string directory, List<string> paths)
    {
        string? current = Path.GetFullPath(directory);
        for (; current is not null; current = Path.GetDirectoryName(current))
        {
            paths.Add(current);
            foreach (string relative in new[] { "EFMI", "游戏包/EFMI", "Packages/EFMI" })
                paths.Add(Path.Combine(current, relative));
            paths.AddRange(ReadLauncherCandidates(current));
        }
    }

    internal static bool HasRuntimeFiles(string directory) =>
        File.Exists(Path.Combine(directory, "EndfieldJiggle.ini")) &&
        File.Exists(Path.Combine(directory, "Passes.ini"));

    internal static readonly string[] RuntimeIniNames =
        ["EndfieldJiggle.ini", "Passes.ini", "Outfits.ini", "OutfitPreparation.ini"];

    private static void FindRuntimes(string mods, List<RuntimeCandidate> found, CancellationToken token)
    {
        Stack<string> pending = new();
        pending.Push(mods);
        while (pending.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            string[] active = RuntimeIniNames.Select(name => Path.Combine(directory, name))
                .Where(File.Exists).Where(path =>
                {
                    InstallablePackage.EnsureUnlinked(path);
                    string expected = Path.GetFileName(path) == "OutfitPreparation.ini"
                        ? "AEndfieldJiggleEFMI" : "EndfieldJiggleEFMI";
                    foreach (string raw in File.ReadLines(path))
                    {
                        string line = raw.Split(';', 2)[0].Trim();
                        if (line.StartsWith('[')) break;
                        string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && parts[0].Equals("namespace", StringComparison.OrdinalIgnoreCase))
                            return parts[1].Equals(expected, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                }).ToArray();
            if (active.Length != 0)
            {
                string? problem = null;
                try
                {
                    _ = new RuntimeSettingsStore(directory).Read();
                    foreach (string name in new[] { "step_cs.hlsl", "character_pick_ps.hlsl",
                        "pick_1479b2b594b9c91a.hlsl", "pick_c6e55aaa8f4b3218.hlsl",
                        "input_controller.hlsl", "motion_model.hlsl" })
                    {
                        string path = Path.Combine(directory, "shaders", name);
                        InstallablePackage.EnsureUnlinked(path);
                        if (!File.Exists(path)) throw new FileNotFoundException("缺少 shaders/" + name);
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    problem = error.Message;
                }
                found.Add(new(directory, active, problem));
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(child);
                if (!name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    pending.Push(child);
            }
        }
    }
}
