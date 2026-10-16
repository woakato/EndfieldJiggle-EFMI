using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using EndfieldJiggle.Launcher.Core;

namespace EndfieldJiggle.Launcher;

public partial class MainWindow : Window
{
    private readonly StandalonePackage package = new(AppContext.BaseDirectory);
    private readonly GameLocationStore settings = new(AppContext.BaseDirectory);
    private CancellationTokenSource? launchCancellation;
    private bool ready;
    private bool closeAfterCancellation;
    private bool busy;

    public MainWindow() => InitializeComponent();

    public async Task InitializeAsync(bool autoLaunch)
    {
        SetBusy(true);
        try
        {
            StandaloneManifest manifest = await Task.Run(package.Validate);
            ready = true;
            VersionText.Text = $"EFMI {manifest.EfmiVersion} · XXMI 渲染库 {manifest.LibrariesVersion}";
            string? remembered = await Task.Run(settings.Read);
            GameLocation? found = null;
            if (remembered is not null) GamePathBox.Text = remembered;
            else
            {
                found = await Task.Run(() => GameLocationStore.Discover(DiscoveryRoots()));
                if (found is not null) GamePathBox.Text = found.Executable;
            }
            StatusText.Text = string.IsNullOrWhiteSpace(GamePathBox.Text)
                ? "点击“选择游戏”，找到游戏安装目录中的 Endfield.exe。"
                : "校验通过。点击“等待官方启动”，再从官方启动器开始游戏。";
        }
        catch (Exception error) { Report(error); }
        finally { SetBusy(false); }
        if (autoLaunch && ready && !string.IsNullOrWhiteSpace(GamePathBox.Text))
            await LaunchAsync();
    }

    private static IEnumerable<string> DiscoveryRoots()
    {
        List<string> roots = [Environment.CurrentDirectory, AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GRYPHLINK"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hypergryph Launcher"),
            @"D:\GRYPHLINK", @"D:\Hypergryph Launcher"];
        using RegistryKey? uninstall = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is not null)
        {
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using RegistryKey? item = uninstall.OpenSubKey(name);
                string display = item?.GetValue("DisplayName") as string ?? "";
                if (!display.Contains("GRYPH", StringComparison.OrdinalIgnoreCase) &&
                    !display.Contains("Endfield", StringComparison.OrdinalIgnoreCase) &&
                    !display.Contains("终末地", StringComparison.Ordinal))
                    continue;
                if (item?.GetValue("InstallLocation") is string path) roots.Add(path);
            }
        }
        return roots;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Filter = "终末地游戏本体 (Endfield.exe)|Endfield.exe",
            Title = "选择 Endfield.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        GamePathBox.Text = dialog.FileName;
        StatusText.Text = "已选择游戏。点击“等待官方启动”，再从官方启动器开始游戏。";
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            GameLocation? found = await Task.Run(() => GameLocationStore.Discover(DiscoveryRoots()));
            if (found is null) StatusText.Text = "未找到游戏，请手动选择 Endfield.exe。";
            else { GamePathBox.Text = found.Executable; StatusText.Text = "已找到游戏。"; }
        }
        catch (Exception error) { Report(error); }
        finally { SetBusy(false); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        string selected = GamePathBox.Text;
        SetBusy(true);
        try
        {
            GameLocation game = await Task.Run(() => settings.Save(selected));
            GamePathBox.Text = game.Executable;
            StatusText.Text = "游戏路径已保存。";
        }
        catch (Exception error) { Report(error); }
        finally { SetBusy(false); }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchAsync();

    private async Task LaunchAsync()
    {
        if (busy || !ready) return;
        string selected = GamePathBox.Text;
        SetBusy(true);
        launchCancellation = new();
        CancelButton.Visibility = Visibility.Visible;
        try
        {
            GameLocation game = await Task.Run(() => settings.Save(selected));
            Log("mode=manual-official-launcher; injection=upstream-direct; dx11=user-confirmation-required");
            StatusText.Text = "准备等待官方启动…";
            await Task.Run(() => LaunchSession.Start(package, game.Executable,
                message => Dispatcher.Invoke(() => StatusText.Text = message),
                launchCancellation.Token, Log));
            Log("EFMI loaded");
        }
        catch (OperationCanceledException)
        {
            Log("phase=cancelled");
            StatusText.Text = "已取消等待；不会关闭游戏。若 EFMI 已开始加载，取消不会卸载它。";
        }
        catch (TimeoutException error) { Log("phase=timeout"); Report(error); }
        catch (Exception error) { Log("phase=failed"); Report(error); }
        finally
        {
            launchCancellation.Dispose();
            launchCancellation = null;
            CancelButton.Visibility = Visibility.Collapsed;
            SetBusy(false);
            if (closeAfterCancellation) Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => launchCancellation?.Cancel();

    private void Configure_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(package.ConfiguratorPath)) throw new FileNotFoundException("缺少配置器，请重新解压完整包。");
            Process.Start(new ProcessStartInfo(package.ConfiguratorPath)
                { WorkingDirectory = package.EfmiDirectory, UseShellExecute = true });
            // The existing configurator treats other Endfield-named processes as a running game.
            Close();
        }
        catch (Exception error) { Report(error); }
    }

    private void Mods_Click(object sender, RoutedEventArgs e) => OpenDocument(package.ModsDirectory);
    private void Instructions_Click(object sender, RoutedEventArgs e) =>
        OpenDocument(Path.Combine(package.Root, "INSTALL-STANDALONE-zh-CN.md"));

    private void OpenDocument(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { Report(error); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        GameControls.IsEnabled = SaveButton.IsEnabled = !value;
        LaunchButton.IsEnabled = ConfigureButton.IsEnabled = ModsButton.IsEnabled = !value && ready;
    }

    private void Report(Exception error)
    {
        StatusText.Text = error is Win32Exception windowsError
            ? LaunchSession.DescribeWindowsFailure(windowsError) : error.Message;
        Log(error.ToString());
    }

    private void Log(string message)
    {
        try
        {
            string directory = Path.Combine(package.Root, "Logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "launcher.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (launchCancellation is null) return;
        e.Cancel = true;
        closeAfterCancellation = true;
        launchCancellation.Cancel();
    }

    public async Task RunSmokeAsync(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        StandaloneManifest manifest = await Task.Run(package.Validate);
        using (OfficialInjector injector = new(package.LoaderPath)) { }
        ready = true;
        VersionText.Text = $"EFMI {manifest.EfmiVersion} · XXMI 渲染库 {manifest.LibrariesVersion}";
        GamePathBox.Text = @"D:\游戏\明日方舟终末地\EndField Game\Endfield.exe";
        StatusText.Text = "校验通过。点击“等待官方启动”，再从官方启动器开始游戏。";
        Show();
        foreach ((int width, int height) in new[] { (760, 550), (640, 520) })
        {
            Width = width;
            Height = height;
            SetBusy(false);
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            RenderTargetBitmap bitmap = new((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(Path.Combine(output, $"launcher-{width}x{height}.png"));
            encoder.Save(stream);
        }
        GamePathBox.Clear();
        StatusText.Text = "点击“选择游戏”，找到游戏安装目录中的 Endfield.exe。";
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        RenderTargetBitmap empty = new((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        empty.Render(this);
        PngBitmapEncoder emptyEncoder = new();
        emptyEncoder.Frames.Add(BitmapFrame.Create(empty));
        using (FileStream stream = File.Create(Path.Combine(output, "launcher-first-run.png")))
            emptyEncoder.Save(stream);
        SetBusy(true);
        CancelButton.Visibility = Visibility.Visible;
        GamePathBox.Text = @"D:\游戏\明日方舟终末地\EndField Game\Endfield.exe";
        StatusText.Text = "等待官方启动（最多 5 分钟）。请在官方启动器确认 DirectX 11，再点击开始游戏。";
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        RenderTargetBitmap waiting = new((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        waiting.Render(this);
        PngBitmapEncoder waitingEncoder = new();
        waitingEncoder.Frames.Add(BitmapFrame.Create(waiting));
        using (FileStream stream = File.Create(Path.Combine(output, "launcher-waiting.png")))
            waitingEncoder.Save(stream);
        File.WriteAllText(Path.Combine(output, "smoke-report.json"),
            JsonSerializer.Serialize(new { schema = 1, manifest.Version, manifest.EfmiVersion,
                manifest.LibrariesVersion, officialExportsResolved = true, screenshots = 4,
                launchMode = "manual-official-launcher", startsGameExecutable = false,
                gameStarted = false, injectorCalled = false, liveInstallationModified = false },
                StandalonePackage.JsonOptions));
        Hide();
    }
}
