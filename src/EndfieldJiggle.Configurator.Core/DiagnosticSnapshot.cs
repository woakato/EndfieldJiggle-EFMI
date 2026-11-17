using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndfieldJiggle.Configurator.Core;

public sealed record DiagnosticSection<T>(T? Value, string? Error) where T : class;
public sealed record DiagnosticFile(string Path, bool Present, long? Bytes, DateTime? ModifiedUtc,
    string? Sha256, string? ReceiptSha256, bool? MatchesReceipt, string? Error);
public sealed record DiagnosticLog(string Path, bool Present, long? Bytes, bool Truncated,
    string? Tail, string? Error);
public sealed record ReceiptDiagnostic(bool Present, string? Version, IReadOnlyDictionary<string, string> Files);
public sealed record InputGuardSession(int GamePid, bool MappingPresent, int? Protocol, int? Status,
    bool? Enabled, int? CompanionPid, long? HeartbeatAgeMs, bool Ready, string? Error);
public sealed record InputGuardDiagnostic(IReadOnlyList<int> CompanionPids,
    IReadOnlyList<InputGuardSession> Games);
public sealed record OutfitDiagnostic(bool AlreadyAdapted, int Supported, int Unsupported, int Auxiliary,
    IReadOnlyList<string> DisabledInis, IReadOnlyList<string> MissingRequired,
    IReadOnlyList<string> MissingTextures, IReadOnlyList<string> MissingUnreferenced,
    IReadOnlyDictionary<string, int> UnsupportedReasons);
public sealed record OptionalQaqmDiagnostic(bool? Applicable, QaqmRecoveryPlan? Plan, string? Note);
public sealed record DiagnosticSnapshot(DateTimeOffset CapturedAtUtc,
    DiagnosticSection<RuntimeLayout> Layout,
    DiagnosticSection<EfmiInputStatus> EfmiInput,
    DiagnosticSection<TouchSettings> SavedSettings,
    DiagnosticSection<ReceiptDiagnostic> Receipt,
    DiagnosticSection<IReadOnlyList<DiagnosticFile>> Files,
    DiagnosticSection<InputGuardDiagnostic> InputGuard,
    IReadOnlyList<DiagnosticLog> Logs,
    DiagnosticSection<OutfitDiagnostic> Outfit,
    DiagnosticSection<OptionalQaqmDiagnostic> Qaqm);

public static class DiagnosticSnapshotCollector
{
    private const int MaxLogBytes = 32 * 1024;
    private static readonly string[] EfmiFiles =
        ["d3dx.ini", "d3d11.dll", "3dmloader.dll", "Core/EFMI/KeyBindings.ini"];
    private static readonly string[] RuntimeFiles =
    [
        "EndfieldJiggle.ini", "Passes.ini", "Outfits.ini", "OutfitPreparation.ini",
        "shaders/step_cs.hlsl", "shaders/character_pick_ps.hlsl",
        "shaders/pick_1479b2b594b9c91a.hlsl", "shaders/pick_c6e55aaa8f4b3218.hlsl",
        "shaders/input_controller.hlsl", "shaders/motion_model.hlsl",
        "InputGuard/EndfieldJiggleInputGuard.exe", "InputGuard/EndfieldJiggleInputGuard.dll",
    ];

    public static DiagnosticSnapshot Capture(string? efmi, string? runtime, string? outfit,
        string configuratorLog, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var layout = Section(efmi, () => RuntimeLocationLocator.Inspect(efmi!, token));
        var input = Section(efmi, () => EfmiInputDiagnostics.Read(efmi!));
        var settings = Section(runtime, () => new RuntimeSettingsStore(runtime!).Read().Settings);
        var receipt = Section(runtime, () => ReadReceipt(runtime!));
        var files = Section(efmi, () => ReadFiles(efmi!, runtime, receipt.Value, token));
        var guard = TryRead(ReadInputGuard);
        DiagnosticLog[] logs =
        [
            ReadLog(configuratorLog),
            efmi is null ? new DiagnosticLog("", false, null, false, null, "未选择 EFMI。") :
                ReadLog(Path.Combine(efmi, "d3d11_log.txt")),
        ];
        var inspectedOutfit = outfit is null
            ? new DiagnosticSection<OutfitDiagnostic>(null, "未选择换装。")
            : Section(runtime, () => ReadOutfit(outfit, runtime!));
        var qaqm = outfit is null
            ? new DiagnosticSection<OptionalQaqmDiagnostic>(null, "未选择换装。")
            : Section(runtime, () => ReadQaqm(outfit, runtime!, token));
        return new(DateTimeOffset.UtcNow, layout, input, settings, receipt, files, guard,
            logs, inspectedOutfit, qaqm);
    }

    private static DiagnosticSection<T> Section<T>(string? directory, Func<T> read) where T : class =>
        string.IsNullOrWhiteSpace(directory)
            ? new(null, "未选择目录。")
            : TryRead(read);

    private static DiagnosticSection<T> TryRead<T>(Func<T> read) where T : class
    {
        try { return new(read(), null); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return new(null, error.GetType().Name + ": " + error.Message);
        }
    }

    private static ReceiptDiagnostic ReadReceipt(string runtime)
    {
        string path = Path.Combine(runtime, "EndfieldJiggle.install.json");
        InstallablePackage.EnsureUnlinked(path);
        if (!File.Exists(path)) return new(false, null, new Dictionary<string, string>());
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("安装记录过大。");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("Schema", out JsonElement schema) || schema.GetInt32() != 1 ||
            !root.TryGetProperty("Files", out JsonElement files) ||
            files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > 128)
            throw new InvalidDataException("安装记录格式不支持。");
        Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement file in files.EnumerateArray())
        {
            string name = file.GetProperty("Name").GetString() ?? "";
            string hash = file.GetProperty("Sha256").GetString() ?? "";
            _ = InstallablePackage.Inside(runtime, name);
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !hashes.TryAdd(name, hash))
                throw new InvalidDataException("安装记录含无效或重复的文件。");
        }
        string? version = root.TryGetProperty("Version", out JsonElement value) ? value.GetString() : null;
        return new(true, version, hashes);
    }

    private static IReadOnlyList<DiagnosticFile> ReadFiles(string efmi, string? runtime,
        ReceiptDiagnostic? receipt, CancellationToken token)
    {
        List<DiagnosticFile> result = [];
        foreach (string name in EfmiFiles)
        {
            token.ThrowIfCancellationRequested();
            result.Add(ReadFile(efmi, name, null));
        }
        if (string.IsNullOrWhiteSpace(runtime)) return result;
        HashSet<string> names = new(RuntimeFiles, StringComparer.OrdinalIgnoreCase);
        if (receipt is { Present: true }) names.UnionWith(receipt.Files.Keys);
        string shaderDirectory = Path.Combine(runtime, "shaders");
        InstallablePackage.EnsureUnlinked(shaderDirectory);
        if (Directory.Exists(shaderDirectory))
        {
            string[] shaders = Directory.GetFiles(shaderDirectory, "*", SearchOption.TopDirectoryOnly);
            if (shaders.Length > 128) throw new InvalidDataException("运行时 shaders 目录文件过多。");
            foreach (string shader in shaders.Where(path => Path.GetExtension(path) is ".hlsl" or ".bin" or ".shdr"))
                names.Add("shaders/" + Path.GetFileName(shader));
        }
        foreach (string name in names.Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            string? expected = receipt is not null && receipt.Files.TryGetValue(name, out string? hash)
                ? hash : null;
            result.Add(ReadFile(runtime, name, expected));
        }
        return result;
    }

    private static DiagnosticFile ReadFile(string root, string name, string? expected)
    {
        string path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            path = InstallablePackage.Inside(root, name);
            InstallablePackage.EnsureUnlinked(path);
            if (!File.Exists(path))
                return new(path, false, null, null, null, expected, expected is null ? null : false, null);
            FileInfo file = new(path);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            return new(path, true, file.Length, file.LastWriteTimeUtc, hash, expected,
                expected is null ? null : hash.Equals(expected, StringComparison.OrdinalIgnoreCase), null);
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return new(path, false, null, null, null, expected, null,
                error.GetType().Name + ": " + error.Message);
        }
    }

    private static InputGuardDiagnostic ReadInputGuard()
    {
        using var companions = new ProcessGroup("EndfieldJiggleInputGuard");
        using var games = new ProcessGroup("Endfield");
        int[] companionPids = companions.Ids;
        return new(companionPids, games.Ids.Select(ReadSession).ToArray());
    }

    internal static InputGuardSession ReadSession(int pid)
    {
        if (!OperatingSystem.IsWindows())
            return new(pid, false, null, null, null, null, null, false, "仅支持 Windows 共享会话。");
        try
        {
            using MemoryMappedFile mapping = MemoryMappedFile.OpenExisting(
                "Local\\EndfieldJiggle.InputGuard." + pid, MemoryMappedFileRights.Read);
            using MemoryMappedViewAccessor view = mapping.CreateViewAccessor(0, 24, MemoryMappedFileAccess.Read);
            int protocol = view.ReadInt32(0);
            int enabled = view.ReadInt32(4);
            int status = view.ReadInt32(8);
            int owner = view.ReadInt32(12);
            long heartbeat = view.ReadInt64(16);
            long now = Environment.TickCount64;
            long? age = heartbeat >= 0 && heartbeat <= now ? now - heartbeat : null;
            return new(pid, true, protocol, status, enabled == 1, owner, age,
                protocol == 1 && status == 1 && enabled == 1 && age is >= 0 and < 1500, null);
        }
        catch (FileNotFoundException)
        {
            return new(pid, false, null, null, null, null, null, false, null);
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return new(pid, false, null, null, null, null, null, false,
                error.GetType().Name + ": " + error.Message);
        }
    }

    private sealed class ProcessGroup : IDisposable
    {
        private readonly Process[] processes;
        public int[] Ids { get; }
        public ProcessGroup(string name)
        {
            processes = Process.GetProcessesByName(name);
            Ids = processes.Select(process => process.Id).Order().ToArray();
        }
        public void Dispose()
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private static DiagnosticLog ReadLog(string path)
    {
        try
        {
            InstallablePackage.EnsureUnlinked(path);
            if (!File.Exists(path)) return new(path, false, null, false, null, null);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            bool truncated = length > MaxLogBytes;
            if (truncated) stream.Seek(-MaxLogBytes, SeekOrigin.End);
            byte[] bytes = new byte[(int)Math.Min(length, MaxLogBytes)];
            stream.ReadExactly(bytes);
            int start = truncated ? Array.IndexOf(bytes, (byte)'\n') + 1 : 0;
            return new(path, true, length, truncated,
                Encoding.UTF8.GetString(bytes.AsSpan(start)), null);
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            return new(path, false, null, false, null, error.GetType().Name + ": " + error.Message);
        }
    }

    private static OutfitDiagnostic ReadOutfit(string outfit, string runtime)
    {
        OutfitAdapterInspection inspection = new OutfitAdapterStore(outfit, runtime).Inspect();
        return new(inspection.AlreadyAdapted, inspection.SupportedDraws.Count,
            inspection.UnsupportedDraws.Count, inspection.AuxiliaryDraws.Count,
            inspection.DisabledIniFiles, inspection.MissingRequiredResources,
            inspection.MissingTextureResources, inspection.MissingUnreferencedResources,
            inspection.UnsupportedDraws.GroupBy(draw => draw.Reason)
                .ToDictionary(group => group.Key, group => group.Count()));
    }

    private static OptionalQaqmDiagnostic ReadQaqm(string outfit, string runtime, CancellationToken token)
    {
        OutfitAdapterStore selected = new(outfit, runtime);
        if (!HasQaqmReferences(outfit, token))
            return new(false, null, "未发现 QAQM 状态依赖；其他换装仍由通用资源检查覆盖。");
        if (!selected.IsInEfmiModsTree)
            return new(null, null, "外部换装源目录无法证明当前 EFMI 的加载状态。");
        QaqmRecoveryPlan plan =
            new QaqmRecoveryStore(outfit, InstallablePackage.ResolveEfmiDirectory(runtime)).Inspect();
        return new(true, plan, null);
    }

    private static bool HasQaqmReferences(string outfit, CancellationToken token)
    {
        Stack<string> directories = new();
        directories.Push(outfit);
        int count = 0;
        while (directories.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            InstallablePackage.EnsureUnlinked(directory);
            if (++count > 2048) throw new InvalidDataException("换装目录过大，无法判定 QAQM 依赖。");
            foreach (string path in Directory.EnumerateFiles(directory, "*.ini"))
            {
                token.ThrowIfCancellationRequested();
                InstallablePackage.EnsureUnlinked(path);
                if (++count > 2048 || new FileInfo(path).Length > 4 * 1024 * 1024)
                    throw new InvalidDataException("换装 INI 过多或过大，无法判定 QAQM 依赖。");
                if (File.ReadLines(path).Any(line =>
                    line.Contains("\\QAQM\\Persist\\Bridge_", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("qaqm_state_", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (!name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    directories.Push(child);
            }
        }
        return false;
    }
}
