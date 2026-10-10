using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndfieldJiggle.Configurator.Core;

public sealed record KeyBinding(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public string IniValue =>
        $"{(Ctrl ? "" : "no_")}ctrl {(Shift ? "" : "no_")}shift {(Alt ? "" : "no_")}alt {Key}";

    public string DisplayName
    {
        get
        {
            string key = Key switch
            {
                "VK_LBUTTON" => "鼠标左键",
                "VK_RBUTTON" => "鼠标右键",
                "VK_MBUTTON" => "鼠标中键",
                "VK_XBUTTON1" => "鼠标侧键 1",
                "VK_XBUTTON2" => "鼠标侧键 2",
                _ => Key.StartsWith("VK_", StringComparison.Ordinal) ? Key[3..] : Key,
            };
            string modifiers = string.Join("+", new[]
            {
                Ctrl ? "Ctrl" : null,
                Shift ? "Shift" : null,
                Alt ? "Alt" : null,
            }.Where(value => value is not null));
            return modifiers.Length == 0 ? key : $"{modifiers}+{key}";
        }
    }
}

public sealed record PhysicsSettings(decimal Radius = 0.12m, decimal MaxOffset = 0.04m, decimal DragScale = 0.75m);

public sealed record TouchSettings(
    bool DefaultEnabled,
    KeyBinding Toggle,
    KeyBinding Stop,
    KeyBinding Diagnostic,
    KeyBinding Drag,
    PhysicsSettings Physics)
{
    private static readonly string[] MouseKeys =
        ["VK_LBUTTON", "VK_RBUTTON", "VK_MBUTTON", "VK_XBUTTON1", "VK_XBUTTON2"];

    public static IReadOnlyList<KeyBinding> KeyboardOptions { get; } = BuildKeyboardOptions();
    public static IReadOnlyList<KeyBinding> DragOptions { get; } =
        MouseKeys.Select(key => new KeyBinding(key)).Concat(KeyboardOptions).ToArray();

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Toggle);
        ArgumentNullException.ThrowIfNull(Stop);
        ArgumentNullException.ThrowIfNull(Diagnostic);
        ArgumentNullException.ThrowIfNull(Drag);
        ArgumentNullException.ThrowIfNull(Physics);
        ValidateBinding(Toggle, allowMouse: false);
        ValidateBinding(Stop, allowMouse: false);
        ValidateBinding(Diagnostic, allowMouse: false);
        ValidateBinding(Drag, allowMouse: true);
        KeyBinding[] bindings = [Toggle, Stop, Diagnostic, Drag];
        if (bindings.Select(binding => (binding.Key, binding.Ctrl, binding.Shift, binding.Alt))
            .Distinct().Count() != bindings.Length)
            throw new InvalidDataException("触摸按键不能互相冲突。");

        foreach (KeyBinding binding in bindings)
        {
            if (binding.Key == "VK_F12" && !binding.Shift && !binding.Alt)
                throw new InvalidDataException("F12 和 Ctrl+F12 已由 EFMI 使用。");
            if (!binding.Ctrl && !binding.Shift && !binding.Alt &&
                binding.Key is "Q" or "E" or "VK_LEFT" or "VK_RIGHT")
                throw new InvalidDataException("未修饰的 Q、E、方向键由游戏用于角色导航。");
        }

        if (Physics.Radius is < 0.01m or > 0.5m ||
            Physics.MaxOffset is < 0.001m or > 0.15m ||
            Physics.DragScale is < 0.01m or > 3m)
            throw new InvalidDataException("形变参数超出允许范围。");
    }

    public static KeyBinding ParseBinding(string value, bool allowMouse)
    {
        string[] tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new InvalidDataException("INI 中的触摸按键为空。");
        bool ctrl = tokens.Contains("ctrl", StringComparer.Ordinal);
        bool shift = tokens.Contains("shift", StringComparer.Ordinal);
        bool alt = tokens.Contains("alt", StringComparer.Ordinal);
        bool noModifiers = tokens.Contains("no_modifiers", StringComparer.Ordinal);
        if (noModifiers && tokens.Length != 2 ||
            noModifiers && (ctrl || shift || alt))
            throw new InvalidDataException("INI 中的触摸修饰键格式无效。");
        string[] keys = tokens.Where(token => token is not (
            "ctrl" or "no_ctrl" or "shift" or "no_shift" or "alt" or "no_alt" or "no_modifiers")).ToArray();
        if (keys.Length != 1)
            throw new InvalidDataException("INI 中的按键定义不受支持。");

        foreach ((string positive, string negative) in new[]
                 {
                     ("ctrl", "no_ctrl"), ("shift", "no_shift"), ("alt", "no_alt"),
                 })
        {
            if (tokens.Count(token => token == positive) > 1 ||
                tokens.Count(token => token == negative) > 1 ||
                tokens.Contains(positive, StringComparer.Ordinal) &&
                tokens.Contains(negative, StringComparer.Ordinal))
                throw new InvalidDataException("INI 中的触摸修饰键重复或矛盾。");
        }
        KeyBinding binding = new(keys[0], ctrl, shift, alt);
        ValidateBinding(binding, allowMouse);
        return binding;
    }

    private static void ValidateBinding(KeyBinding binding, bool allowMouse)
    {
        string key = binding.Key;
        bool keyboard = key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9');
        bool function = Regex.IsMatch(key, @"^VK_F(?:[1-9]|12)$", RegexOptions.CultureInvariant) &&
            key is not ("VK_F10" or "VK_F11");
        bool navigation = key is "VK_HOME" or "VK_END" or "VK_INSERT" or "VK_DELETE" or "VK_PRIOR" or "VK_NEXT";
        bool mouse = allowMouse && MouseKeys.Contains(key, StringComparer.Ordinal);
        if (!keyboard && !function && !navigation && !mouse)
            throw new InvalidDataException($"不支持按键：{key}");
    }

    private static IReadOnlyList<KeyBinding> BuildKeyboardOptions()
    {
        IEnumerable<KeyBinding> letters = Enumerable.Range('A', 26).Select(value => new KeyBinding(((char)value).ToString()));
        IEnumerable<KeyBinding> digits = Enumerable.Range('0', 10).Select(value => new KeyBinding(((char)value).ToString()));
        IEnumerable<KeyBinding> functions = Enumerable.Range(1, 12)
            .Where(value => value is not (10 or 11)).Select(value => new KeyBinding($"VK_F{value}"));
        IEnumerable<KeyBinding> navigation = new[]
            { "VK_HOME", "VK_END", "VK_INSERT", "VK_DELETE", "VK_PRIOR", "VK_NEXT" }
            .Select(key => new KeyBinding(key));
        return functions.Concat(letters).Concat(digits).Concat(navigation).ToArray();
    }
}

public sealed record RuntimeSettingsSnapshot(TouchSettings Settings, string Fingerprint);
public sealed record RuntimeApplyResult(bool Changed, string? BackupDirectory, IReadOnlyList<string> ChangedFiles);

public sealed class RuntimeSettingsStore
{
    private const string RuntimeName = "EndfieldJiggle.ini";
    private const string PassesName = "Passes.ini";
    private const string OutfitsName = "Outfits.ini";
    private const string BackupDirectoryName = "ConfiguratorBackups";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string root;

    public string Root => root;
    public bool HasRestorableBackup
    {
        get
        {
            string backupRoot = Path.Combine(root, BackupDirectoryName);
            if (!Directory.Exists(backupRoot))
                return false;
            EnsureNoReparsePoints(backupRoot);
            return Directory.EnumerateDirectories(backupRoot).Any(IsAppliedBackup);
        }
    }

    public RuntimeSettingsStore(string runtimeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        root = Path.GetFullPath(runtimeDirectory);
        VerifyRuntimeRoot(root);
    }

    public static string? FindRuntimeDirectory(string startDirectory)
    {
        string current = Path.GetFullPath(startDirectory);
        for (int depth = 0; depth < 10; depth++)
        {
            if (File.Exists(Path.Combine(current, RuntimeName)) &&
                File.Exists(Path.Combine(current, PassesName)))
            {
                try
                {
                    VerifyRuntimeRoot(current);
                    return current;
                }
                catch (InvalidDataException) { }
            }
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
                break;
            current = parent.FullName;
        }
        return null;
    }

    public RuntimeSettingsSnapshot Read()
    {
        Dictionary<string, byte[]> bytes = ReadOwnedFiles();
        string runtimeText = Decode(bytes[RuntimeName]);
        IniFile runtime = IniFile.Parse(runtimeText);
        EnsureNamespace(runtime, RuntimeName);
        TouchSettings settings = new(
            ParseBoolean(runtime.Get("Constants", "global $enabled")),
            TouchSettings.ParseBinding(runtime.Get("KeyToggle", "key"), allowMouse: false),
            TouchSettings.ParseBinding(runtime.Get("KeyStop", "key"), allowMouse: false),
            TouchSettings.ParseBinding(runtime.Get("KeyDiagnostic", "key"), allowMouse: false),
            TouchSettings.ParseBinding(runtime.Get("KeyDrag", "key"), allowMouse: true),
            new PhysicsSettings(
                ParseDecimal(runtime.Get("Constants", "global $radius")),
                ParseDecimal(runtime.Get("Constants", "global $max_offset")),
                ParseDecimal(runtime.Get("Constants", "global $drag_scale"))));
        settings.Validate();
        ValidatePasses(Decode(bytes[PassesName]));
        if (bytes.TryGetValue(OutfitsName, out byte[]? outfitBytes))
            ValidateOutfits(Decode(outfitBytes));
        return new(settings, Fingerprint(bytes));
    }

    public RuntimeApplyResult Apply(TouchSettings settings, string expectedFingerprint, bool requireStopped = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (requireStopped)
            EnsureGameStopped();

        Dictionary<string, byte[]> original = ReadOwnedFiles();
        if (!string.Equals(Fingerprint(original), expectedFingerprint, StringComparison.Ordinal))
            throw new IOException("运行时文件在打开配置器后发生变化。请重新读取再应用。");

        Dictionary<string, byte[]> updated = new(StringComparer.OrdinalIgnoreCase)
        {
            [RuntimeName] = Encode(PatchRuntime(Decode(original[RuntimeName]), settings), original[RuntimeName]),
            [PassesName] = Encode(PatchInputGate(Decode(original[PassesName]), PassesName), original[PassesName]),
        };
        if (original.TryGetValue(OutfitsName, out byte[]? outfitBytes))
            updated[OutfitsName] = Encode(PatchInputGate(Decode(outfitBytes), OutfitsName), outfitBytes);

        Dictionary<string, byte[]> changed = updated
            .Where(pair => !original[pair.Key].AsSpan().SequenceEqual(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (changed.Count == 0)
            return new(false, null, []);
        string backupRoot = Path.Combine(root, BackupDirectoryName);
        EnsureNoReparsePoints(backupRoot);
        Directory.CreateDirectory(backupRoot);
        EnsureNoReparsePoints(backupRoot);
        string backup = Path.Combine(backupRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
            "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        Dictionary<string, DateTime> originalTimes = changed.Keys.ToDictionary(
            name => name, name => File.GetLastWriteTimeUtc(Path.Combine(root, name)), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DateTime> stagedTimes = changed.Keys.ToDictionary(
            name => name, name => File.GetLastWriteTimeUtc(Path.Combine(root, name)), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, byte[]> before = changed.Keys.ToDictionary(
            name => name, name => original[name], StringComparer.OrdinalIgnoreCase);
        List<(string Name, string Path)> temporaries = [];
        string manifestPath = Path.Combine(backup, "manifest.json");
        BackupManifest? manifest = null;
        bool mutationStarted = false;

        try
        {
            foreach (string name in changed.Keys)
            {
                File.WriteAllBytes(Path.Combine(backup, name + ".bak"), before[name]);
                File.SetLastWriteTimeUtc(Path.Combine(backup, name + ".bak"), originalTimes[name]);
                string temp = Path.Combine(root, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
                temporaries.Add((name, temp));
                using FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(changed[name]);
                stream.Flush(flushToDisk: true);
            }
            manifest = new(1, "prepared", root, changed.Keys.Select(name =>
                new BackupFile(name, Hash(before[name]), originalTimes[name].ToString("O"),
                    Hash(changed[name]), stagedTimes[name].ToString("O"))).ToArray());
            WriteJsonAtomic(manifestPath, manifest);
            if (requireStopped)
                EnsureGameStopped();
            if (!string.Equals(Fingerprint(ReadOwnedFiles()), expectedFingerprint, StringComparison.Ordinal))
                throw new IOException("运行时文件在备份期间发生变化。");
            foreach ((string name, string temp) in temporaries)
            {
                if (requireStopped)
                    EnsureGameStopped();
                File.Move(temp, Path.Combine(root, name), overwrite: true);
                mutationStarted = true;
            }
            foreach (string name in changed.Keys)
            {
                if (requireStopped)
                    EnsureGameStopped();
                File.SetLastWriteTimeUtc(Path.Combine(root, name), stagedTimes[name]);
            }
            if (requireStopped)
                EnsureGameStopped();
            manifest = manifest with { Status = "applied" };
            WriteJsonAtomic(manifestPath, manifest);
            return new(true, backup, changed.Keys.ToArray());
        }
        catch (Exception applyError)
        {
            if (!mutationStarted)
            {
                if (manifest is not null)
                {
                    try { WriteJsonAtomic(manifestPath, manifest with { Status = "not-applied" }); }
                    catch (IOException) { }
                }
                throw;
            }
            try
            {
                foreach (string name in changed.Keys)
                {
                    if (requireStopped)
                        EnsureGameStopped();
                    AtomicReplace(Path.Combine(root, name), before[name]);
                    File.SetLastWriteTimeUtc(Path.Combine(root, name), originalTimes[name]);
                }
                BackupManifest failed = new(1, "rolled-back", root, changed.Keys.Select(name =>
                    new BackupFile(name, Hash(before[name]), originalTimes[name].ToString("O"),
                        Hash(changed[name]), stagedTimes[name].ToString("O"))).ToArray());
                WriteJsonAtomic(Path.Combine(backup, "manifest.json"), failed);
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    $"应用设置失败：{applyError.Message}。自动回滚也失败：{rollbackError.Message}。备份保留在 {backup}",
                    new AggregateException(applyError, rollbackError));
            }
            throw;
        }
        finally
        {
            foreach ((_, string temp) in temporaries)
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }

    public RuntimeApplyResult RestoreLast(bool requireStopped = true)
    {
        if (requireStopped)
            EnsureGameStopped();
        string backupRoot = Path.Combine(root, BackupDirectoryName);
        EnsureNoReparsePoints(backupRoot);
        if (!Directory.Exists(backupRoot))
            throw new DirectoryNotFoundException("没有可恢复的配置备份。");
        string? latest = Directory.EnumerateDirectories(backupRoot)
            .OrderByDescending(directory => Path.GetFileName(directory), StringComparer.Ordinal)
            .FirstOrDefault(directory => IsAppliedBackup(directory));
        if (latest is null)
            throw new DirectoryNotFoundException("没有可恢复的配置备份。");

        string manifestPath = Path.Combine(latest, "manifest.json");
        BackupManifest manifestData = JsonSerializer.Deserialize<BackupManifest>(
            File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("配置备份清单无效。");
        if (manifestData.Schema != 1 || manifestData.Status != "applied" ||
            !string.Equals(Path.GetFullPath(manifestData.RuntimeDirectory), root, StringComparison.OrdinalIgnoreCase) ||
            manifestData.Files.Length == 0)
            throw new InvalidDataException("最新配置备份状态不匹配。");
        if (requireStopped)
            EnsureGameStopped();

        Dictionary<string, byte[]> current = ReadOwnedFiles();
        foreach (BackupFile file in manifestData.Files)
        {
            if (file.Name is not (RuntimeName or PassesName or OutfitsName) ||
                !current.TryGetValue(file.Name, out byte[]? bytes) ||
                !string.Equals(Hash(bytes), file.AppliedSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(File.GetLastWriteTimeUtc(Path.Combine(root, file.Name)).ToString("O"),
                    file.AppliedLastWriteTimeUtc, StringComparison.Ordinal))
                throw new IOException($"拒绝覆盖已被后续修改的文件：{file.Name}");
        }

        Dictionary<string, byte[]> beforeRestore = manifestData.Files.ToDictionary(
            file => file.Name, file => current[file.Name], StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DateTime> beforeRestoreTimes = manifestData.Files.ToDictionary(
            file => file.Name, file => File.GetLastWriteTimeUtc(Path.Combine(root, file.Name)),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (byte[] Bytes, DateTime LastWriteTimeUtc)> originals =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupFile file in manifestData.Files)
        {
            string source = Path.Combine(latest, file.Name + ".bak");
            EnsureNoReparsePoints(source);
            byte[] original = File.ReadAllBytes(source);
            if (!string.Equals(Hash(original), file.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"配置备份已损坏：{file.Name}");
            DateTime originalTime = DateTime.Parse(file.OriginalLastWriteTimeUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            originals.Add(file.Name, (original, originalTime));
        }
        bool restoreStarted = false;
        try
        {
            foreach (BackupFile file in manifestData.Files)
            {
                if (requireStopped)
                    EnsureGameStopped();
                AtomicReplace(Path.Combine(root, file.Name), originals[file.Name].Bytes);
                restoreStarted = true;
                File.SetLastWriteTimeUtc(Path.Combine(root, file.Name), originals[file.Name].LastWriteTimeUtc);
            }
            if (requireStopped)
                EnsureGameStopped();
            WriteJsonAtomic(manifestPath, manifestData with { Status = "restored" });
        }
        catch (Exception restoreError)
        {
            if (!restoreStarted)
                throw;
            try
            {
                foreach (string name in beforeRestore.Keys)
                {
                    if (requireStopped)
                        EnsureGameStopped();
                    AtomicReplace(Path.Combine(root, name), beforeRestore[name]);
                    File.SetLastWriteTimeUtc(Path.Combine(root, name), beforeRestoreTimes[name]);
                }
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    $"恢复失败：{restoreError.Message}。自动回滚也失败：{rollbackError.Message}。备份保留在 {latest}",
                    new AggregateException(restoreError, rollbackError));
            }
            throw;
        }
        return new(true, latest, manifestData.Files.Select(file => file.Name).ToArray());
    }

    public static bool IsGameRunning()
    {
        int currentProcessId = Environment.ProcessId;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try { name = process.ProcessName; }
                catch (InvalidOperationException) { continue; }
                if (IsGameOrLoaderProcess(process.Id, name, currentProcessId))
                    return true;
            }
        }
        return false;
    }

    internal static bool IsGameOrLoaderProcess(int processId, string name, int currentProcessId) =>
        processId != currentProcessId &&
        (name.Contains("Endfield", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("XXMI", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("loader_host", StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, byte[]> ReadOwnedFiles()
    {
        VerifyRuntimeRoot(root);
        Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { RuntimeName, PassesName, OutfitsName })
        {
            string path = Path.Combine(root, name);
            EnsureNoReparsePoints(path);
            if (File.Exists(path))
                files.Add(name, File.ReadAllBytes(path));
        }
        if (!files.ContainsKey(RuntimeName) || !files.ContainsKey(PassesName))
            throw new FileNotFoundException("请选择已安装的 EndfieldJiggleEFMI 运行时文件夹。");
        return files;
    }

    private static void VerifyRuntimeRoot(string path)
    {
        EnsureNoReparsePoints(path);
        string runtime = Path.Combine(path, RuntimeName);
        string passes = Path.Combine(path, PassesName);
        if (!File.Exists(runtime) || !File.Exists(passes))
            throw new FileNotFoundException("目录中未找到 EndfieldJiggle.ini 和 Passes.ini。");
        EnsureNamespace(IniFile.Parse(Decode(File.ReadAllBytes(runtime))), RuntimeName);
        ValidatePasses(Decode(File.ReadAllBytes(passes)));
    }

    private static string PatchRuntime(string text, TouchSettings settings)
    {
        IniFile ini = IniFile.Parse(text);
        EnsureNamespace(ini, RuntimeName);
        Dictionary<(string Section, string Key), string> replacements = new()
        {
            [("Constants", "global $enabled")] = settings.DefaultEnabled ? "1" : "0",
            [("Constants", "global $radius")] = Format(settings.Physics.Radius),
            [("Constants", "global $max_offset")] = Format(settings.Physics.MaxOffset),
            [("Constants", "global $drag_scale")] = Format(settings.Physics.DragScale),
            [("KeyToggle", "key")] = settings.Toggle.IniValue,
            [("KeyStop", "key")] = settings.Stop.IniValue,
            [("KeyDiagnostic", "key")] = settings.Diagnostic.IniValue,
            [("KeyDrag", "key")] = settings.Drag.IniValue,
            [("KeyUIMouse", "key")] = "VK_LBUTTON",
        };
        return ini.Replace(replacements).Replace(
            " || $ui_mouse ||", " || ($ui_mouse && !$drag) ||", StringComparison.Ordinal);
    }

    private static string PatchInputGate(string text, string fileName)
    {
        IniFile ini = IniFile.Parse(text);
        EnsureNamespace(ini, fileName);
        string old = "&& !$ui_mouse &&";
        string current = "&& !($ui_mouse && !$drag) &&";
        bool hasCurrent = ini.Text.Contains(current, StringComparison.Ordinal);
        int oldCount = CountOccurrences(ini.Text, old);
        if (!hasCurrent && oldCount == 0)
            throw new InvalidDataException($"{fileName} 的触摸输入条件不受支持。");
        return ini.Text.Replace(old, current, StringComparison.Ordinal);
    }

    private static void ValidatePasses(string text)
    {
        IniFile ini = IniFile.Parse(text);
        EnsureNamespace(ini, PassesName);
        int knownGates = CountOccurrences(ini.Text, "&& !$ui_mouse &&") +
            CountOccurrences(ini.Text, "&& !($ui_mouse && !$drag) &&");
        if (knownGates != 12)
            throw new InvalidDataException("Passes.ini 的 12 个触摸输入条件不匹配，拒绝改写。");
    }

    private static void ValidateOutfits(string text)
    {
        IniFile ini = IniFile.Parse(text);
        EnsureNamespace(ini, OutfitsName);
        int knownGates = CountOccurrences(ini.Text, "&& !$ui_mouse &&") +
            CountOccurrences(ini.Text, "&& !($ui_mouse && !$drag) &&");
        if (knownGates != 1)
            throw new InvalidDataException("Outfits.ini 的触摸输入条件不匹配，拒绝改写。");
    }

    private static void EnsureNamespace(IniFile ini, string fileName)
    {
        if (ini.NamespaceCount != 1 || ini.NamespaceName != "EndfieldJiggleEFMI")
            throw new InvalidDataException($"{fileName} 不是受支持的 EndfieldJiggleEFMI 文件。");
    }

    private static bool ParseBoolean(string value) => value switch
    {
        "0" => false,
        "1" => true,
        _ => throw new InvalidDataException("触摸启动状态必须为 0 或 1。"),
    };

    private static decimal ParseDecimal(string value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : throw new InvalidDataException($"INI 数值无效：{value}");

    private static string Format(decimal value) =>
        value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }
        return count;
    }

    private bool IsAppliedBackup(string directory)
    {
        string manifest = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifest) || IsReparsePoint(manifest))
            return false;
        try
        {
            EnsureNoReparsePoints(directory);
            BackupManifest? value = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifest), JsonOptions);
            return value is { Schema: 1, Status: "applied" } &&
                string.Equals(Path.GetFullPath(value.RuntimeDirectory), root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private string Fingerprint(IReadOnlyDictionary<string, byte[]> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string name, byte[] bytes) in files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
            hash.AppendData([0]);
            hash.AppendData(SHA256.HashData(bytes));
            hash.AppendData(BitConverter.GetBytes(File.GetLastWriteTimeUtc(Path.Combine(root, name)).Ticks));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetString(bytes);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }

    private static byte[] Encode(string text, byte[] original)
    {
        bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
        return new UTF8Encoding(bom).GetBytes(text);
    }

    private static void EnsureGameStopped()
    {
        if (IsGameRunning())
            throw new InvalidOperationException("请先退出游戏和 XXMI，再应用或恢复设置。");
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void EnsureNoReparsePoints(string target)
    {
        string full = Path.GetFullPath(target);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (IsReparsePoint(current))
                throw new InvalidDataException($"路径不能经过重解析点：{current}");
        }
    }

    private static void AtomicReplace(string target, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void WriteJsonAtomic(string path, BackupManifest value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private sealed record BackupManifest(
        int Schema, string Status, string RuntimeDirectory, BackupFile[] Files);
    private sealed record BackupFile(
        string Name, string OriginalSha256, string OriginalLastWriteTimeUtc,
        string AppliedSha256, string AppliedLastWriteTimeUtc);

    private sealed class IniFile
    {
        private static readonly Regex SectionRegex = new(@"^\s*\[([^\]]+)\]\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex EntryRegex = new(@"^(\s*)([^;=]+?)\s*=\s*(.*)$", RegexOptions.CultureInvariant);
        private readonly string[] lines;
        private readonly Dictionary<string, List<int>> sections;
        private readonly Dictionary<string, int> namespaces;
        public string Text { get; }
        public int NamespaceCount => namespaces.Values.Sum();
        public string? NamespaceName => namespaces.Count == 1 ? namespaces.Keys.Single() : null;

        private IniFile(string text, string[] lines,
            Dictionary<string, List<int>> sections, Dictionary<string, int> namespaces)
        {
            Text = text;
            this.lines = lines;
            this.sections = sections;
            this.namespaces = namespaces;
        }

        public static IniFile Parse(string text)
        {
            string[] lines = text.Split('\n');
            Dictionary<string, List<int>> sections = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> namespaces = new(StringComparer.Ordinal);
            string? active = null;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].TrimEnd('\r');
                Match section = SectionRegex.Match(line);
                if (section.Success)
                {
                    active = section.Groups[1].Value;
                    if (!sections.TryGetValue(active, out List<int>? indexes))
                        sections.Add(active, indexes = []);
                    indexes.Add(index);
                    continue;
                }
                if (active is null && line.TrimStart().StartsWith("namespace", StringComparison.OrdinalIgnoreCase) &&
                    TryParseEntry(line, out string key, out string value) &&
                    key.Equals("namespace", StringComparison.OrdinalIgnoreCase))
                    namespaces[value] = namespaces.GetValueOrDefault(value) + 1;
            }
            return new IniFile(text, lines, sections, namespaces);
        }

        public string Get(string section, string key)
        {
            int index = FindEntry(section, key);
            Match match = EntryRegex.Match(lines[index].TrimEnd('\r'));
            return match.Groups[3].Value.Trim();
        }

        public string Replace(IReadOnlyDictionary<(string Section, string Key), string> values)
        {
            string[] patched = lines.ToArray();
            foreach (((string section, string key), string value) in values)
            {
                int index = FindEntry(section, key);
                string raw = patched[index].TrimEnd('\r');
                Match match = EntryRegex.Match(raw);
                string content = match.Groups[3].Value;
                Match comment = Regex.Match(content, @"(\s+[;#].*)$", RegexOptions.CultureInvariant);
                string suffix = comment.Success ? comment.Value : "";
                patched[index] = match.Groups[1].Value + match.Groups[2].Value.Trim() + " = " + value + suffix +
                    (patched[index].EndsWith('\r') ? "\r" : "");
            }
            return string.Join("\n", patched);
        }

        private int FindEntry(string section, string key)
        {
            if (!sections.TryGetValue(section, out List<int>? starts) || starts.Count != 1)
                throw new InvalidDataException($"INI 节重复或缺失：[{section}]");
            int start = starts[0] + 1;
            int end = sections.Values.SelectMany(value => value).Where(index => index > starts[0])
                .DefaultIfEmpty(lines.Length).Min();
            List<int> found = [];
            for (int index = start; index < end; index++)
            {
                if (TryParseEntry(lines[index].TrimEnd('\r'), out string actual, out _) &&
                    actual.Equals(key, StringComparison.OrdinalIgnoreCase))
                    found.Add(index);
            }
            if (found.Count != 1)
                throw new InvalidDataException($"INI 项重复或缺失：[{section}] {key}");
            return found[0];
        }

        private static bool TryParseEntry(string line, out string key, out string value)
        {
            Match match = EntryRegex.Match(line);
            key = match.Success ? match.Groups[2].Value.Trim() : "";
            value = match.Success ? match.Groups[3].Value.Trim() : "";
            return match.Success;
        }
    }
}
