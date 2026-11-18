using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndfieldJiggle.Configurator.Core;

public sealed record XxmiProfile(string ConfigPath, string? ActiveImporter, bool? EfmiListed,
    string? EfmiDirectory, bool? MatchesSelectedDirectory, string? GameFolder,
    string? EfmiDeployedVersion, bool? MigotoInput, string? ConfigError, XxmiLog Log);
public sealed record XxmiLog(string Path, DateTime? ModifiedUtc, bool Truncated,
    IReadOnlyList<string> ErrorTypes, string? Error);
public sealed record XxmiStatus(int LauncherProcesses, IReadOnlyList<XxmiProfile> Profiles, string Summary);

public static class XxmiDiagnostics
{
    public const string ConfigName = "XXMI Launcher Config.json";

    public static XxmiStatus Read(string? selectedEfmi, string? explicitConfig, CancellationToken token)
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        Process[] processes = Process.GetProcessesByName("XXMI Launcher");
        foreach (Process process in processes)
        {
            using (process)
            {
                if (explicitConfig is not null) continue;
                try
                {
                    if (process.MainModule?.FileName is string exe)
                        roots.Add(Path.GetDirectoryName(exe)!);
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
            }
        }
        if (explicitConfig is not null)
            return Inspect(selectedEfmi, [explicitConfig], processes.Length, token);

        for (var dir = selectedEfmi is null ? null : new DirectoryInfo(selectedEfmi); dir is not null; dir = dir.Parent)
            roots.Add(dir.FullName);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        roots.Add(Path.Combine(local, "XXMI Launcher"));
        roots.Add(Path.Combine(local, "XXMI"));
        return Inspect(selectedEfmi, roots.Select(dir => Path.Combine(dir, ConfigName)).Where(File.Exists),
            processes.Length, token);
    }

    internal static XxmiStatus Inspect(string? selectedEfmi, IEnumerable<string> configs,
        int processes, CancellationToken token)
    {
        List<XxmiProfile> profiles = [];
        foreach (string path in configs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            profiles.Add(ReadProfile(path, selectedEfmi));
        }
        string summary = profiles.Count switch
        {
            0 => "未找到启动器配置。使用 XXMI 时可手动选择配置文件。",
            > 1 => $"发现 {profiles.Count} 份启动器配置，请选择本次启动使用的那份。",
            _ => Describe(profiles[0]),
        };
        return new(processes, profiles, summary);
    }

    private static string Describe(XxmiProfile profile)
    {
        if (profile.ConfigError is not null) return "启动器配置无法读取，详见报告。";
        List<string> items = [];
        if (profile.MatchesSelectedDirectory == false)
            items.Add("这份启动器配置指向另一套 EFMI，请核对两处路径");
        if (profile.ActiveImporter is not null && !profile.ActiveImporter.Equals("EFMI", StringComparison.OrdinalIgnoreCase))
            items.Add("启动器当前选择 " + profile.ActiveImporter);
        if (profile.EfmiListed == false) items.Add("启动器启用列表中没有 EFMI");
        if (profile.MigotoInput == false) items.Add("启动器 EFMI 配置的 Migoto.input 已关闭");
        if (profile.MatchesSelectedDirectory is null) items.Add("缺少可比较的 EFMI 路径");
        return items.Count == 0 ? "启动器配置路径与所选 EFMI 一致。" : string.Join("；", items) + "。";
    }

    private static XxmiProfile ReadProfile(string path, string? selectedEfmi)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        XxmiLog log = ReadLog(Path.Combine(root, "XXMI Launcher Log.txt"));
        try
        {
            InstallablePackage.EnsureUnlinked(path);
            using FileStream file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 2 * 1024 * 1024) throw new InvalidDataException("启动器配置超过 2 MiB。");
            byte[] bytes = new byte[checked((int)file.Length)];
            file.ReadExactly(bytes);
            ReadOnlyMemory<byte> json = bytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) ? bytes.AsMemory(3) : bytes;
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement config = document.RootElement;
            if (config.ValueKind != JsonValueKind.Object) throw new InvalidDataException("启动器配置格式不正确。");
            JsonElement launcher = Field(config, "Launcher");
            JsonElement enabled = Field(launcher, "enabled_importers");
            bool? listed = enabled.ValueKind == JsonValueKind.Array &&
                enabled.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String)
                ? enabled.EnumerateArray().Any(v => "EFMI".Equals(v.GetString(), StringComparison.OrdinalIgnoreCase)) : null;
            JsonElement efmi = Field(Field(config, "Importers"), "EFMI");
            JsonElement importer = Field(efmi, "Importer");
            string? folder = Text(importer, "importer_folder") ?? Text(importer, "importer_path");
            string? resolved = folder is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder, root));
            bool? matches = selectedEfmi is null || resolved is null ? null :
                resolved.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedEfmi)), StringComparison.OrdinalIgnoreCase);
            JsonElement input = Field(Field(efmi, "Migoto"), "input");
            return new(path, Text(launcher, "active_importer"), listed, resolved, matches,
                Text(importer, "game_folder"), Text(Field(Field(Field(config, "Packages"), "packages"), "EFMI"), "deployed_version"),
                input.ValueKind is JsonValueKind.True or JsonValueKind.False ? input.GetBoolean() : null, null, log);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            return new(path, null, null, null, null, null, null, null, error.Message, log);
        }
    }

    private static XxmiLog ReadLog(string path)
    {
        DiagnosticLog log = DiagnosticSnapshotCollector.ReadLog(path);
        // Launcher logs can contain command lines and account details. Export only known error types.
        string[] errors = new[] { "PermissionError", "FileNotFoundError", "Access is denied", "WinError 5",
            "WinError 126", "WinError 193", "Traceback (most recent call last)" }
            .Where(text => log.Tail is { } tail && Regex.IsMatch(tail, Regex.Escape(text) + @"(?![\w])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        return new(path, log.ModifiedUtc, log.Truncated, errors, log.Error ?? (log.Present ? null : "未找到启动器日志。"));
    }

    private static JsonElement Field(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value) ? value : default;

    private static string? Text(JsonElement obj, string name) =>
        Field(obj, name) is { ValueKind: JsonValueKind.String } value &&
            value.GetString() is string text && !string.IsNullOrWhiteSpace(text) ? text : null;
}
