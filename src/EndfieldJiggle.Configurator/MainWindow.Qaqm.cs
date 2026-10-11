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
            details.AppendLine($"[可恢复] {host.FileName} · {host.Variables.Count} 个变量");
        foreach (QaqmLoadedHost host in plan.LoadedHosts)
            details.AppendLine($"[已加载] Bridge_{host.Id}\n{host.Path}");
        foreach (QaqmRecoveryIssue issue in plan.Issues)
            details.AppendLine($"[未解决] {issue.Code}\n{issue.Message}");
        if (plan.MissingHosts.Count == 0 && plan.Issues.Count == 0)
            details.AppendLine("未发现缺失的状态声明。");
        OutfitDetailsText.Text = details.ToString();
        return plan;
    }

    private async Task<bool> StateAllowsAdaptationAsync(CancellationToken token)
    {
        QaqmRecoveryPlan plan = await InspectStateAsync(token);
        if (plan.Issues.Count > 0 || plan.MissingHosts.Count > 0)
        {
            SetStatus(plan.Issues.Count > 0 ? "状态依赖未解决，暂未修改换装。详见检查结果。" :
                "先退出游戏补齐缺失的 QAQM 状态，再进行绘制适配。", false);
            return false;
        }
        return true;
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
        if (MessageBox.Show(this, "请先退出游戏和 XXMI。\n\n仅从这套换装自己的 .qaqm-persistbak " +
                "恢复已证实的缺失声明，不修改 UI、换装绘制或全局配置。\n已加载的 Bridge 不会重复生成。",
                "补齐缺失状态", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await RunOperationAsync("恢复缺失状态", async token =>
        {
            QaqmRecoveryResult result = await Task.Run(() => selected.Apply(fingerprint));
            await InspectOutfitAsync(token);
            await InspectStateAsync(token);
            SetStatus(result.Changed ? "缺失状态已恢复。" : "没有需要恢复的状态。", true);
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
