namespace EndfieldJiggle.Configurator.Core;

public sealed record EfmiInputStatus(string? Reload, string? ModsDisplay, string? ShowOriginal,
    string? InputAtStartup, string? ToggleInput, string? LogLevel,
    IReadOnlyList<string> IncludeRecursive, IReadOnlyList<string> ExcludeRecursive);

public static class EfmiInputDiagnostics
{
    public static EfmiInputStatus Read(string efmiDirectory)
    {
        var global = ReadEntries(Path.Combine(efmiDirectory, "d3dx.ini"), [
            "Hunting/reload_config", "Hunting/show_original", "Input/input", "Input/toggle_input",
            "Logging/log_level", "Include/include_recursive", "Include/exclude_recursive"]);
        var bindings = ReadEntries(Path.Combine(efmiDirectory, "Core", "EFMI", "KeyBindings.ini"), ["KeyToggleMods/key"]);
        return new(global["Hunting/reload_config"].FirstOrDefault(), bindings["KeyToggleMods/key"].FirstOrDefault(),
            global["Hunting/show_original"].FirstOrDefault(), global["Input/input"].FirstOrDefault(),
            global["Input/toggle_input"].FirstOrDefault(), global["Logging/log_level"].FirstOrDefault(),
            global["Include/include_recursive"], global["Include/exclude_recursive"]);
    }

    private static Dictionary<string, List<string>> ReadEntries(string path, string[] requested)
    {
        var entries = requested.ToDictionary(key => key, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return entries;
        string section = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Split(';', 2)[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1].Trim();
            int split = line.IndexOf('=');
            if (split < 1 || !entries.TryGetValue(section + "/" + line[..split].Trim(), out var values)) continue;
            string value = line[(split + 1)..].Trim();
            if (value.Length > 0) values.Add(value);
        }
        return entries;
    }
}
