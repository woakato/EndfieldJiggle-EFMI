# 使用说明

## 先确认下载的是哪一种版本

Windows 使用者可下载 `EndfieldJiggleEFMI-v0.2.1-with-configurator-win64.zip`
（含配置器），或 `EndfieldJiggleEFMI-v0.2.1-win64.zip`（仅 Mod）。
GitHub 自动生成的 `Source code` 归档以及单独标记的 `v0.1.0-source`
都只是源码，不是安装包。

另有可选的 `EndfieldJiggleConfigurator-v0.2.1-win64.zip`。
完整构建的 EXE 内置运行时，可安装、改键和原目录换装适配，不会常驻后台。

## 完整运行时的测试流程

**本节的安装步骤适用于 Release 附带的 Windows x64 Mod ZIP。**

准备条件：

- Windows 版《明日方舟：终末地》与自行取得的 XXMI / EFMI 环境。
- 已备份已有同名 Mod 文件夹。
- 游戏与原版模型在未开启触摸时显示正常。

完整包可双击 `Install.cmd`，在程序中选择 EFMI 目录安装；也支持下述手动安装。
不要照搬 JiggleForge 的 ZZMI 资源或快捷键，不要覆盖 `d3d11.dll`、全局 `ShaderFixes`
或其他 Mod。

### 安装步骤

1. 下载并解压上述 v0.2.1 安装包。
2. 完全退出游戏和 XXMI 启动器。
3. 找到自己 EFMI 包中的 `Mods` 文件夹。
4. 将已有 `Mods/EndfieldJiggleEFMI` 备份到 `Mods` 之外（如果存在）。
5. 将压缩包内的 `Mods/EndfieldJiggleEFMI` 复制到 EFMI 的 `Mods`。
6. 确认路径为 `EFMI/Mods/EndfieldJiggleEFMI/EndfieldJiggle.ini`。
7. 通过 XXMI 启动游戏。触摸默认关闭。

不要覆盖框架 DLL、全局配置、ShaderFixes 或其他 Mod。ZIP 附带的
`INSTALL-zh-CN.md` 有同一套安装和恢复步骤。

### 开启与拖动

1. 通过实际使用的 XXMI / EFMI 启动游戏，进入原版干员总览。
2. 先检查模型外观。运行时默认关闭触摸；此时异常应先排查安装和模组冲突。
3. 按 `Ctrl+Shift+F9` 显示诊断状态。
4. 按 `Ctrl+Shift+F8` 开启触摸。
5. 将鼠标放在角色可见表面，按住 `Shift + 鼠标左键`，短距离拖动。
6. 松开按键，观察回弹。换角色后先松开拖动键，再重新点击表面。
7. 测试结束按 `Ctrl+Shift+F7` 强制关闭；诊断信息可用 `Ctrl+Shift+F9` 隐藏。

建议分别测试衣物和裸露皮肤。不同表面的响应范围可能不同，不要用一次
成功拖动推断所有部位均兼容。当前优先验证干员总览，不将该坐标映射直接
视为世界或战斗场景支持。

### 快捷键

| 按键 | 操作 |
| --- | --- |
| `Ctrl+Shift+F8` | 切换触摸开启 / 关闭 |
| `Shift + 鼠标左键` | 抓取与拖动 |
| `Ctrl+Shift+F9` | 切换诊断信息 |
| `Ctrl+Shift+F7` | 强制关闭触摸 |
| `F10` | EFMI 的配置重载操作；不是触摸开关 |

若 `F10` 出现错误，先记录完整错误，不要连续重载或随意删除资源。
实际重载是否成功取决于所用框架、配置和完整版本，不能用源码检查结果代替。

### 可选 EXE 配置器

程序提供安装、配置和换装适配；只使用已有运行时不需要打开它。
完整包已包含 `Configurator`，无需另行解压配置器附件。
先安装基础 Mod ZIP，再将配置器 ZIP 内的 `Configurator` 文件夹放入
`EFMI/Mods/EndfieldJiggleEFMI/`。打开
`Configurator/EndfieldJiggleConfigurator.exe` 后，程序会自动定位同级 Mod；
也可手动选择 `EndfieldJiggle.ini` 与 `Passes.ini` 所在目录。

加载配置只读文件。点“保存配置”前必须退出游戏和 XXMI。参数保存仅更新
本 Mod 的 `EndfieldJiggle.ini`、`Passes.ini`，以及存在时的 `Outfits.ini`，
并将更新前字节及时间戳备份至 `ConfiguratorBackups`。着色器、缓存、
`d3dx.ini`、注入 DLL 和游戏文件不会被编辑。原目录换装适配会在另行确认后
修改所选换装的 INI；详情见[配置器](CONFIGURATOR.md)。

设置保存在 Mod 的 INI 文件中。应用成功后可关闭或删除 `Configurator`
文件夹；下次从 XXMI 启动时仍会使用保存的值。若只使用基础包默认快捷键，
无需下载 EXE。

“恢复上次设置”会校验已改文件的哈希和时间戳后才恢复。若其他工具在应用后
又改过这些文件，恢复会拒绝覆盖；请保留 `ConfiguratorBackups` 中的备份。

## 常见状态

| 诊断提示 | 含义与处理 |
| --- | --- |
| `TOUCH OFF` | 当前关闭触摸，可用 `Ctrl+Shift+F8` 开启 |
| `TOUCH ON` | 当前开启触摸，不代表鼠标一定命中了表面 |
| `DRAG INPUT HELD` | 已检测到拖动输入，不代表拾取与形变一定成功 |
| `NATIVE PIPELINE WARMUP` | 兼容颜色绘制正在预热；稳定显示后通常应结束 |
| `NO COMPATIBLE NATIVE PROGRAM` | 未检测到已覆盖接口，需检查场景、加载或接口兼容性 |
| `NO COMPATIBLE COLOR DRAW` | 未检测到符合条件的颜色绘制，不能仅据此判断是角色不支持 |
| `EFMI MODS DISABLED` | 框架的 Mod 开关关闭 |
| `EFMI VERSION REJECTED` | 框架版本检查未通过 |

持续闪烁或长期停在预热状态时，记录整个角色画面和提示，避免只截一行文字。

## 异常处理

### 完全无法拖动

先确认使用的是完整版本而非公开源码 ZIP，并已开启触摸和按住正确组合键。
在原版干员总览复现，查看诊断提示；不要先为每个角色添加网格白名单。

### 只有部分衣物或皮肤响应

记录未响应部位和对应场景。不同材质可能需要不同接口适配，
第三方替换模型也可能绕过原生绘制钩子。不要跨角色复制缓存或着色器文件。

### 方向异常、衣物分裂、闪烁或崩溃

按 `Ctrl+Shift+F7` 关闭交互。若异常仍然存在，退出游戏并按备份恢复。
记录角色、视角、分辨率、窗口模式和同时启用的其他 Mod。
不要将删除整个 `Mods` 或覆盖 DLL 当作修复步骤。

### 关闭、恢复与卸载

快捷键关闭不等于卸载。需要移除完整运行时时，请先退出游戏和启动器，
使用该版本的受控卸载或恢复流程；仅处理能确认属于它的文件。
不要使用其他项目或旧版安装器来管理不匹配的版本。

源码版没有安装到游戏，删除源码下载目录即可移除源码，不会修改游戏。
安装版卸载时只移动或删除 `EFMI/Mods/EndfieldJiggleEFMI`；
如安装前有同名目录，先备份并在卸载后恢复它。
仅删除子目录 `Configurator` 不会回滚已保存的运行设置；需要回滚时，先用
配置器的“恢复上次设置”。

## 换装兼容

v0.2.1 恢复已有 EJTouch 所需桥接，并可通过程序在普通换装原目录生成支持的绘制包装器。
安装器升级保留可读取的设置，手动升级使用干净目录，不要用旧 INI 覆盖新版。
基础运行时首次升级后完整重启；换装 INI 适配可在确认后按 F10 重载，
先关闭触摸并让目标角色离开画面。QAQM 缺失声明可在程序中按当前依赖检查和恢复，
见[换装兼容](OUTFIT-COMPATIBILITY.md)与[状态恢复](QAQM-STATE-RECOVERY.md)。

## 提交反馈

在 [GitHub Issues](https://github.com/woakato/EndfieldJiggle-EFMI/issues) 中提供：

- 版本号及来源：源码版或完整本地运行时。
- 游戏、XXMI / EFMI 版本，以及使用场景。
- 干员名字、具体部位、原版或第三方换装。
- 操作步骤、预期效果和实际结果。
- 包含诊断提示或完整错误的截图；需要时再提供脱敏日志。

提交前请遮挡 UID、账号信息与个人路径。不要上传游戏抓取资源、
模型、纹理、密码、令牌或完整私人安装目录。
使用风险见[免责声明](../DISCLAIMER.md)。
