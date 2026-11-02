using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndfieldJiggle.Configurator.Core;

public sealed record LayoutRepairPreview(string EfmiDirectory, string RuntimeDirectory,
    string Fingerprint, string[] QuarantineFiles, string[] MissingFiles);
public sealed record LayoutRepairResult(InstallationResult Installation, string LayoutBackupDirectory);

public sealed partial class InstallablePackage
{
    public LayoutRepairPreview PreviewLayoutRepair(string selectedDirectory)
    {
        if (!Available) throw new InvalidOperationException("此配置器没有内置运行文件。");
        RuntimeLayout layout = RuntimeLocationLocator.Inspect(selectedDirectory);
        List<RuntimeCandidate> readable = [];
        foreach (RuntimeCandidate candidate in layout.Candidates)
        {
            EnsureUnlinked(candidate.Directory);
            foreach (string path in candidate.ActiveInis) EnsureUnlinked(path);
            if (RuntimeLocationLocator.HasRuntimeFiles(candidate.Directory))
            {
                _ = new RuntimeSettingsStore(candidate.Directory).Read();
                readable.Add(candidate);
            }
            else
            {
                foreach (string path in candidate.ActiveInis)
                    if (!files.TryGetValue(Path.GetFileName(path), out byte[]? expected) ||
                        !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
                        throw new InvalidDataException("残留 INI 不是可核对的自有文件，未移动：" + path);
            }
        }
        string selected = Path.GetFullPath(selectedDirectory).TrimEnd(Path.DirectorySeparatorChar);
        RuntimeCandidate? target = readable.Where(item =>
            selected.Equals(item.Directory, StringComparison.OrdinalIgnoreCase) ||
            selected.StartsWith(item.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Directory.Length).FirstOrDefault();
        target ??= readable.Count == 1 ? readable[0] :
            readable.Count(item => item.Problem is null) == 1 ? readable.Single(item => item.Problem is null) : null;
        if (target is null)
            throw new InvalidDataException("请选择要保留并补齐的触摸目录，目录中需有可读取的 EndfieldJiggle.ini 和 Passes.ini。");

        string[] quarantine = layout.Candidates.Where(item => item.Directory != target.Directory)
            .SelectMany(item => item.ActiveInis).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        HashSet<string> recognized = layout.Candidates.SelectMany(item => item.ActiveInis)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeCandidate candidate in layout.Candidates)
            foreach (string path in EnumerateActiveInis(candidate.Directory))
                if (!recognized.Contains(path))
                    throw new InvalidDataException("触摸目录中有其他活动 INI，未自动处理：" + path);
        foreach ((string name, byte[] bytes) in files.Where(pair =>
            !pair.Key.EndsWith(".ini", StringComparison.Ordinal) &&
            !pair.Key.StartsWith("Configurator/", StringComparison.Ordinal)))
        {
            string path = Inside(target.Directory, name);
            EnsureUnlinked(path);
            if (File.Exists(path) && !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                throw new IOException("目标自有资源已被修改，未覆盖：" + path);
        }
        ValidateReceiptForRepair(target.Directory);
        string[] missing = files.Keys.Where(name => !File.Exists(Inside(target.Directory, name)))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (RuntimeCandidate candidate in layout.Candidates)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(candidate.Directory.ToLowerInvariant()));
            hash.AppendData(Encoding.UTF8.GetBytes(Fingerprint(candidate.Directory)));
        }
        return new(layout.EfmiDirectory, target.Directory, Convert.ToHexString(hash.GetHashAndReset()),
            quarantine, missing);
    }

    public LayoutRepairResult RepairLayout(LayoutRepairPreview preview, bool requireStopped = true)
    {
        if (requireStopped) EnsureStopped();
        LayoutRepairPreview current = PreviewLayoutRepair(preview.RuntimeDirectory);
        if (current.EfmiDirectory != preview.EfmiDirectory || current.Fingerprint != preview.Fingerprint ||
            !current.QuarantineFiles.SequenceEqual(preview.QuarantineFiles) ||
            !current.MissingFiles.SequenceEqual(preview.MissingFiles))
            throw new IOException("目录自修复预览后已变化，请重新检查。");
        string backupRoot = Path.Combine(current.EfmiDirectory, "EndfieldJiggleBackups");
        EnsureUnlinked(backupRoot);
        string session = Path.Combine(backupRoot, "layout-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(session);
        List<(string Source, string Backup, string Hash, DateTime Time)> moved = [];
        InstallationResult? installed = null;
        try
        {
            foreach (string source in current.QuarantineFiles)
            {
                if (requireStopped) EnsureStopped();
                EnsureUnlinked(source);
                string destination = Inside(session, Path.GetRelativePath(current.EfmiDirectory, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string digest = Hash(File.ReadAllBytes(source));
                DateTime time = File.GetLastWriteTimeUtc(source);
                moved.Add((source, destination, digest, time));
                WriteLayoutTransaction(session, "prepared", current, moved);
                File.Move(source, destination);
            }
            InstallationPreview installation = Preview(current.RuntimeDirectory, repairMissingFiles: true);
            installed = Install(installation, requireStopped);
            WriteLayoutTransaction(session, "repaired", current, moved);
            return new(installed, session);
        }
        catch
        {
            if (installed is not null) RestoreInstallation(installed.RuntimeDirectory, requireStopped);
            foreach (var item in moved.AsEnumerable().Reverse())
            {
                EnsureUnlinked(item.Source);
                EnsureUnlinked(item.Backup);
                if (File.Exists(item.Backup) && !File.Exists(item.Source) &&
                    Hash(File.ReadAllBytes(item.Backup)) == item.Hash &&
                    File.GetLastWriteTimeUtc(item.Backup) == item.Time)
                    File.Move(item.Backup, item.Source);
            }
            throw;
        }
    }

    private void ValidateReceiptForRepair(string runtime)
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
            throw new InvalidDataException("安装记录不匹配，未自动修复。");
        foreach (InstalledFile file in receipt.Files)
        {
            string owned = Inside(runtime, file.Name);
            EnsureUnlinked(owned);
            if (file.Name is "EndfieldJiggle.ini" or "Passes.ini" or "Outfits.ini" || !File.Exists(owned)) continue;
            if (Hash(File.ReadAllBytes(owned)) != file.Sha256)
                throw new IOException("已安装文件另被修改，未覆盖：" + file.Name);
        }
    }

    private static void WriteLayoutTransaction(string session, string status, LayoutRepairPreview preview,
        IEnumerable<(string Source, string Backup, string Hash, DateTime Time)> moved) =>
        AtomicWrite(Path.Combine(session, "layout-transaction.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schema = 1, status, efmiDirectory = preview.EfmiDirectory, runtimeDirectory = preview.RuntimeDirectory,
            quarantined = moved.Select(item => new
                { source = item.Source, backup = item.Backup, sha256 = item.Hash, lastWriteTimeUtc = item.Time }),
            liveGameVerified = false,
        }, JsonOptions)));
}
