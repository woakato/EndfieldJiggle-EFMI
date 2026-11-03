# 工具索引

在仓库根目录使用 PowerShell 7。输出保存在 `reports`，各脚本参数见文件开头。

## 当前完整包

| 工具 | 用途 |
| --- | --- |
| [Build-FlatRelease.ps1](Build-FlatRelease.ps1) | 从审核后的运行时报告生成扁平纯 Mod、drop-in，并同步配置器内嵌安装文件 |
| [Build-Configurator.ps1](Build-Configurator.ps1) | 配置器；传入 `RuntimeZipPath` 可嵌入对应运行时 |
| [Build-Standalone.ps1](Build-Standalone.ps1) | 从指定 drop-in 与固定官方依赖组装普通独立版 |
| [Test-ReleaseRegression.ps1](Test-ReleaseRegression.ps1) | 安装包运行文件与换装桥接回归检查 |
| [Test-ManualStandaloneArchive.ps1](Test-ManualStandaloneArchive.ps1) | 独立包归档内容和缓存时间检查 |
| [Test-PublicSource.ps1](Test-PublicSource.ps1) | 源码、PowerShell 语法和模块导入检查 |
| [Test-OutfitCompatibility.ps1](Test-OutfitCompatibility.ps1) | 任意只读换装列表的通用适配、精确恢复及实际 EFMI 隔离加载验证 |
| [Test-RuntimeLocation.ps1](Test-RuntimeLocation.ps1) | 实际 EFMI 加载器验证平铺、分类目录、中文名和多层运行时布局 |

构建 v0.2.3 独立版时，显式指定 `Version`、已核对的 `ExpectedDropInSha256`
和扁平包对应的空 `DropInRuntimePrefix`。参数中的历史默认值不表示当前发布版本。
身份以 [PACKAGE-MAP.md](../docs/PACKAGE-MAP.md) 为准。

## 渲染调查与候选构建

| 工具 | 用途 |
| --- | --- |
| [Build-NativeReceiverCandidate.ps1](Build-NativeReceiverCandidate.ps1) | 原皮通用接收规则候选包 |
| [Build-OutfitReceiverCandidate.ps1](Build-OutfitReceiverCandidate.ps1) | 换装绘制前的通用通道准备 |
| [Test-NativeReceiver.ps1](Test-NativeReceiver.ps1) | 原皮规则、GPU 与加载验证 |
| [Test-OutfitReceiver.ps1](Test-OutfitReceiver.ps1) | 换装绘制的 GPU 控制及负例验证 |
| [Test-ReceiverGeometry.ps1](Test-ReceiverGeometry.ps1) | 原生接口几何检查 |
| [Test-CapturedSkinPass.ps1](Test-CapturedSkinPass.ps1) | 采集模型顶点回放 |
| [Build-ShadingCandidate.ps1](Build-ShadingCandidate.ps1) / [Build-SkinPassCandidate.ps1](Build-SkinPassCandidate.ps1) | 较早的表面方向和精确颜色通道候选构建 |

这些工具依赖本地证据和匹配输入；候选构建的离线检查与实际游戏画面确认分别记录。
模块 `.psm1` 仍与调用脚本放在同一目录，保留现有导入路径。

## 诊断与历史流程

| 工具 | 用途 |
| --- | --- |
| [Build-DiagnosticStandalone.ps1](Build-DiagnosticStandalone.ps1) | 独立的无法触摸诊断包 |
| [Collect-Diagnostics.ps1](Collect-Diagnostics.ps1) | 诊断信息收集 |
| [Test-DiagnosticOffline.ps1](Test-DiagnosticOffline.ps1) / [Test-DiagnosticCollector.ps1](Test-DiagnosticCollector.ps1) | 诊断包离线检查与收集器验证 |
| [Export-QaqmStateRepair.ps1](Export-QaqmStateRepair.ps1) | 状态修复导出 |
| [Build-Installable.ps1](Build-Installable.ps1) / [Build-Complete.ps1](Build-Complete.ps1) / [Export-Release.ps1](Export-Release.ps1) | 较早的安装包构建链，保留其版本和输入约束 |
| [Build-Universal.ps1](Build-Universal.ps1) | 早期原生接口构建，依赖对应的父构建证据 |

配置器、安装、定位、换装、启动器等 C# 检查分别位于 `tests/EndfieldJiggle.*.Tests`。
历史流程按对应版本使用，不作为当前三个完整包的默认构建入口。
