using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndfieldJiggle.Configurator.Core;

public sealed record InstallationPreview(string EfmiDirectory, string RuntimeDirectory,
    bool IsUpgrade, string Fingerprint, int FileCount);
public sealed record InstallationResult(string RuntimeDirectory, string BackupDirectory, bool SettingsPreserved);

public sealed class InstallablePackage
{
    private const string ReceiptName = "EndfieldJiggle.install.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, byte[]> files;
    public bool Available => files.Count != 0;

    private InstallablePackage(Dictionary<string, byte[]> files) => this.files = files;

    public static InstallablePackage FromAssembly(Assembly assembly, string executablePath)
    {
        using Stream? payload = assembly.GetManifestResourceStream("EndfieldJiggle.InstallableRuntime.zip");
        if (payload is null)
            return new(new(StringComparer.OrdinalIgnoreCase));
        using MemoryStream zip = new();
        payload.CopyTo(zip);
        Dictionary<string, byte[]> files = ReadRuntimeArchive(zip.ToArray());
        EnsureUnlinked(executablePath);
        files.Add("Configurator/EndfieldJiggleConfigurator.exe", File.ReadAllBytes(executablePath));
        foreach (string name in new[] { "DOTNET-LICENSE.txt", "DOTNET-THIRD-PARTY-NOTICES.txt",
                     "LICENSE", "INSTALL-zh-CN.md", "DISCLAIMER.md" })
        {
            using Stream resource = assembly.GetManifestResourceStream("EndfieldJiggle.Notices." + name)
                ?? throw new InvalidDataException("安装程序缺少许可或安装说明：" + name);
            using MemoryStream content = new();
            resource.CopyTo(content);
            files.Add("Configurator/" + name, content.ToArray());
        }
        return new(files);
    }

    public static InstallablePackage FromRuntimeArchive(byte[] archive) => new(ReadRuntimeArchive(archive));

    public InstallationPreview Preview(string selectedDirectory)
    {
        if (!Available)
            throw new InvalidOperationException("此配置器没有内置运行时，请使用完整安装包。");
        string efmi = ResolveEfmiDirectory(selectedDirectory);
        EnsureUnlinked(efmi);
        string runtime = Path.Combine(efmi, "Mods", "EndfieldJiggleEFMI");
        EnsureUnlinked(runtime);
        bool upgrade = Directory.Exists(runtime);
        if (upgrade)
        {
            _ = new RuntimeSettingsStore(runtime).Read();
            EnsureKnownActiveInis(runtime);
            ValidateReceipt(runtime);
            foreach ((string name, byte[] bytes) in files.Where(pair =>
                         !pair.Key.EndsWith(".ini", StringComparison.Ordinal) &&
                         !pair.Key.StartsWith("Configurator/", StringComparison.Ordinal)))
            {
                string owned = Inside(runtime, name);
                EnsureUnlinked(owned);
                if (File.Exists(owned) && !File.ReadAllBytes(owned).AsSpan().SequenceEqual(bytes))
                    throw new IOException("旧运行时资源与已审核版本不同，拒绝覆盖：" + name);
            }
        }
        return new(efmi, runtime, upgrade, Fingerprint(runtime), files.Count);
    }

    public InstallationResult Install(InstallationPreview preview, bool requireStopped = true)
    {
        if (requireStopped) EnsureStopped();
        InstallationPreview current = Preview(preview.EfmiDirectory);
        if (current != preview)
            throw new IOException("安装目录自预览后已变化，请重新选择目录。");
        TouchSettings? previousSettings = current.IsUpgrade
            ? new RuntimeSettingsStore(current.RuntimeDirectory).Read().Settings : null;
        string backupRoot = Path.Combine(current.EfmiDirectory, "EndfieldJiggleBackups");
        EnsureUnlinked(backupRoot);
        string session = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(session);
        EnsureUnlinked(session);
        string[] names = files.Keys.Append(ReceiptName).ToArray();
        List<OriginalFile> originals = [];
        foreach (string name in names)
        {
            string target = Inside(current.RuntimeDirectory, name);
            EnsureUnlinked(target);
            byte[]? bytes = File.Exists(target) ? File.ReadAllBytes(target) : null;
            DateTime time = bytes is null ? default : File.GetLastWriteTimeUtc(target);
            originals.Add(new(name, bytes, time));
            if (bytes is not null)
            {
                string backup = Inside(session, name + ".bak");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.WriteAllBytes(backup, bytes);
                File.SetLastWriteTimeUtc(backup, time);
            }
        }
        File.WriteAllText(Path.Combine(session, "transaction.json"), JsonSerializer.Serialize(new
        {
            schema = 1, status = "prepared", runtimeDirectory = current.RuntimeDirectory,
            files = originals.Select(file => new
            {
                file.Name, existed = file.Bytes is not null,
                sha256 = file.Bytes is null ? null : Hash(file.Bytes), lastWriteTimeUtc = file.Time,
            }),
        }, JsonOptions), new UTF8Encoding(false));
        if (Fingerprint(current.RuntimeDirectory) != preview.Fingerprint)
            throw new IOException("安装目录在备份期间变化，未开始安装。");

        Dictionary<string, string> written = new(StringComparer.OrdinalIgnoreCase);
        bool createdRuntime = !Directory.Exists(current.RuntimeDirectory);
        try
        {
            Directory.CreateDirectory(current.RuntimeDirectory);
            foreach ((string name, byte[] bytes) in files)
            {
                if (requireStopped) EnsureStopped();
                string path = Inside(current.RuntimeDirectory, name);
                EnsureUnlinked(path);
                OriginalFile original = originals.Single(item => item.Name == name);
                VerifyOriginal(path, original);
                if (original.Bytes is not null && original.Bytes.AsSpan().SequenceEqual(bytes)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                EnsureUnlinked(path);
                AtomicWrite(path, bytes);
                written.Add(name, Hash(bytes));
            }
            RuntimeSettingsStore store = new(current.RuntimeDirectory);
            RuntimeSettingsSnapshot snapshot = store.Read();
            if (previousSettings is not null && snapshot.Settings != previousSettings)
            {
                store.Apply(previousSettings, snapshot.Fingerprint, requireStopped);
                foreach (string name in files.Keys.Where(name => name.EndsWith(".ini", StringComparison.Ordinal)))
                    written[name] = Hash(File.ReadAllBytes(Inside(current.RuntimeDirectory, name)));
            }
            InstallationReceipt receipt = new(1, "v0.2.1", current.RuntimeDirectory, session,
                files.Keys.Select(name => new InstalledFile(name,
                    Hash(File.ReadAllBytes(Inside(current.RuntimeDirectory, name))))).ToArray());
            byte[] receiptBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt, JsonOptions));
            AtomicWrite(Inside(current.RuntimeDirectory, ReceiptName), receiptBytes);
            written[ReceiptName] = Hash(receiptBytes);
            File.WriteAllText(Path.Combine(session, "result.json"), JsonSerializer.Serialize(new
            {
                schema = 1, status = "installed", runtimeDirectory = current.RuntimeDirectory,
                settingsPreserved = previousSettings is not null, inGameVerified = false,
            }, JsonOptions), new UTF8Encoding(false));
            return new(current.RuntimeDirectory, session, previousSettings is not null);
        }
        catch (Exception error)
        {
            List<string> conflicts = [];
            foreach (OriginalFile original in originals.AsEnumerable().Reverse())
            {
                if (!written.TryGetValue(original.Name, out string? appliedHash)) continue;
                string path = Inside(current.RuntimeDirectory, original.Name);
                EnsureUnlinked(path);
                if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != appliedHash)
                {
                    conflicts.Add(original.Name);
                    continue;
                }
                if (original.Bytes is null) File.Delete(path);
                else
                {
                    AtomicWrite(path, original.Bytes);
                    File.SetLastWriteTimeUtc(path, original.Time);
                }
            }
            if (createdRuntime && Directory.Exists(current.RuntimeDirectory))
                RemoveEmptyDirectories(current.RuntimeDirectory);
            throw new IOException(conflicts.Count == 0
                ? $"安装失败，已恢复本次写入。备份：{session}"
                : $"安装失败，文件另被修改，保留现场未覆盖：{string.Join("、", conflicts)}。备份：{session}", error);
        }
    }

    public static string ResolveEfmiDirectory(string selectedDirectory)
    {
        string current = Path.GetFullPath(selectedDirectory);
        for (int depth = 0; depth < 5; depth++)
        {
            EnsureUnlinked(current);
            if (File.Exists(Path.Combine(current, "d3dx.ini")) &&
                File.Exists(Path.Combine(current, "d3d11.dll")) &&
                Directory.Exists(Path.Combine(current, "Mods")))
                return current;
            current = Directory.GetParent(current)?.FullName
                ?? throw new DirectoryNotFoundException("请选择含 d3dx.ini、d3d11.dll 和 Mods 的 EFMI 目录。");
        }
        throw new DirectoryNotFoundException("所选目录不是 EFMI 安装目录。");
    }

    public static bool HasRestorableInstallation(string runtime)
    {
        EnsureUnlinked(runtime);
        string path = Inside(runtime, ReceiptName);
        EnsureUnlinked(path);
        return File.Exists(path);
    }

    public static string RestoreInstallation(string runtime, bool requireStopped = true)
    {
        if (requireStopped) EnsureStopped();
        string efmi = ResolveEfmiDirectory(runtime);
        runtime = Path.Combine(efmi, "Mods", "EndfieldJiggleEFMI");
        EnsureUnlinked(runtime);
        EnsureKnownActiveInis(runtime);
        string receiptPath = Inside(runtime, ReceiptName);
        EnsureUnlinked(receiptPath);
        InstallationReceipt receipt = JsonSerializer.Deserialize<InstallationReceipt>(
            File.ReadAllText(receiptPath), JsonOptions) ?? throw new InvalidDataException("安装记录无效。");
        if (receipt.Schema != 1 || receipt.RuntimeDirectory != runtime || receipt.Files is null ||
            receipt.Files.Length is < 14 or > 24 ||
            receipt.Files.Any(file => file is null || !IsOwnedPayloadName(file.Name) ||
                file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) ||
            receipt.Files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Files.Length)
            throw new InvalidDataException("安装记录不匹配。");
        string backupRoot = Path.Combine(efmi, "EndfieldJiggleBackups");
        string session = Path.GetFullPath(receipt.BackupDirectory);
        if (!session.StartsWith(backupRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份目录不匹配。");
        EnsureUnlinked(session);
        string transactionPath = Inside(session, "transaction.json");
        EnsureUnlinked(transactionPath);
        InstallationTransaction transaction = JsonSerializer.Deserialize<InstallationTransaction>(
            File.ReadAllText(transactionPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("安装备份无效。");
        if (transaction.Schema != 1 || transaction.RuntimeDirectory != runtime || transaction.Files is null ||
            transaction.Files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != transaction.Files.Length ||
            !transaction.Files.Select(file => file.Name).Order().SequenceEqual(
                receipt.Files.Select(file => file.Name).Append(ReceiptName).Order()))
            throw new InvalidDataException("备份文件清单不匹配。");
        Dictionary<string, byte[]> applied = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DateTime> appliedTimes = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (byte[]? Bytes, DateTime Time)> original = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledFile file in receipt.Files.Append(new(ReceiptName, Hash(File.ReadAllBytes(receiptPath)))))
        {
            string path = Inside(runtime, file.Name);
            EnsureUnlinked(path);
            byte[] bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != file.Sha256)
                throw new IOException("安装后另有修改，请勿覆盖：" + file.Name);
            applied.Add(file.Name, bytes);
            appliedTimes.Add(file.Name, File.GetLastWriteTimeUtc(path));
            TransactionFile previous = transaction.Files.Single(item => item.Name == file.Name);
            byte[]? backup = null;
            if (previous.Existed)
            {
                string source = Inside(session, file.Name + ".bak");
                EnsureUnlinked(source);
                backup = File.ReadAllBytes(source);
                if (Hash(backup) != previous.Sha256 || File.GetLastWriteTimeUtc(source) != previous.LastWriteTimeUtc)
                    throw new IOException("备份已改变：" + file.Name);
            }
            original.Add(file.Name, (backup, previous.LastWriteTimeUtc));
            if (string.Equals(Path.GetFullPath(path), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                (backup is null || !backup.AsSpan().SequenceEqual(bytes)))
                throw new IOException("不能替换正在运行的 EXE。请从解压包打开程序，再恢复此安装。");
        }
        List<string> changed = [];
        try
        {
            foreach ((string name, (byte[]? bytes, DateTime time)) in original)
            {
                if (requireStopped) EnsureStopped();
                string path = Inside(runtime, name);
                EnsureUnlinked(path);
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(applied[name]))
                    throw new IOException("文件在恢复期间被修改：" + name);
                if (bytes is not null && bytes.AsSpan().SequenceEqual(applied[name]))
                {
                    File.SetLastWriteTimeUtc(path, time);
                    changed.Add(name);
                    continue;
                }
                if (bytes is null) File.Delete(path);
                else
                {
                    AtomicWrite(path, bytes);
                    File.SetLastWriteTimeUtc(path, time);
                }
                changed.Add(name);
            }
        }
        catch (Exception error)
        {
            List<string> conflicts = [];
            foreach (string name in changed.AsEnumerable().Reverse())
            {
                string path = Inside(runtime, name);
                EnsureUnlinked(path);
                byte[]? expected = original[name].Bytes;
                if (expected is null ? File.Exists(path) :
                    !File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
                {
                    conflicts.Add(name);
                    continue;
                }
                AtomicWrite(path, applied[name]);
                File.SetLastWriteTimeUtc(path, appliedTimes[name]);
            }
            throw new IOException(conflicts.Count == 0 ? "恢复失败，已撤销本次恢复。" :
                "恢复失败，有外部修改，未覆盖：" + string.Join("、", conflicts), error);
        }
        RemoveEmptyDirectories(runtime);
        return session;
    }

    private void ValidateReceipt(string runtime)
    {
        string path = Inside(runtime, ReceiptName);
        EnsureUnlinked(path);
        if (!File.Exists(path)) return;
        InstallationReceipt receipt = JsonSerializer.Deserialize<InstallationReceipt>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("安装记录无效。");
        if (receipt.Schema != 1 || receipt.Files is null ||
            !string.Equals(receipt.RuntimeDirectory, runtime, StringComparison.OrdinalIgnoreCase) ||
            receipt.Files.Any(file => file is null || !files.ContainsKey(file.Name)) ||
            receipt.Files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != receipt.Files.Length)
            throw new InvalidDataException("安装记录不匹配，拒绝覆盖。");
        foreach (InstalledFile file in receipt.Files)
        {
            string owned = Inside(runtime, file.Name);
            EnsureUnlinked(owned);
            // Settings are intentionally editable through the optional configurator.
            if (file.Name is "EndfieldJiggle.ini" or "Passes.ini" or "Outfits.ini") continue;
            if (!File.Exists(owned) || Hash(File.ReadAllBytes(owned)) != file.Sha256)
                throw new IOException("已安装文件另被修改，拒绝覆盖：" + file.Name);
        }
    }

    private string Fingerprint(string runtime)
    {
        EnsureUnlinked(runtime);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(runtime.ToLowerInvariant()));
        hash.AppendData([Directory.Exists(runtime) ? (byte)1 : (byte)0]);
        IEnumerable<string> names = files.Keys.Append(ReceiptName);
        if (Directory.Exists(runtime))
            names = names.Concat(EnumerateActiveInis(runtime).Select(path =>
                Path.GetRelativePath(runtime, path).Replace('\\', '/')));
        foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            string path = Inside(runtime, name);
            EnsureUnlinked(path);
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            hash.AppendData([0]);
            if (!File.Exists(path)) continue;
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(path)));
            hash.AppendData(BitConverter.GetBytes(File.GetLastWriteTimeUtc(path).Ticks));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Dictionary<string, byte[]> ReadRuntimeArchive(byte[] bytes)
    {
        Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
        using ZipArchive archive = new(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        const string marker = "/Mods/EndfieldJiggleEFMI/";
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            int offset = entry.FullName.IndexOf(marker, StringComparison.Ordinal);
            if (offset < 0 || entry.FullName.EndsWith('/')) continue;
            string name = entry.FullName[(offset + marker.Length)..].Replace('\\', '/');
            _ = Inside(Path.GetTempPath(), name);
            bool known = IsOwnedPayloadName(name) && !name.StartsWith("Configurator/", StringComparison.Ordinal);
            if (!known || entry.Length > 16 * 1024 * 1024 || (total += entry.Length) > 32 * 1024 * 1024)
                throw new InvalidDataException("内置运行时包含未知或过大的文件：" + name);
            using Stream stream = entry.Open();
            using MemoryStream content = new();
            stream.CopyTo(content);
            if (!files.TryAdd(name, content.ToArray()))
                throw new InvalidDataException("内置运行时文件重复：" + name);
        }
        if (files.Count != 14 || !files.ContainsKey("Outfits.ini") ||
            !files.ContainsKey("EndfieldJiggle.ini") || !files.ContainsKey("Passes.ini"))
            throw new InvalidDataException("内置运行时不完整，需包含已审核的 14 个文件。");
        return files;
    }

    private static void VerifyOriginal(string path, OriginalFile original)
    {
        if (original.Bytes is null)
        {
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("目标路径已被占用：" + path);
        }
        else if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(original.Bytes) ||
                 File.GetLastWriteTimeUtc(path) != original.Time)
            throw new IOException("文件在安装期间被修改：" + path);
    }

    private static bool IsOwnedPayloadName(string name) =>
        name is "EndfieldJiggle.ini" or "Passes.ini" or "Outfits.ini" or "JiggleForge-LICENSE"
        || name.StartsWith("shaders/", StringComparison.Ordinal) && Path.GetExtension(name) is ".hlsl" or ".bin" or ".shdr"
        || name is "Configurator/EndfieldJiggleConfigurator.exe" or "Configurator/DOTNET-LICENSE.txt" or
            "Configurator/DOTNET-THIRD-PARTY-NOTICES.txt" or "Configurator/LICENSE" or
            "Configurator/INSTALL-zh-CN.md" or "Configurator/DISCLAIMER.md";

    private static void EnsureKnownActiveInis(string runtime)
    {
        foreach (string path in EnumerateActiveInis(runtime))
            if (Path.GetRelativePath(runtime, path) is not ("EndfieldJiggle.ini" or "Passes.ini" or "Outfits.ini"))
                throw new InvalidDataException("安装目录有未知的活动 INI，请先核对：" + path);
    }

    private static IEnumerable<string> EnumerateActiveInis(string runtime)
    {
        Queue<string> pending = new();
        pending.Enqueue(runtime);
        int directories = 0;
        while (pending.Count != 0)
        {
            string directory = pending.Dequeue();
            EnsureUnlinked(directory);
            if (++directories > 2048) throw new InvalidDataException("运行时目录结构过大，拒绝处理。");
            foreach (string path in Directory.EnumerateFiles(directory, "*.ini"))
            {
                if (Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)) continue;
                EnsureUnlinked(path);
                yield return path;
            }
            foreach (string path in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                EnsureUnlinked(path);
                pending.Enqueue(path);
            }
        }
    }

    internal static string Inside(string root, string relative)
    {
        string[] parts = relative.Split(['/', '\\']);
        if (Path.IsPathRooted(relative) || parts.Any(part => part is "" or "." or ".." || part.Contains(':')))
            throw new InvalidDataException("不安全的相对路径：" + relative);
        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("路径超出目标目录。");
        return full;
    }

    internal static void EnsureUnlinked(string target)
    {
        for (string? path = Path.GetFullPath(target); path is not null; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("路径不能经过重解析点：" + path);
    }

    internal static void EnsureStopped()
    {
        if (RuntimeSettingsStore.IsGameRunning())
            throw new InvalidOperationException("安装或恢复前请退出游戏和 XXMI。");
    }

    internal static void AtomicWrite(string target, byte[] bytes)
    {
        EnsureUnlinked(target);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            EnsureUnlinked(target);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        EnsureUnlinked(root);
        foreach (string child in Directory.EnumerateDirectories(root)) RemoveEmptyDirectories(child);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed record OriginalFile(string Name, byte[]? Bytes, DateTime Time);
    private sealed record InstalledFile(string Name, string Sha256);
    private sealed record InstallationReceipt(int Schema, string Version, string RuntimeDirectory,
        string BackupDirectory, InstalledFile[] Files);
    private sealed record InstallationTransaction(int Schema, string RuntimeDirectory, TransactionFile[] Files);
    private sealed record TransactionFile(string Name, bool Existed, string? Sha256, DateTime LastWriteTimeUtc);
}
