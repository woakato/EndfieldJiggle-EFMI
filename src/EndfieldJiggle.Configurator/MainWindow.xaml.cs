using System.IO;
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

    public MainWindow()
    {
        InitializeComponent();
        ToggleKeyCombo.ItemsSource = StopKeyCombo.ItemsSource =
            DiagnosticKeyCombo.ItemsSource = TouchSettings.KeyboardOptions;
        DragKeyCombo.ItemsSource = TouchSettings.DragOptions;
        RadiusSlider.ValueChanged += Physics_ValueChanged;
        MaxOffsetSlider.ValueChanged += Physics_ValueChanged;
        DragScaleSlider.ValueChanged += Physics_ValueChanged;
        Loaded += (_, _) =>
        {
            string? runtime = RuntimeSettingsStore.FindRuntimeDirectory(AppContext.BaseDirectory);
            if (runtime is not null)
                LoadRuntime(runtime);
            else
                SetStatus("请选择已安装的 Mods/EndfieldJiggleEFMI 文件夹。", false);
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
            LoadRuntime(dialog.FolderName);
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
            SetStatus("拖入的路径中未找到已安装的 EndfieldJiggle.ini 和 Passes.ini。", false);
            return;
        }
        LoadRuntime(runtime);
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
            SetStatus("已读取兼容运行时。当前配置已写入 Mod 文件，关闭配置器后仍会生效。", true);
            FooterText.Text = "已加载当前配置。";
            RestoreButton.IsEnabled = selected.HasRestorableBackup;
            ApplyButton.IsEnabled = true;
        }
        catch (Exception error)
        {
            store = null;
            snapshot = null;
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = false;
            SetStatus(error.Message, false);
        }
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
