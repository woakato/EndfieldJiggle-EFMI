using System.Security.Cryptography;
using System.Text.Json;

namespace EndfieldJiggle.Launcher.Core;

public sealed record LibraryFile(string Path, string Sha256);
public sealed record StandaloneManifest(int Schema, string Version, string EfmiVersion,
    string LibrariesVersion, LibraryFile[] Libraries);

public sealed class StandalonePackage
{
    public string Root { get; }
    public string EfmiDirectory => Path.Combine(Root, "EFMI");
    public string ModsDirectory => Path.Combine(EfmiDirectory, "Mods");
    public string ConfiguratorPath => Path.Combine(ModsDirectory, "EndfieldJiggleEFMI",
        "Configurator", "EndfieldJiggleConfigurator.exe");
    public string LoaderPath => Path.Combine(EfmiDirectory, "3dmloader.dll");
    public string D3dPath => Path.Combine(EfmiDirectory, "d3d11.dll");
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public StandalonePackage(string directory) => Root = Path.GetFullPath(directory);

    public StandaloneManifest Validate()
    {
        string manifestPath = Path.Combine(Root, "standalone-manifest.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 64 * 1024)
            throw new InvalidDataException("独立包清单缺失，请完整解压 standalone 压缩包。");
        StandaloneManifest manifest = JsonSerializer.Deserialize<StandaloneManifest>(
            File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("无法读取独立包清单。");
        string[] required = ["EFMI/3dmloader.dll", "EFMI/d3d11.dll", "EFMI/d3dcompiler_47.dll"];
        if (manifest.Schema != 1 || manifest.Libraries is null ||
            manifest.Libraries.Length != required.Length ||
            !manifest.Libraries.Select(file => file.Path).Order(StringComparer.Ordinal)
                .SequenceEqual(required.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("独立包的渲染库清单不完整。");
        foreach (LibraryFile file in manifest.Libraries)
        {
            string path = Path.Combine(Root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("缺少运行库，请重新解压完整包：" + file.Path);
            using FileStream stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("运行库校验失败，请重新解压完整包：" + file.Path);
        }
        foreach (string relative in new[] { "EFMI/d3dx.ini", "EFMI/Core/EFMI/main.ini",
                     "EFMI/Mods/EndfieldJiggleEFMI/EndfieldJiggle.ini",
                     "EFMI/Mods/EndfieldJiggleEFMI/Passes.ini",
                     "EFMI/Mods/EndfieldJiggleEFMI/Outfits.ini" })
        {
            if (!File.Exists(Path.Combine(Root, relative)))
                throw new FileNotFoundException("独立包缺少文件：" + relative);
        }
        ShaderCachePathGuard.Validate(EfmiDirectory);
        return manifest;
    }
}
