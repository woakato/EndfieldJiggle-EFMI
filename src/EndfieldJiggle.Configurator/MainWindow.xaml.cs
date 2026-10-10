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
        Loaded += (_, _) =>
        {
            try
            {
                package = InstallablePackage.FromAssembly(Assembly.GetExecutingAssembly(),
                    Environment.ProcessPath ?? throw new IOException("无法定位当前 EXE。"));
            }
            catch (Exception error) { InstallResultText.Text = error.Message; }
            string? runtime = RuntimeSettingsStore.FindRuntimeDirectory(AppContext.BaseDirectory);
            if (runtime is not null)
            {
                try
                {
                    string efmi = InstallablePackage.ResolveEfmiDirectory(runtime);
                    LoadRuntime(runtime);
                    SelectEfmi(efmi);
                }
                catch (DirectoryNotFoundException)
                {
                    SetStatus(package?.Available == true ? "完整安装包已就绪。" : "尚未读取已安装 Mod。", false);
                    MainTabs.SelectedItem = InstallationTab;
                }
            }
            else
                SetStatus(package?.Available == true ? "完整安装包已就绪。" : "尚未读取已安装 Mod。", false);
            if (Environment.GetCommandLineArgs().Contains("--install", StringComparer.Ordinal))
                MainTabs.SelectedItem = InstallationTab;
        };
    }

    private void ChooseRuntime_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "选择 Mods/EndfieldJiggleEFMI 文件夹",
            Multiselect = false,
            InitialDirectory = store?.Root,
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                SelectEfmi(InstallablePackage.ResolveEfmiDirectory(dialog.FolderName));
                LoadRuntime(dialog.FolderName);
            }
            catch (Exception error) { SetStatus(error.Message, false); }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e) =>
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length != 1)
            return;
        string candidate = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]) ?? "";
        string? runtime = RuntimeSettingsStore.FindRuntimeDirectory(candidate);
        if (runtime is null)
        {
            SelectOutfit(candidate);
            return;
        }
        try
        {
            SelectEfmi(InstallablePackage.ResolveEfmiDirectory(runtime));
            LoadRuntime(runtime);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    private void LoadRuntime(string directory)
    {
        try
        {
            RuntimeSettingsStore selected = new(directory);
            RuntimeSettingsSnapshot current = selected.Read();
            store = selected;
            snapshot = current;
            SetControls(current.Settings);
            RuntimePathText.Text = selected.Root;
            SetStatus("已读取运行时配置。", true);
            FooterText.Text = "已加载当前配置。";
            RestoreButton.IsEnabled = selected.HasRestorableBackup;
            ApplyButton.IsEnabled = true;
            MainTabs.SelectedItem = TouchTab;
            if (!string.IsNullOrEmpty(OutfitPathBox.Text))
                SelectOutfit(OutfitPathBox.Text);
        }
        catch (Exception error)
        {
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
            SetStatus(error.Message, false);
        }
    }

    private void ChooseEfmi_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "选择 EFMI 安装目录", Multiselect = false,
            InitialDirectory = string.IsNullOrEmpty(EfmiPathBox.Text) ? null : EfmiPathBox.Text,
        };
        if (dialog.ShowDialog(this) != true) return;
        try { SelectEfmi(dialog.FolderName); }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    private void SelectEfmi(string selected)
    {
        string efmi = InstallablePackage.ResolveEfmiDirectory(selected);
        EfmiPathBox.Text = efmi;
        installation = null;
        InstallButton.IsEnabled = false;
        if (package?.Available == true)
        {
            installation = package.Preview(efmi);
            InstallPreviewText.Text = $"{(installation.IsUpgrade ? "升级已安装版本" : "新安装")} · {installation.FileCount} 个文件\n" +
                installation.RuntimeDirectory;
            InstallButton.IsEnabled = true;
        }
        else InstallPreviewText.Text = "此配置器未内置安装资源。";
        InstallRestoreButton.IsEnabled = InstallablePackage.HasRestorableInstallation(
            Path.Combine(efmi, "Mods", "EndfieldJiggleEFMI"));
        string currentRuntime = Path.Combine(efmi, "Mods", "EndfieldJiggleEFMI");
        if (File.Exists(Path.Combine(currentRuntime, "EndfieldJiggle.ini")) &&
            File.Exists(Path.Combine(currentRuntime, "Passes.ini")) &&
            !string.Equals(store?.Root, currentRuntime, StringComparison.OrdinalIgnoreCase))
        {
            object activeTab = MainTabs.SelectedItem;
            LoadRuntime(currentRuntime);
            MainTabs.SelectedItem = activeTab;
        }
        else if (!Directory.Exists(currentRuntime))
        {
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (package is null || installation is null) return;
        InstallationPreview preview = installation;
        if (MessageBox.Show(this,
                $"安装到：\n{preview.RuntimeDirectory}\n\n只安装 EndfieldJiggle 自有文件，备份旧文件并保留可读取的参数。" +
                "\n不修改游戏、注入器或其他 Mod。请先退出游戏和 XXMI。",
                "安装 / 升级", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        IsEnabled = false;
        try
        {
            InstallationResult result = await Task.Run(() => package.Install(preview));
            InstallResultText.Text = $"安装完成。\n备份：{result.BackupDirectory}\n" +
                (result.SettingsPreserved ? "已保留原来的按键和强度。" : "已使用本版默认配置。");
            LoadRuntime(result.RuntimeDirectory);
            SelectEfmi(preview.EfmiDirectory);
            SetStatus("0.2.1 已安装。", true);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
        finally { IsEnabled = true; }
    }

    private async void InstallRestore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(EfmiPathBox.Text)) return;
        string runtime = Path.Combine(EfmiPathBox.Text, "Mods", "EndfieldJiggleEFMI");
        if (MessageBox.Show(this, $"恢复安装前版本：\n{runtime}\n\n请退出游戏和 XXMI。\n" +
                "安装后有修改的文件不会被强行覆盖。", "恢复安装",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        IsEnabled = false;
        try
        {
            string backup = await Task.Run(() => InstallablePackage.RestoreInstallation(runtime));
            InstallResultText.Text = "已恢复安装前文件。备份：" + backup;
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
            SelectEfmi(EfmiPathBox.Text);
            SetStatus("已恢复安装前版本。", true);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
        finally { IsEnabled = true; }
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
            UpdatePhysicsLabels();
    }

    private void UpdatePhysicsLabels()
    {
        RadiusValueText.Text = RadiusSlider.Value.ToString("0.###");
        MaxOffsetValueText.Text = MaxOffsetSlider.Value.ToString("0.###");
        DragScaleValueText.Text = DragScaleSlider.Value.ToString("0.###");
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (store is not null)
            LoadRuntime(store.Root);
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
        FooterText.Text = "显示的是此 Mod 版本的默认值；按“应用设置”后才写入。";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || snapshot is null)
            return;
        try
        {
            TouchSettings settings = ReadControls();
            MessageBoxResult answer = MessageBox.Show(
                this,
                $"应用设置到：\n{store.Root}\n\n将只更新 EndfieldJiggle.ini、Passes.ini" +
                $"{(File.Exists(Path.Combine(store.Root, "Outfits.ini")) ? " 和 Outfits.ini" : "")}。" +
                "\n\n继续前请确认游戏和 XXMI 已完全退出。",
                "确认应用设置", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (answer != MessageBoxResult.OK)
                return;
            RuntimeApplyResult result = store.Apply(settings, snapshot.Fingerprint);
            snapshot = store.Read();
            SetControls(snapshot.Settings);
            RestoreButton.IsEnabled = true;
            SetStatus("配置已保存到 Mod。下次从 XXMI 启动游戏时会直接使用这些参数。", true);
            FooterText.Text = result.Changed
                ? $"已更新 {string.Join("、", result.ChangedFiles)}。备份：{result.BackupDirectory}"
                : "配置没有变化，无需写入。";
        }
        catch (Exception error)
        {
            SetStatus(error.Message, false);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (store is null)
            return;
        try
        {
            MessageBoxResult answer = MessageBox.Show(
                this, "将恢复上次配置前保存的运行参数文件。\n\n请先确认游戏和 XXMI 已完全退出。",
                "恢复上次设置", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK)
                return;
            RuntimeApplyResult result = store.RestoreLast();
            snapshot = store.Read();
            SetControls(snapshot.Settings);
            RestoreButton.IsEnabled = store.HasRestorableBackup;
            SetStatus("已恢复上次配置。", true);
            FooterText.Text = $"已恢复：{string.Join("、", result.ChangedFiles)}";
        }
        catch (Exception error)
        {
            SetStatus(error.Message, false);
        }
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusDot.Fill = success ? new SolidColorBrush(Color.FromRgb(31, 132, 92)) :
            new SolidColorBrush(Color.FromRgb(185, 91, 32));
    }
}
