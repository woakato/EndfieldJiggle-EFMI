using System.Text;
using System.Text.Json;

namespace EndfieldJiggle.Launcher.Core;

public sealed record LaunchSettings(int Schema, string GameExecutable);
public sealed record GameLocation(string Executable, string Directory);

public sealed class GameLocationStore(string packageDirectory)
{
    private string SettingsPath => Path.Combine(Path.GetFullPath(packageDirectory), "launch-settings.json");

    public string? Read()
    {
        if (!File.Exists(SettingsPath)) return null;
        if (new FileInfo(SettingsPath).Length > 64 * 1024)
            throw new InvalidDataException("游戏路径配置过大，请重新选择游戏。");
        LaunchSettings? settings = JsonSerializer.Deserialize<LaunchSettings>(
            File.ReadAllText(SettingsPath), StandalonePackage.JsonOptions);
        if (settings is null || settings.Schema != 1 || string.IsNullOrWhiteSpace(settings.GameExecutable))
            throw new InvalidDataException("游戏路径配置无效，请重新选择游戏。");
        return settings.GameExecutable;
    }

    public GameLocation Save(string selectedPath)
    {
        GameLocation game = Resolve(selectedPath);
        string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new LaunchSettings(1, game.Executable),
                StandalonePackage.JsonOptions), new UTF8Encoding(false));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return game;
    }

    public static GameLocation Resolve(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            throw new ArgumentException("请选择游戏的 Endfield.exe。");
        string path = Path.GetFullPath(selectedPath.Trim().Trim('"'));
        if (System.IO.Directory.Exists(path)) path = Path.Combine(path, "Endfield.exe");
        if (!Path.GetFileName(path).Equals("Endfield.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择游戏本体 Endfield.exe，不是 GRYPHLINK 或 XXMI 启动器。");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到游戏本体：" + path);
        string directory = Path.GetDirectoryName(path)!;
        if (!System.IO.Directory.Exists(Path.Combine(directory, "Endfield_Data")) ||
            !File.Exists(Path.Combine(directory, "GameAssembly.dll")))
            throw new InvalidDataException("此目录缺少 Endfield_Data 或 GameAssembly.dll，请选择完整游戏目录。");
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("游戏 EXE 不是有效的 Windows 程序。");
        stream.Position = 0x3c;
        int header = reader.ReadInt32();
        if (header < 64 || header > stream.Length - 6)
            throw new InvalidDataException("游戏 EXE 的文件头无效。");
        stream.Position = header;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664)
            throw new InvalidDataException("独立包需要 Windows x64 版 Endfield.exe。");
        return new(path, directory);
    }

    public static GameLocation? Discover(IEnumerable<string> roots)
    {
        string[] suffixes = ["", "EndField Game", "Endfield Game", "games/EndField Game",
            "Games/EndField Game", "EndField", "Endfield"];
        foreach (string root in roots.Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string suffix in suffixes)
            {
                try { return Resolve(Path.Combine(root, suffix)); }
                catch (Exception error) when (error is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException) { }
            }
        }
        return null;
    }
}
