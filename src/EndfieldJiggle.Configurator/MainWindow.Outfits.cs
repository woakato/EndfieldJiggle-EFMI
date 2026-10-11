using System.IO;
using System.ComponentModel;
using System.Text;
using System.Windows;
using EndfieldJiggle.Configurator.Core;
using Microsoft.Win32;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow
{
    private OutfitAdapterStore? outfitStore;
    private OutfitAdapterInspection? outfitInspection;
    private List<DrawRow> drawRows = [];

    private sealed class DrawRow(OutfitAdapterDraw draw, bool alreadyAdapted) : INotifyPropertyChanged
    {
        public OutfitAdapterDraw Draw { get; } = draw;
        public string Section => Draw.Section;
        public string Source => $"{Draw.SourceFile}:{Draw.SourceLine}";
        public string Count => Draw.Count;
        public string State => !Draw.Supported ? Reason(Draw.Reason) : alreadyAdapted ? "已适配 / 原始绘制" : "可适配";
        public bool Selectable => Draw.Supported && !alreadyAdapted;
        private bool enabled = draw.Supported && !alreadyAdapted;
        public bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value && Selectable;
                PropertyChanged?.Invoke(this, new(nameof(Enabled)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private async void ChooseOutfit_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "选择当前 EFMI/Mods 中的换装目录", Multiselect = false,
            InitialDirectory = store is null ? null : Path.GetDirectoryName(store.Root),
        };
        if (dialog.ShowDialog(this) == true)
            await RunOperationAsync("分析换装", token => SelectOutfitAsync(dialog.FolderName, token));
    }

    private async Task SelectOutfitAsync(string directory, CancellationToken token = default)
    {
        MainTabs.SelectedItem = OutfitTab;
        OutfitPathBox.Text = directory;
        OutfitNameText.Text = Path.GetFileName(directory);
        OutfitPreviewImage.Source = libraryRows.FirstOrDefault(row =>
            row.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase))?.Preview;
        drawRows = [];
        DrawGrid.ItemsSource = drawRows;
        outfitStore = null;
        outfitInspection = null;
        AdaptOutfitButton.IsEnabled = RestoreOutfitButton.IsEnabled = InspectOutfitButton.IsEnabled = false;
        CheckStateButton.IsEnabled = RecoverStateButton.IsEnabled = RestoreStateButton.IsEnabled = false;
        try
        {
            if (store is null) throw new InvalidOperationException("请先安装或选择当前 EFMI 中的触摸 Mod。");
            _ = InstallablePackage.ResolveEfmiDirectory(store.Root);
            string runtime = store.Root;
            outfitStore = await Task.Run(() => new OutfitAdapterStore(directory, runtime), token);
            token.ThrowIfCancellationRequested();
            InspectOutfitButton.IsEnabled = CheckStateButton.IsEnabled = true;
            await InspectOutfitAsync(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    private async void InspectOutfit_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("分析换装", InspectOutfitAsync);
    }

    private async Task InspectOutfitAsync(CancellationToken token = default)
    {
        if (outfitStore is null) return;
        outfitInspection = null;
        AdaptOutfitButton.IsEnabled = false;
        OutfitAdapterStore selected = outfitStore;
        var inspected = await Task.Run(() => (Inspection: selected.Inspect(), Restore: selected.HasRestorableBackup), token);
        token.ThrowIfCancellationRequested();
        OutfitAdapterInspection inspection = inspected.Inspection;
        outfitInspection = inspection;
        drawRows = inspection.Draws.Select(draw => new DrawRow(draw, inspection.AlreadyAdapted)).ToList();
        foreach (DrawRow row in drawRows)
            row.PropertyChanged += (_, _) => UpdateAdaptButton();
        FilterDraws();
        RestoreOutfitButton.IsEnabled = inspected.Restore;
        AdaptOutfitButton.IsEnabled = !inspection.AlreadyAdapted &&
            inspection.SupportedDraws.Count > 0 && inspection.MissingRequiredResources.Count == 0;
        OutfitSummaryText.Text = inspection.AlreadyAdapted ? "已包含触摸包装器，不重复适配。" :
            $"可适配 {inspection.SupportedDraws.Count} 项 · 未支持 {inspection.UnsupportedDraws.Count} 项" +
            $" · 缺失必要资源 {inspection.MissingRequiredResources.Count} 项";
        StringBuilder text = new();
        foreach (OutfitAdapterDraw draw in inspection.Draws)
        {
            text.Append(draw.Supported ? "[支持] " : "[未支持] ")
                .Append(draw.SourceFile).Append(':').Append(draw.SourceLine)
                .Append(" [").Append(draw.Section).Append(']').AppendLine();
            text.AppendLine(draw.Command.Trim());
            if (!draw.Supported) text.AppendLine(Reason(draw.Reason));
            text.AppendLine();
        }
        foreach (string missing in inspection.MissingRequiredResources) text.AppendLine("[必要资源缺失] " + missing);
        foreach (string missing in inspection.MissingUnreferencedResources) text.AppendLine("[未引用资源缺失] " + missing);
        OutfitDetailsText.Text = text.ToString();
        SetStatus("换装分析完成。", true);
        FooterText.Text = outfitStore.Root;
    }

    private void DrawFilter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => FilterDraws();

    private void FilterDraws()
    {
        if (DrawGrid is null || DrawSearchBox is null) return;
        string query = DrawSearchBox.Text.Trim();
        DrawGrid.ItemsSource = drawRows.Where(row => query.Length == 0 ||
            row.Section.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Draw.Branch.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void UpdateAdaptButton() => AdaptOutfitButton.IsEnabled =
        outfitInspection is { AlreadyAdapted: false, MissingRequiredResources.Count: 0 } &&
        drawRows.Any(row => row.Enabled);

    private void SelectAllDraws_Click(object sender, RoutedEventArgs e)
    {
        foreach (DrawRow row in drawRows) row.Enabled = true;
        UpdateAdaptButton();
    }

    private void SelectNoDraws_Click(object sender, RoutedEventArgs e)
    {
        foreach (DrawRow row in drawRows) row.Enabled = false;
        UpdateAdaptButton();
    }

    private async void AdaptOutfit_Click(object sender, RoutedEventArgs e)
    {
        if (outfitStore is null || outfitInspection is null) return;
        bool hot = HotReloadCheck.IsChecked == true;
        if (!ConfirmOutfitWrite("原目录适配", hot)) return;
        OutfitAdapterStore selected = outfitStore;
        string fingerprint = outfitInspection.Fingerprint;
        string[] selectedDraws = drawRows.Where(row => row.Enabled).Select(row => row.Draw.Id).ToArray();
        await RunOperationAsync("适配换装", async token =>
        {
            // Resolve broken third-party state before changing rendering wrappers.
            if (!await StateAllowsAdaptationAsync(token)) return;
            await Task.Run(() => selected.Apply(fingerprint, selectedDraws, requireStopped: !hot));
            await InspectOutfitAsync(token);
            await RefreshLibraryAsync(token);
            SetStatus("原目录适配完成。模型与纹理未复制。", true);
            FooterText.Text = hot ? "写入已结束。回到游戏按 F10 重载，再开启触摸。" :
                "适配已保存。启动游戏后由 EFMI 加载，不需要保持此程序运行。";
        }, writing: true);
    }

    private async void RestoreOutfit_Click(object sender, RoutedEventArgs e)
    {
        if (outfitStore is null) return;
        bool hot = HotReloadCheck.IsChecked == true;
        if (!ConfirmOutfitWrite("恢复原换装", hot)) return;
        OutfitAdapterStore selected = outfitStore;
        await RunOperationAsync("恢复换装", async token =>
        {
            await Task.Run(() => selected.Restore(requireStopped: !hot));
            await InspectOutfitAsync(token);
            await RefreshLibraryAsync(token);
            SetStatus("原换装 INI 已恢复。", true);
            FooterText.Text = hot ? "写入已结束。回到游戏按 F10 重载。" : "恢复后可重新启动游戏。";
        }, writing: true);
    }

    private bool ConfirmOutfitWrite(string operation, bool hot)
    {
        string mode = hot
            ? "请在游戏中先关闭触摸，切换到其他角色，使被修改角色离开画面，并关闭 XXMI 启动器。\n" +
              "写入期间不要按 F10；完成后再回游戏重载。\n" +
              "此操作不会发送按键。F10 后若模型消失，请完整退出重启。"
            : "请先退出游戏和 XXMI。";
        return MessageBox.Show(this, $"{operation}：\n{outfitStore?.Root}\n\n" +
            "只修改所选换装的 INI，保存可撤销备份，不生成第二份换装 Mod。\n\n" + mode,
            operation, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private static string Reason(string reason) => reason switch
    {
        "custom-vertex-stage" => "自定义顶点或几何着色阶段，不能套用原生触摸。",
        "unsupported-draw-command" => "不是受支持的直接索引绘制命令。",
        "explicit-index-range-required" => "没有明确的索引范围。",
        "unsupported-range-or-instance-expression" => "动态范围或实例表达式不受支持。",
        "range-exceeds-exact-ini-integer" => "范围超过加载器浮点变量可精确表示的整数。",
        "empty-or-short-draw" => "绘制不足一个三角形。",
        "non-mesh-section" => "不是模型绘制命令段。",
        _ => reason,
    };
}
