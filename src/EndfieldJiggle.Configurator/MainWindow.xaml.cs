using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EndfieldJiggle.Configurator.Core;
using Microsoft.Win32;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow : Window
{
    private RuntimeSettingsStore? store;
    private RuntimeSettingsSnapshot? snapshot;
    private bool loading;
    private InstallablePackage? package;
    private InstallationPreview? installation;

    public MainWindow()
    {
        InitializeComponent();
        ToggleKeyCombo.ItemsSource = StopKeyCombo.ItemsSource =
            DiagnosticKeyCombo.ItemsSource = TouchSettings.KeyboardOptions;
        DragKeyCombo.ItemsSource = TouchSettings.DragOptions;
        foreach (ComboBox combo in new[] { ToggleKeyCombo, StopKeyCombo, DiagnosticKeyCombo, DragKeyCombo })
            combo.DisplayMemberPath = nameof(KeyBinding.DisplayName);
        RadiusSlider.ValueChanged += Physics_ValueChanged;
        MaxOffsetSlider.ValueChanged += Physics_ValueChanged;
        DragScaleSlider.ValueChanged += Physics_ValueChanged;
        Closing += (_, e) =>
        {
            if (writeInProgress)
            {
                e.Cancel = true;
                FooterText.Text = "正在写入，完成后可关闭。";
            }
            else operationCancellation?.Cancel();
        };
        Loaded += async (_, _) => await RunOperationAsync("自动识别目录", InitializeWorkspaceAsync);
    }

    private async Task InitializeWorkspaceAsync(CancellationToken token)
    {
        string executableDirectory = AppContext.BaseDirectory;
        string workingDirectory = Environment.CurrentDirectory;
        var detected = await Task.Run(() =>
        {
            InstallablePackage installer = InstallablePackage.FromAssembly(Assembly.GetExecutingAssembly(),
                Environment.ProcessPath ?? throw new IOException("无法定位当前 EXE。"));
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            List<string> launcherDirectories =
                [Path.Combine(localAppData, "XXMI Launcher"), Path.Combine(localAppData, "XXMI")];
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName("XXMI Launcher"))
            {
                using (process)
                {
                    try
                    {
                        string? path = process.MainModule?.FileName;
                        if (path is not null) launcherDirectories.Add(Path.GetDirectoryName(path)!);
                    }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
            }
            RuntimeLocation? location = RuntimeLocationLocator.Discover(executableDirectory, workingDirectory,
                ReadPreviousWorkspace(), launcherDirectories, token);
            return (Installer: installer, Location: location);
        }, token);
        token.ThrowIfCancellationRequested();
        package = detected.Installer;
        if (detected.Location is not null)
        {
            await SelectEfmiAsync(detected.Location, token);
            MainTabs.SelectedItem = store is null ? InstallationTab : LibraryTab;
        }
        else
        {
            SetStatus("未发现已安装运行时，请选择 EFMI、Mods 或触摸 Mod 目录。", false);
            MainTabs.SelectedItem = InstallationTab;
        }
        if (Environment.GetCommandLineArgs().Contains("--install", StringComparer.Ordinal))
            MainTabs.SelectedItem = InstallationTab;
    }

    private async void ChooseRuntime_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "选择触摸 Mod、Mods 或 EFMI 文件夹",
            Multiselect = false,
            InitialDirectory = store?.Root,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await RunOperationAsync("读取目录", token => OpenWorkspaceAsync(dialog.FolderName, token));
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e) =>
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length != 1)
            return;
        string candidate = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]) ?? "";
        await RunOperationAsync("读取拖入目录", async token =>
        {
            bool isRuntime = await Task.Run(() => IsEfmiOrMods(candidate) ||
                RuntimeSettingsStore.FindRuntimeDirectory(candidate) is not null ||
                Path.GetFileName(candidate).StartsWith("EndfieldJiggle", StringComparison.OrdinalIgnoreCase), token);
            if (isRuntime) await OpenWorkspaceAsync(candidate, token);
            else
            {
                if (store is null) await OpenWorkspaceAsync(candidate, token);
                await SelectOutfitAsync(candidate, token);
            }
        });
    }

    private async Task LoadRuntimeAsync(string directory, CancellationToken token = default)
    {
        try
        {
            var loaded = await Task.Run(() =>
            {
                RuntimeSettingsStore selectedStore = new(directory);
                return (Store: selectedStore, Snapshot: selectedStore.Read(),
                    Restore: selectedStore.HasRestorableBackup);
            }, token);
            token.ThrowIfCancellationRequested();
            RuntimeSettingsStore selected = loaded.Store;
            RuntimeSettingsSnapshot current = loaded.Snapshot;
            store = selected;
            snapshot = current;
            SetControls(current.Settings);
            RuntimePathText.Text = selected.Root;
            SetStatus("已读取运行时配置。", true);
            FooterText.Text = "已加载当前配置。";
            RestoreButton.IsEnabled = loaded.Restore;
            ApplyButton.IsEnabled = true;
            UpdateRuntimeSummary(current.Settings);
            MainTabs.SelectedItem = TouchTab;
            outfitStore = null;
            outfitInspection = null;
            AdaptOutfitButton.IsEnabled = RestoreOutfitButton.IsEnabled = false;
            CheckStateButton.IsEnabled = RecoverStateButton.IsEnabled = RestoreStateButton.IsEnabled = false;
            stateStore = null;
            statePlan = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
            SetStatus(error.Message, false);
        }
    }

    private async void ChooseEfmi_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "选择 EFMI 安装目录", Multiselect = false,
            InitialDirectory = string.IsNullOrEmpty(EfmiPathBox.Text) ? null : EfmiPathBox.Text,
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync("读取 EFMI", token => OpenWorkspaceAsync(dialog.FolderName, token));
    }

    private async Task SelectEfmiAsync(RuntimeLocation location, CancellationToken token = default)
    {
        string efmi = location.EfmiDirectory;
        EfmiPathBox.Text = efmi;
        installation = null;
        InstallButton.IsEnabled = false;
        if (location.Nested)
            InstallPreviewText.Text = "检测到多套一层的运行时，已定位实际配置。可改键和适配；安装升级请先整理目录，程序不会自动搬动文件。";
        else if (package?.Available == true)
        {
            try
            {
                installation = await Task.Run(() => package.Preview(efmi), token);
                InstallPreviewText.Text = $"{(installation.IsUpgrade ? "升级已安装版本" : "新安装")} · {installation.FileCount} 个文件\n" +
                    installation.RuntimeDirectory;
                InstallButton.IsEnabled = true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                InstallPreviewText.Text = "安装检查：" + error.Message + "\n读取设置不受此安装检查限制。";
            }
        }
        else InstallPreviewText.Text = "此配置器未内置安装资源。";
        InstallRestoreButton.IsEnabled = InstallablePackage.HasRestorableInstallation(
            location.RuntimeDirectory ?? Path.Combine(efmi, "Mods", "EndfieldJiggleEFMI"));
        string? currentRuntime = location.RuntimeDirectory;
        if (currentRuntime is not null)
        {
            object activeTab = MainTabs.SelectedItem;
            await LoadRuntimeAsync(currentRuntime, token);
            MainTabs.SelectedItem = activeTab;
        }
        else
        {
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
        }
        await RefreshLibraryAsync(token);
        UpdateReloadBinding();
        await Task.Run(() => RememberWorkspace(efmi), token);
        if (location.Nested)
            FooterText.Text = "已自动定位实际运行时：" + currentRuntime + "（目录多套一层，未移动文件）";
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (package is null || installation is null) return;
        InstallationPreview preview = installation;
        if (MessageBox.Show(this,
                $"安装到：\n{preview.RuntimeDirectory}\n\n只安装 EndfieldJiggle 自有文件，备份旧文件并保留可读取的参数。" +
                "\n不修改游戏、注入器或其他 Mod。请先退出游戏和 XXMI。",
                "安装 / 升级", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await RunOperationAsync("安装运行时", async token =>
        {
            InstallationResult result = await Task.Run(() => package.Install(preview));
            InstallResultText.Text = $"安装完成。\n备份：{result.BackupDirectory}\n" +
                (result.SettingsPreserved ? "已保留原来的按键和强度。" : "已使用本版默认配置。");
            await OpenWorkspaceAsync(preview.EfmiDirectory, token);
            SetStatus("0.2.3 已安装。", true);
        }, writing: true);
    }

    private async void InstallRestore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(EfmiPathBox.Text)) return;
        string runtime = store?.Root ?? Path.Combine(EfmiPathBox.Text, "Mods", "EndfieldJiggleEFMI");
        if (MessageBox.Show(this, $"恢复安装前版本：\n{runtime}\n\n请退出游戏和 XXMI。\n" +
                "安装后有修改的文件不会被强行覆盖。", "恢复安装",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await RunOperationAsync("恢复运行时", async token =>
        {
            string backup = await Task.Run(() => InstallablePackage.RestoreInstallation(runtime));
            InstallResultText.Text = "已恢复安装前文件。备份：" + backup;
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
            await OpenWorkspaceAsync(EfmiPathBox.Text, token);
            SetStatus("已恢复安装前版本。", true);
        }, writing: true);
    }

    private void SetControls(TouchSettings settings)
    {
        loading = true;
        try
        {
            StartupEnabledCheck.IsChecked = settings.DefaultEnabled;
            SetBinding(settings.Toggle, ToggleKeyCombo, ToggleCtrlCheck, ToggleShiftCheck, ToggleAltCheck);
            SetBinding(settings.Stop, StopKeyCombo, StopCtrlCheck, StopShiftCheck, StopAltCheck);
            SetBinding(settings.Drag, DragKeyCombo, DragCtrlCheck, DragShiftCheck, DragAltCheck);
            SetBinding(settings.Diagnostic, DiagnosticKeyCombo, DiagnosticCtrlCheck,
                DiagnosticShiftCheck, DiagnosticAltCheck);
            RadiusSlider.Value = (double)settings.Physics.Radius;
            MaxOffsetSlider.Value = (double)settings.Physics.MaxOffset;
            DragScaleSlider.Value = (double)settings.Physics.DragScale;
            UpdatePhysicsLabels();
            PhysicsPresetCombo.SelectedIndex = 0;
        }
        finally { loading = false; }
    }

    private static void SetBinding(KeyBinding binding, ComboBox combo,
        CheckBox ctrl, CheckBox shift, CheckBox alt)
    {
        combo.SelectedItem = ((IEnumerable<KeyBinding>)combo.ItemsSource)
            .FirstOrDefault(option => option.Key == binding.Key);
        ctrl.IsChecked = binding.Ctrl;
        shift.IsChecked = binding.Shift;
        alt.IsChecked = binding.Alt;
    }

    private TouchSettings ReadControls()
    {
        KeyBinding ReadBinding(ComboBox combo, CheckBox ctrl, CheckBox shift, CheckBox alt)
        {
            if (combo.SelectedItem is not KeyBinding option)
                throw new InvalidDataException("请选择一个有效按键。");
            return new(option.Key, ctrl.IsChecked == true, shift.IsChecked == true, alt.IsChecked == true);
        }

        TouchSettings settings = new(
            StartupEnabledCheck.IsChecked == true,
            ReadBinding(ToggleKeyCombo, ToggleCtrlCheck, ToggleShiftCheck, ToggleAltCheck),
            ReadBinding(StopKeyCombo, StopCtrlCheck, StopShiftCheck, StopAltCheck),
            ReadBinding(DiagnosticKeyCombo, DiagnosticCtrlCheck, DiagnosticShiftCheck, DiagnosticAltCheck),
            ReadBinding(DragKeyCombo, DragCtrlCheck, DragShiftCheck, DragAltCheck),
            new PhysicsSettings((decimal)RadiusSlider.Value, (decimal)MaxOffsetSlider.Value,
                (decimal)DragScaleSlider.Value));
        settings.Validate();
        return settings;
    }

    private void Physics_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loading && RadiusValueText is not null)
        {
            UpdatePhysicsLabels();
            PhysicsPresetCombo.SelectedIndex = 0;
        }
    }

    private void UpdatePhysicsLabels()
    {
        RadiusValueText.Text = RadiusSlider.Value.ToString("0.###");
        MaxOffsetValueText.Text = MaxOffsetSlider.Value.ToString("0.###");
        DragScaleValueText.Text = DragScaleSlider.Value.ToString("0.###");
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (store is not null)
        {
            string directory = store.Root;
            await RunOperationAsync("读取设置", token => LoadRuntimeAsync(directory, token));
        }
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        SetControls(new TouchSettings(
            false,
            new KeyBinding("VK_F8", Ctrl: true, Shift: true),
            new KeyBinding("VK_F7", Ctrl: true, Shift: true),
            new KeyBinding("VK_F9", Ctrl: true, Shift: true),
            new KeyBinding("VK_LBUTTON", Shift: true),
            new PhysicsSettings()));
        FooterText.Text = "默认值尚未保存。";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || snapshot is null)
            return;
        await RunOperationAsync("保存配置", async token =>
        {
            TouchSettings settings = ReadControls();
            bool hot = HotReloadCheck.IsChecked == true;
            if (hot) ValidateHotSettingsTarget();
            IReadOnlyList<KeyConflict> conflicts = await CheckConflictsAsync(settings, token);
            string mode = hot
                ? "先松开拖动并关闭触摸。写入期间不要按 F10。\n完成后回到游戏，松开 Ctrl、Shift、Alt，再按重载键。"
                : "继续前请确认游戏和 XXMI 已完全退出。";
            MessageBoxResult answer = MessageBox.Show(
                this,
                $"应用设置到：\n{store.Root}\n\n将只更新 EndfieldJiggle.ini、Passes.ini" +
                $"{(File.Exists(Path.Combine(store.Root, "Outfits.ini")) ? " 和 Outfits.ini" : "")}。" +
                "\n\n" + mode + (conflicts.Count == 0 ? "" :
                    $"\n\n发现 {conflicts.Count} 项潜在按键冲突（不求值第三方条件）。详情在“按键与诊断”。是否仍保存？"),
                "确认应用设置", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (answer != MessageBoxResult.OK)
                return;
            RuntimeSettingsStore selected = store;
            string fingerprint = snapshot.Fingerprint;
            RuntimeApplyResult result = await Task.Run(() => selected.Apply(settings, fingerprint, requireStopped: !hot));
            snapshot = await Task.Run(selected.Read);
            SetControls(snapshot.Settings);
            RestoreButton.IsEnabled = true;
            UpdateRuntimeSummary(snapshot.Settings);
            SetStatus(hot ? "配置已保存。等待游戏内重载。" : "配置已保存，下次启动生效。", true);
            FooterText.Text = result.Changed
                ? (hot ? ReloadNextStep() + " " : "") + $"备份：{result.BackupDirectory}"
                : "配置没有变化，无需写入。";
        }, writing: true);
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (store is null)
            return;
        await RunOperationAsync("撤销配置保存", async _ =>
        {
            bool hot = HotReloadCheck.IsChecked == true;
            if (hot) ValidateHotSettingsTarget();
            MessageBoxResult answer = MessageBox.Show(
                this, "将恢复上次配置前保存的运行参数文件。\n\n" +
                    (hot ? "先关闭触摸并松开拖动。恢复期间不要重载，完成后回游戏按重载键。" :
                        "请先确认游戏和 XXMI 已完全退出。"),
                "恢复上次设置", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK)
                return;
            RuntimeSettingsStore selected = store;
            RuntimeApplyResult result = await Task.Run(() => selected.RestoreLast(requireStopped: !hot));
            snapshot = await Task.Run(selected.Read);
            SetControls(snapshot.Settings);
            UpdateRuntimeSummary(snapshot.Settings);
            RestoreButton.IsEnabled = await Task.Run(() => selected.HasRestorableBackup);
            SetStatus("已恢复上次配置。", true);
            FooterText.Text = (hot ? ReloadNextStep() + " " : "") +
                $"已恢复：{string.Join("、", result.ChangedFiles)}";
        }, writing: true);
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusDot.Fill = success ? new SolidColorBrush(Color.FromRgb(31, 132, 92)) :
            new SolidColorBrush(Color.FromRgb(185, 91, 32));
    }
}
