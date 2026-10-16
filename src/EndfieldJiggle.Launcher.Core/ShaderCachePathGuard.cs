namespace EndfieldJiggle.Launcher.Core;

public sealed record ShaderCachePathRisk(string Source, string Cache, int Length);

public static class ShaderCachePathGuard
{
    public const int MaximumPathLength = 259;

    public static string PredictCachePath(string source) =>
        Path.ChangeExtension(Path.GetFullPath(source), "cs_5_0.ffffffff.bin");

    public static ShaderCachePathRisk? FindLongest(string efmiDirectory)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };
        ShaderCachePathRisk? longest = null;
        foreach (string source in Directory.EnumerateFiles(efmiDirectory, "*.hlsl", options))
        {
            string cache = PredictCachePath(source);
            if (longest is null || cache.Length > longest.Length) longest = new(source, cache, cache.Length);
        }
        return longest;
    }

    public static void Validate(string efmiDirectory)
    {
        ShaderCachePathRisk? longest = FindLongest(efmiDirectory);
        if (longest is not null && longest.Length > MaximumPathLength)
        {
            // Upstream formats this name into a MAX_PATH buffer even when a cache already exists.
            throw new InvalidDataException($"安装路径过长：预留着色器缓存名后可达 {longest.Length} 个字符，" +
                $"当前渲染库最多允许 {MaximumPathLength} 个。请退出程序，将完整包重新解压到 D:\\EJ 等短目录，再打开；无需修改游戏目录。");
        }
    }
}
