using System.Text.RegularExpressions;

namespace EndfieldJiggle.Configurator.Core;

public sealed record EfmiInputStatus(string? Reload, string? ModsDisplay, string? ShowOriginal);

public static class EfmiInputDiagnostics
{
    public static EfmiInputStatus Read(string efmiDirectory)
    {
        return new(ReadEntry(Path.Combine(efmiDirectory, "d3dx.ini"), "Hunting", "reload_config"),
            ReadEntry(Path.Combine(efmiDirectory, "Core", "EFMI", "KeyBindings.ini"), "KeyToggleMods", "key"),
            ReadEntry(Path.Combine(efmiDirectory, "d3dx.ini"), "Hunting", "show_original"));
    }

    private static string? ReadEntry(string path, string requestedSection, string requestedKey)
    {
        if (!File.Exists(path)) return null;
        string section = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Split(';', 2)[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1].Trim();
            if (!section.Equals(requestedSection, StringComparison.OrdinalIgnoreCase)) continue;
            Match entry = Regex.Match(line, @"^([^=]+?)\s*=\s*(.+)$");
            if (entry.Success && entry.Groups[1].Value.Trim().Equals(requestedKey, StringComparison.OrdinalIgnoreCase))
                return entry.Groups[2].Value.Trim();
        }
        return null;
    }
}
