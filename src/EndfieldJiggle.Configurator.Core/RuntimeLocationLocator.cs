using System.Text.Json;

namespace EndfieldJiggle.Configurator.Core;

public sealed record RuntimeLocation(string EfmiDirectory, string? RuntimeDirectory, bool Nested);

public static class RuntimeLocationLocator
{
    public static RuntimeLocation Resolve(string selectedDirectory, CancellationToken cancellationToken = default)
    {
        string selected = Path.GetFullPath(selectedDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        string efmi = InstallablePackage.ResolveEfmiDirectory(selected);
        string mods = Path.Combine(efmi, "Mods");
        List<string> runtimes = [];
        foreach (string directory in Directory.EnumerateDirectories(mods))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(directory);
            if (name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("EndfieldJiggle", StringComparison.OrdinalIgnoreCase))
                continue;
            FindRuntime(directory, 0, runtimes, cancellationToken);
        }
        if (runtimes.Count > 1)
            throw new InvalidDataException("检测到多份启用的触摸运行时，请先保留一份：" +
                string.Join("；", runtimes));
        string? runtime = runtimes.SingleOrDefault();
        return new(efmi, runtime, runtime is not null &&
            !Path.GetDirectoryName(runtime)!.Equals(mods, StringComparison.OrdinalIgnoreCase));
    }

    public static RuntimeLocation? Discover(string executableDirectory, string workingDirectory,
        string? rememberedDirectory = null, IEnumerable<string>? launcherDirectories = null,
        CancellationToken cancellationToken = default)
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
                RuntimeLocation location = Resolve(candidate, cancellationToken);
                if (location.RuntimeDirectory is not null) return location;
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
        for (int depth = 0; current is not null && depth < 8; depth++, current = Path.GetDirectoryName(current))
        {
            paths.Add(current);
            foreach (string relative in new[] { "EFMI", "游戏包/EFMI", "Packages/EFMI" })
                paths.Add(Path.Combine(current, relative));
            paths.AddRange(ReadLauncherCandidates(current));
        }
    }

    private static void FindRuntime(string directory, int depth, List<string> found, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        if (File.Exists(Path.Combine(directory, "EndfieldJiggle.ini")) &&
            File.Exists(Path.Combine(directory, "Passes.ini")))
        {
            _ = new RuntimeSettingsStore(directory).Read();
            found.Add(directory);
            return;
        }
        if (depth >= 3) return;
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith("EndfieldJiggle", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Mods", StringComparison.OrdinalIgnoreCase))
                FindRuntime(child, depth + 1, found, token);
        }
    }
}
