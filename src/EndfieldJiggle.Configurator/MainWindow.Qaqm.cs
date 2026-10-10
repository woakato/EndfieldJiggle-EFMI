using System.Text;
using System.Windows;
using EndfieldJiggle.Configurator.Core;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow
{
    private QaqmRecoveryStore? stateStore;
    private QaqmRecoveryPlan? statePlan;

    private QaqmRecoveryPlan InspectState()
    {
        stateStore = null;
        statePlan = null;
        RecoverStateButton.IsEnabled = RestoreStateButton.IsEnabled = false;
        if (store is null || outfitStore is null)
            throw new InvalidOperationException("请先选择运行时和换装目录。");
        QaqmRecoveryStore selected = new(outfitStore.Root,
            InstallablePackage.ResolveEfmiDirectory(store.Root));
        QaqmRecoveryPlan plan = selected.Inspect();
        stateStore = selected;
        statePlan = plan;
        RecoverStateButton.IsEnabled = plan.CanApply;
        RestoreStateButton.IsEnabled = selected.HasRestorableBackup;
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

    private bool StateAllowsAdaptation()
    {
        QaqmRecoveryPlan plan = InspectState();
        if (plan.Issues.Count > 0 || plan.MissingHosts.Count > 0)
        {
            SetStatus(plan.Issues.Count > 0 ? "状态依赖未解决，暂未修改换装。详见检查结果。" :
                "先退出游戏补齐缺失的 QAQM 状态，再进行绘制适配。", false);
            return false;
        }
        return true;
    }

    private void CheckState_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            QaqmRecoveryPlan plan = InspectState();
            SetStatus(plan.Issues.Count == 0 ? "状态检查完成。" : "状态检查发现未解决项。", plan.Issues.Count == 0);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
    }

    private async void RecoverState_Click(object sender, RoutedEventArgs e)
    {
        if (stateStore is null || statePlan is null || !statePlan.CanApply) return;
        QaqmRecoveryStore selected = stateStore;
        string fingerprint = statePlan.Fingerprint;
        if (MessageBox.Show(this, "请先退出游戏和 XXMI。\n\n仅从这套换装自己的 .qaqm-persistbak " +
                "恢复已证实的缺失声明，不修改 UI、换装绘制或全局配置。\n已加载的 Bridge 不会重复生成。",
                "补齐缺失状态", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        IsEnabled = false;
        try
        {
            QaqmRecoveryResult result = await Task.Run(() => selected.Apply(fingerprint));
            InspectOutfit();
            InspectState();
            SetStatus(result.Changed ? "缺失状态已恢复。" : "没有需要恢复的状态。", true);
            FooterText.Text = result.ReceiptPath ?? "状态未变化。";
        }
        catch (Exception error) { SetStatus(error.Message, false); }
        finally { IsEnabled = true; }
    }

    private async void RestoreState_Click(object sender, RoutedEventArgs e)
    {
        if (stateStore is null) return;
        QaqmRecoveryStore selected = stateStore;
        if (MessageBox.Show(this, "请先退出游戏和 XXMI。\n\n只移除本程序生成且未被改动的状态宿主，" +
                "不会清空 d3dx_user.ini。\n依赖这些声明的换装可能再次出现缺失状态报错。",
                "撤销状态恢复", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        IsEnabled = false;
        try
        {
            await Task.Run(() => selected.Restore());
            InspectOutfit();
            InspectState();
            SetStatus("已撤销本程序的状态恢复。", true);
        }
        catch (Exception error) { SetStatus(error.Message, false); }
        finally { IsEnabled = true; }
    }
}
