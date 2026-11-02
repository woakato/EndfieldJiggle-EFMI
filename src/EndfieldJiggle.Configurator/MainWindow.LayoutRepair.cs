using System.IO;
using System.Text;
using System.Windows;
using EndfieldJiggle.Configurator.Core;
using Microsoft.Win32;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow
{
    private async void RepairLayout_Click(object sender, RoutedEventArgs e)
    {
        if (package?.Available != true) return;
        OpenFolderDialog dialog = new()
        {
            Title = "选择要保留并补齐的触摸目录",
            Multiselect = false,
            InitialDirectory = store?.Root ?? (string.IsNullOrEmpty(EfmiPathBox.Text)
                ? null : Path.Combine(EfmiPathBox.Text, "Mods")),
        };
        if (dialog.ShowDialog(this) != true) return;
        LayoutRepairPreview? preview = null;
        await RunOperationAsync("检查触摸目录", async token =>
            preview = await Task.Run(() => package.PreviewLayoutRepair(dialog.FolderName), token));
        if (preview is null) return;
        StringBuilder summary = new();
        summary.AppendLine("保留并补齐：").AppendLine(preview.RuntimeDirectory);
        summary.AppendLine().AppendLine($"备份并移出加载范围的旧 INI：{preview.QuarantineFiles.Length}");
        foreach (string path in preview.QuarantineFiles)
            summary.AppendLine(Path.GetRelativePath(preview.EfmiDirectory, path));
        summary.AppendLine().AppendLine($"待补齐的自有文件：{preview.MissingFiles.Length}");
        foreach (string path in preview.MissingFiles) summary.AppendLine(path);
        summary.AppendLine().Append("请退出游戏和 XXMI 后执行修复。");
        if (MessageBox.Show(this, summary.ToString(), "修复目录预览",
            MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await RunOperationAsync("修复触摸目录", async token =>
        {
            LayoutRepairResult result = await Task.Run(() => package.RepairLayout(preview));
            await OpenWorkspaceAsync(preview.EfmiDirectory, token);
            InstallResultText.Text = "目录已修复，已保留按键和强度。\n旧入口备份：" +
                result.LayoutBackupDirectory + "\n运行文件备份：" + result.Installation.BackupDirectory;
            MainTabs.SelectedItem = InstallationTab;
            SetStatus("触摸目录已修复，请重新启动游戏。", true);
        }, writing: true);
    }
}
