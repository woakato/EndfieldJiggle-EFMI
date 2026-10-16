using System.Text;
using System.Windows;
using EndfieldJiggle.Configurator.Core;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow
{
    private QaqmRecoveryStore? stateStore;
    private QaqmRecoveryPlan? statePlan;

    private async Task<QaqmRecoveryPlan> InspectStateAsync(CancellationToken token = default)
    {
        stateStore = null;
        statePlan = null;
        RecoverStateButton.IsEnabled = RestoreStateButton.IsEnabled = false;
        if (store is null || outfitStore is null)
            throw new InvalidOperationException("请先选择运行时和换装目录。");
        if (!outfitStore.IsInEfmiModsTree)
            throw new InvalidOperationException("QAQM 检查只适用于当前 EFMI/Mods 中实际参与加载的换装；外部 Mod 源目录请由原管理器负责部署。");
        string outfit = outfitStore.Root;
        string runtime = store.Root;
        var inspected = await Task.Run(() =>
        {
            QaqmRecoveryStore selectedStore = new(outfit, InstallablePackage.ResolveEfmiDirectory(runtime));
            return (Store: selectedStore, Plan: selectedStore.Inspect(), Restore: selectedStore.HasRestorableBackup);
        }, token);
        token.ThrowIfCancellationRequested();
        QaqmRecoveryStore selected = inspected.Store;
        QaqmRecoveryPlan plan = inspected.Plan;
        stateStore = selected;
        statePlan = plan;
        RecoverStateButton.IsEnabled = plan.CanApply;
        RestoreStateButton.IsEnabled = inspected.Restore;
        StringBuilder details = new("QAQM 状态检查\n\n");
        foreach (QaqmRecoveryHost host in plan.MissingHosts)
        {
            details.AppendLine(host.SourceHostPath is null
                ? $"[可恢复声明] {host.FileName} · {host.Variables.Count} 个变量"
                : $"[可恢复加载] {host.FileName} · {host.Variables.Count} 个变量\n{host.SourceHostPath}");
        }
        foreach (QaqmLoadedHost host in plan.LoadedHosts)
            details.AppendLine($"[已加载] Bridge_{host.Id}\n{host.Path}");
        foreach (QaqmRecoveryIssue issue in plan.Issues)
            details.AppendLine($"[未解决] {issue.Code}\n{issue.Message}");
        if (plan.MissingHosts.Count == 0 && plan.Issues.Count == 0)
            details.AppendLine("未发现缺失的状态声明。");
        OutfitDetailsText.Text = details.ToString();
        return plan;
    }

    private async void CheckState_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("检查状态依赖", async token =>
        {
            QaqmRecoveryPlan plan = await InspectStateAsync(token);
            SetStatus(plan.Issues.Count == 0 ? "状态检查完成。" : "状态检查发现未解决项。", plan.Issues.Count == 0);
        });
    }

    private async void RecoverState_Click(object sender, RoutedEventArgs e)
    {
        if (stateStore is null || statePlan is null || !statePlan.CanApply) return;
        QaqmRecoveryStore selected = stateStore;
        string fingerprint = statePlan.Fingerprint;
        if (MessageBox.Show(this, "请先退出游戏和 XXMI。\n\n优先重新加载现有 QAQM 状态缓存，保留原有选项。" +
                "\n缓存不存在时才从这套换装自己的 .qaqm-persistbak 恢复已证实的声明。" +
                "\n生成文件保存在所选换装目录，已加载的 Bridge 不会重复生成。",
                "修复状态加载", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await RunOperationAsync("恢复缺失状态", async token =>
        {
            QaqmRecoveryResult result = await Task.Run(() => selected.Apply(fingerprint));
            await InspectOutfitAsync(token);
            await InspectStateAsync(token);
            SetStatus(result.Changed ? "状态加载已修复，请完整重启游戏。" : "没有需要恢复的状态。", true);
            FooterText.Text = result.ReceiptPath ?? "状态未变化。";
        }, writing: true);
    }

    private async void RestoreState_Click(object sender, RoutedEventArgs e)
    {
        if (stateStore is null) return;
        QaqmRecoveryStore selected = stateStore;
        if (MessageBox.Show(this, "请先退出游戏和 XXMI。\n\n只移除本程序生成且未被改动的状态宿主，" +
                "不会清空 d3dx_user.ini。\n依赖这些声明的换装可能再次出现缺失状态报错。",
                "撤销状态恢复", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await RunOperationAsync("撤销状态恢复", async token =>
        {
            await Task.Run(() => selected.Restore());
            await InspectOutfitAsync(token);
            await InspectStateAsync(token);
            SetStatus("已撤销本程序的状态恢复。", true);
        }, writing: true);
    }
}
