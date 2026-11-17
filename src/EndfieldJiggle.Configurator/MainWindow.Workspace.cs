using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using EndfieldJiggle.Configurator.Core;
using Microsoft.Win32;
using KeyBinding = EndfieldJiggle.Configurator.Core.KeyBinding;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow
{
    private List<LibraryRow> libraryRows = [];
    private string? recordingBinding;
    private string? reloadBinding;
    private bool operationBusy;
    private bool writeInProgress;
    private CancellationTokenSource? operationCancellation;
    private static string WorkspacePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EndfieldJiggle", "workspace.json");

    private sealed record LibraryRow(string Name, string Directory, string State, bool Disabled, BitmapSource? Preview);

    private static string? ReadPreviousWorkspace()
    {
        if (Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal))
            return null;
        try
        {
            string? path = JsonSerializer.Deserialize<string>(File.ReadAllText(WorkspacePath));
            return path is not null && Directory.Exists(path) ? path : null;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void RememberWorkspace(string efmi)
    {
        if (Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WorkspacePath)!);
            File.WriteAllText(WorkspacePath, JsonSerializer.Serialize(efmi));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WriteOperationLog("保存最近目录", TimeSpan.Zero, error);
        }
    }

    private static bool IsEfmiOrMods(string directory) =>
        File.Exists(Path.Combine(directory, "d3dx.ini")) ||
        Path.GetFileName(directory).Equals("Mods", StringComparison.OrdinalIgnoreCase);

    private async Task OpenWorkspaceAsync(string directory, CancellationToken token = default)
    {
        RuntimeLocation location = await Task.Run(() =>
            RuntimeLocationLocator.Resolve(directory, token, allowProblems: true), token);
        await SelectEfmiAsync(location, token);
        MainTabs.SelectedItem = store is null ? InstallationTab : LibraryTab;
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("扫描 Mod 库", RefreshLibraryAsync);
    }

    private async Task RefreshLibraryAsync(CancellationToken token = default)
    {
        if (LibraryList is null || string.IsNullOrEmpty(EfmiPathBox.Text)) return;
        string mods = Path.Combine(EfmiPathBox.Text, "Mods");
        List<LibraryRow> rows = await Task.Run(() => ModLibraryStore.Scan(mods, token).Select(entry =>
        {
            token.ThrowIfCancellationRequested();
            return new LibraryRow(entry.Name, entry.Directory, entry.Disabled ? "已停用" :
                entry.State == "Adapter" ? "已适配" : "未适配", entry.Disabled, LoadPreview(entry.PreviewPath));
        }).ToList(), token);
        token.ThrowIfCancellationRequested();
        libraryRows = rows;
        LibrarySummaryText.Text = $"{libraryRows.Count(row => !row.Disabled)} 个启用 · " +
            $"{libraryRows.Count(row => row.Disabled)} 个停用";
        FilterLibrary();
    }

    private static BitmapSource? LoadPreview(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024)
                return null;
            using FileStream stream = File.OpenRead(path);
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 176;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void LibraryFilter_Changed(object sender, RoutedEventArgs e) => FilterLibrary();

    private void FilterLibrary()
    {
        if (LibraryList is null || LibrarySearchBox is null || ShowDisabledCheck is null) return;
        string query = LibrarySearchBox.Text.Trim();
        LibraryRow[] rows = libraryRows.Where(row => (ShowDisabledCheck.IsChecked == true || !row.Disabled) &&
            (query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Directory.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        LibraryList.ItemsSource = rows;
        LibraryEmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Library_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryList.SelectedItem is LibraryRow row)
            FooterText.Text = row.Directory;
    }

    private async void Library_Open(object sender, MouseButtonEventArgs e)
    {
        if (LibraryList.SelectedItem is LibraryRow row)
            await RunOperationAsync("分析换装", token => SelectOutfitAsync(row.Directory, token));
    }

    private static void OpenDirectory(string? directory)
    {
        if (directory is not null && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void OpenRuntimeFolder_Click(object sender, RoutedEventArgs e) => OpenDirectory(store?.Root);
    private void OpenOutfitFolder_Click(object sender, RoutedEventArgs e) => OpenDirectory(outfitStore?.Root);

    private void UpdateRuntimeSummary(TouchSettings settings)
    {
        RuntimeKeysText.Text = $"启动：{(settings.DefaultEnabled ? "开启" : "关闭")} · " +
            $"触摸开关：{settings.Toggle.DisplayName} · 拖动：{settings.Drag.DisplayName}";
    }

    private void PhysicsPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || RadiusSlider is null || PhysicsPresetCombo.SelectedIndex <= 0) return;
        PhysicsSettings physics = PhysicsPresetCombo.SelectedIndex switch
        {
            1 => new(0.10m, 0.02m, 0.45m),
            2 => new(),
            _ => new(0.16m, 0.065m, 1.10m),
        };
        loading = true;
        RadiusSlider.Value = (double)physics.Radius;
        MaxOffsetSlider.Value = (double)physics.MaxOffset;
        DragScaleSlider.Value = (double)physics.DragScale;
        UpdatePhysicsLabels();
        loading = false;
        FooterText.Text = "参数预设尚未保存。";
    }

    private void NoShiftDrag_Click(object sender, RoutedEventArgs e)
    {
        SetBinding(TouchSettings.Default.Drag, DragKeyCombo, DragCtrlCheck, DragShiftCheck, DragAltCheck);
        FooterText.Text = "拖动已改为 Alt+鼠标左键，尚未保存。";
    }

    private void RecordBinding_Click(object sender, RoutedEventArgs e)
    {
        recordingBinding = (string)((Button)sender).Tag;
        FooterText.Text = "等待录入组合键；Esc 取消。";
        Keyboard.Focus((Button)sender);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (recordingBinding != "drag" || key is not (Key.LeftAlt or Key.RightAlt))
            return;
        KeyBinding binding = new("VK_MENU",
            Ctrl: Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Shift: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        SetBinding(binding, DragKeyCombo, DragCtrlCheck, DragShiftCheck, DragAltCheck);
        recordingBinding = null;
        FooterText.Text = $"已录入 {binding.DisplayName}，尚未保存。";
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (recordingBinding is null) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            recordingBinding = null;
            FooterText.Text = "按键录入已取消。";
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt ||
            e.IsRepeat) return;
        string name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            >= Key.F1 and <= Key.F12 => "VK_" + key,
            Key.PageUp => "VK_PRIOR",
            Key.PageDown => "VK_NEXT",
            Key.Home => "VK_HOME",
            Key.End => "VK_END",
            Key.Insert => "VK_INSERT",
            Key.Delete => "VK_DELETE",
            _ => "",
        };
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (name.Length == 0 || !TouchSettings.KeyboardOptions.Any(option => option.Key == name))
        {
            FooterText.Text = "此按键不可用；F10、F11 保留给 EFMI。继续录入或按 Esc 取消。";
            return;
        }
        KeyBinding binding = new(name, ctrl, shift, alt);
        switch (recordingBinding)
        {
            case "toggle": SetBinding(binding, ToggleKeyCombo, ToggleCtrlCheck, ToggleShiftCheck, ToggleAltCheck); break;
            case "stop": SetBinding(binding, StopKeyCombo, StopCtrlCheck, StopShiftCheck, StopAltCheck); break;
            case "diagnostic": SetBinding(binding, DiagnosticKeyCombo, DiagnosticCtrlCheck, DiagnosticShiftCheck, DiagnosticAltCheck); break;
            case "drag": SetBinding(binding, DragKeyCombo, DragCtrlCheck, DragShiftCheck, DragAltCheck); break;
        }
        recordingBinding = null;
        FooterText.Text = $"已录入 {binding.DisplayName}，尚未保存。";
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Filter = "EndfieldJiggle 配置 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 64 * 1024) throw new InvalidDataException("配置文件过大。");
            SetControls(SettingsProfile.Import(File.ReadAllText(dialog.FileName)));
            FooterText.Text = "配置已导入，尚未保存到 Mod。";
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = SettingsProfile.Export(ReadControls());
            SaveFileDialog dialog = new() { Filter = "EndfieldJiggle 配置 (*.json)|*.json", FileName = "EndfieldJiggle-profile.json" };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
            FooterText.Text = "配置已导出：" + dialog.FileName;
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    private void UpdateReloadBinding()
    {
        EfmiInputStatus input = EfmiInputDiagnostics.Read(EfmiPathBox.Text);
        reloadBinding = input.Reload;
        ReloadBindingText.Text = $"配置重载：{input.Reload ?? "未配置"}\n" +
            $"EFMI 所有 Mod 总开关（Mods Display）：{input.ModsDisplay ?? "未找到绑定"}\n" +
            $"临时显示原版（show_original）：{input.ShowOriginal ?? "未配置"}";
    }

    private void ValidateHotSettingsTarget()
    {
        if (store is null) throw new InvalidOperationException("未选择已安装的触摸 Mod。");
        string efmi = InstallablePackage.ResolveEfmiDirectory(store.Root);
        if (!string.Equals(efmi, EfmiPathBox.Text, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前运行时与所选 EFMI 不一致。");
        UpdateReloadBinding();
        if (reloadBinding is null)
            throw new InvalidOperationException("EFMI 没有配置 reload_config。取消重载模式并完整退出游戏后保存。");
    }

    private string ReloadNextStep() => "写入已结束。回到游戏按重载键：" + (reloadBinding ?? "F10") + "。";

    private async Task<IReadOnlyList<KeyConflict>> CheckConflictsAsync(TouchSettings settings,
        CancellationToken token = default)
    {
        if (store is null || string.IsNullOrEmpty(EfmiPathBox.Text)) return [];
        string efmi = EfmiPathBox.Text;
        string runtime = store.Root;
        IReadOnlyList<KeyConflict> conflicts = await Task.Run(
            () => ModLibraryStore.FindConflicts(efmi, runtime, settings, token), token);
        token.ThrowIfCancellationRequested();
        StringBuilder text = new();
        foreach (KeyConflict conflict in conflicts)
        {
            text.AppendLine($"{conflict.Action} ↔ {conflict.Binding}");
            text.AppendLine($"{conflict.SourceFile}:{conflict.SourceLine} [{conflict.Section}]");
            if (conflict.Condition.Length != 0) text.AppendLine(conflict.Condition);
            text.AppendLine();
        }
        ConflictDetailsText.Text = conflicts.Count == 0 ? "未发现潜在冲突。" : text.ToString();
        return conflicts;
    }

    private async void CheckConflicts_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("扫描按键冲突", async token =>
        {
            UpdateReloadBinding();
            IReadOnlyList<KeyConflict> conflicts = await CheckConflictsAsync(ReadControls(), token);
            SetStatus($"按键扫描完成：{conflicts.Count} 项潜在冲突。", conflicts.Count == 0);
        });
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("检查现场状态", async token =>
        {
            DiagnosticSnapshot diagnostics = await CaptureCurrentDiagnosticsAsync(token);
            UpdateDiagnosticOverview(diagnostics);
            SetStatus("现场诊断已刷新。", true);
        });
    }

    private Task<DiagnosticSnapshot> CaptureCurrentDiagnosticsAsync(CancellationToken token)
    {
        string? efmi = string.IsNullOrWhiteSpace(EfmiPathBox.Text) ? null : EfmiPathBox.Text;
        string? runtime = store?.Root;
        string? outfit = string.IsNullOrWhiteSpace(OutfitPathBox.Text) ? null : OutfitPathBox.Text;
        string log = Path.Combine(Path.GetDirectoryName(WorkspacePath)!, "configurator.log");
        return Task.Run(() => DiagnosticSnapshotCollector.Capture(efmi, runtime, outfit, log, token), token);
    }

    private void UpdateDiagnosticOverview(DiagnosticSnapshot diagnostics)
    {
        IReadOnlyList<DiagnosticFile>? files = diagnostics.Files.Value;
        int missing = files?.Count(file => !file.Present && file.Error is null) ?? 0;
        int changed = files?.Count(file => file.MatchesReceipt == false) ?? 0;
        int unreadable = files?.Count(file => file.Error is not null) ?? 0;
        string layout = diagnostics.Layout.Value is { } found
            ? $"{found.Candidates.Count} 个触摸目录" +
              (found.Candidates.Any(item => item.Problem is not null) ? "，存在布局问题" : "")
            : diagnostics.Layout.Error ?? "未检查";
        string guard = diagnostics.InputGuard.Value is { } input
            ? $"{input.Games.Count} 个游戏进程，{input.Games.Count(game => game.Ready)} 个守护会话就绪"
            : diagnostics.InputGuard.Error ?? "未检查";
        string outfit = diagnostics.Outfit.Value is { } inspected
            ? $"支持 {inspected.Supported} / 未支持 {inspected.Unsupported}，必要资源缺失 {inspected.MissingRequired.Count}"
            : diagnostics.Outfit.Error ?? "未检查";
        string qaqm = diagnostics.Qaqm.Value is { Applicable: false } ? "不适用" :
            diagnostics.Qaqm.Value is { Applicable: true, Plan: { } plan }
                ? $"{plan.Issues.Count} 项未解决" :
            diagnostics.Qaqm.Value?.Note ?? diagnostics.Qaqm.Error ?? "未检查";
        DiagnosticOverviewText.Text =
            $"目录：{layout}\n文件：{(files is null ? diagnostics.Files.Error ?? "未检查" :
                $"{files.Count} 项，缺失 {missing} / 与安装记录不符 {changed} / 无法读取 {unreadable}")}\n" +
            $"InputGuard：{guard}\n换装：{outfit} · QAQM：{qaqm}";
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new() { Filter = "诊断报告 (*.json)|*.json", FileName = "EndfieldJiggle-diagnostics.json" };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync("生成诊断报告", async token =>
        {
            string json = await BuildDiagnosticsReportAsync(token);
            File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
            FooterText.Text = "报告已导出到本机，未上传：" + dialog.FileName;
        });
    }

    private async Task<string> BuildDiagnosticsReportAsync(CancellationToken token)
    {
        DiagnosticSection<TouchSettings> pending;
        try { pending = new(ReadControls(), null); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            pending = new(null, error.GetType().Name + ": " + error.Message);
        }
        string? efmi = string.IsNullOrWhiteSpace(EfmiPathBox.Text) ? null : EfmiPathBox.Text;
        string? runtime = store?.Root;
        string? outfit = string.IsNullOrWhiteSpace(OutfitPathBox.Text) ? null : OutfitPathBox.Text;
        DiagnosticSnapshot diagnostics = await CaptureCurrentDiagnosticsAsync(token);
        UpdateDiagnosticOverview(diagnostics);
        DiagnosticSection<IReadOnlyList<KeyConflict>> conflicts;
        if (store is null || pending.Value is null)
            conflicts = new(null, "未选择有效运行时或待保存按键。");
        else
        {
            try { conflicts = new(await CheckConflictsAsync(pending.Value, token), null); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                conflicts = new(null, error.GetType().Name + ": " + error.Message);
            }
        }
        token.ThrowIfCancellationRequested();
        return JsonSerializer.Serialize(new
        {
            schema = 2, version = InstallablePackage.ApplicationVersion,
            runtime, efmi, outfit, diagnostics.CapturedAtUtc,
            system = new { os = Environment.OSVersion.VersionString,
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() },
            pendingSettings = pending, conflicts, diagnostics,
            inGameVerified = false,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private async Task RunOperationAsync(string name, Func<CancellationToken, Task> operation, bool writing = false)
    {
        if (operationBusy) return;
        operationBusy = true;
        writeInProgress = writing;
        using CancellationTokenSource cancellation = new();
        operationCancellation = cancellation;
        MainTabs.IsEnabled = OpenWorkspaceButton.IsEnabled = HotReloadCheck.IsEnabled = false;
        BusyStrip.Visibility = Visibility.Visible;
        CancelOperationButton.IsEnabled = !writing;
        BusyText.Text = name;
        Stopwatch timer = Stopwatch.StartNew();
        WriteOperationLog(name + " 开始", TimeSpan.Zero, null);
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { SetStatus("已取消，不写入文件。", false); }
        catch (Exception error)
        {
            SetStatus(error.Message, false);
            WriteOperationLog(name, timer.Elapsed, error);
        }
        finally
        {
            WriteOperationLog(name, timer.Elapsed, null);
            operationCancellation = null;
            operationBusy = writeInProgress = false;
            MainTabs.IsEnabled = OpenWorkspaceButton.IsEnabled = HotReloadCheck.IsEnabled = true;
            BusyStrip.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e) => operationCancellation?.Cancel();

    private static void WriteOperationLog(string name, TimeSpan elapsed, Exception? error)
    {
        if (Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal)) return;
        try
        {
            string directory = Path.GetDirectoryName(WorkspacePath)!;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "configurator.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {name} {elapsed.TotalMilliseconds:0}ms" +
                (error is null ? "" : " " + error) + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
