# EndfieldJiggle EFMI

面向《明日方舟：终末地》EFMI / XXMI 环境的开源角色交互实验项目。
通过鼠标拖动角色表面，并在松开后产生回弹，让兼容的原版模型具有实时形变效果。

项目参考并复用了 [JiggleForge](https://github.com/wlytsgd2/JiggleForge)
的输入、弹簧运动与形变核心，将其适配到终末地的原生渲染流程。
本项目不是 JiggleForge 的官方终末地版本。

[下载可安装版](https://github.com/woakato/EndfieldJiggle-EFMI/releases/latest)
 · [源码与历史版本](https://github.com/woakato/EndfieldJiggle-EFMI/releases)
 · [使用说明](docs/USAGE.md)
 · [可选配置器](docs/CONFIGURATOR.md)
 · [换装兼容](docs/OUTFIT-COMPATIBILITY.md)
 · [免责声明](DISCLAIMER.md)
 · [反馈问题](https://github.com/woakato/EndfieldJiggle-EFMI/issues)

> 完整 Windows x64 包包含运行时和安装配置程序，可选择 EFMI 目录安装，
> 也可手动放入 `Mods`。源码研究版 ZIP 不是游戏安装包。

v0.2.1 修复 v0.2.0 发布包遗漏的 EJTouch 换装桥接，并修正配置器自定义拖动键
的会话判断。此修复包的隔离测试不代替游戏实机复验。
完整包新增原目录换装适配、可回退安装和按实际依赖检查的 QAQM 状态恢复。

## 项目特点

- **共享交互机制**：通过兼容的渲染接口处理模型，不逐角色登记网格白名单。
- **鼠标拖动与回弹**：共享拾取、输入控制、弹簧运动和空间形变场。
- **连续表面响应**：已适配的绘制路径共享形变状态，处理位置及表面方向。
- **默认关闭交互**：需要手动开启；导航或兼容绘制中断时取消当前交互。
- **保留原生绘制**：适配原始着色器流程，不随本仓库分发替换注入 DLL。
- **可选程序**：安装、修改键位和强度、原目录换装适配，不需后台常驻。

完整本地版本已获得跨角色迁移成功的实机反馈。兼容性仍取决于渲染接口、
材质和场景，不能将“通用”理解为所有角色、所有部位或所有换装均可使用。
技术验证细节见[开发与来源记录](docs/PROVENANCE.md)。

## 安装与使用

### 普通玩家

从 [最新 Release](https://github.com/woakato/EndfieldJiggle-EFMI/releases/latest)
下载 `EndfieldJiggleEFMI-v0.2.1-with-configurator-win64.zip`（含可选配置器），
或 `EndfieldJiggleEFMI-v0.2.1-win64.zip`（仅 Mod）。GitHub 自动提供的 `Source code`
归档是源代码，不是 Mod 包；不要用它代替 Release 附件。

安装前退出游戏和 XXMI。解压完整 ZIP，双击 `Install.cmd`，选择 EFMI 目录，
点击“安装 / 升级”。程序备份自有文件，保留可读取的键位和强度。
也可以将 ZIP 中的 `Mods/EndfieldJiggleEFMI` 复制到 EFMI 的 `Mods` 目录；
手动升级时先将旧目录备份到 `Mods` 之外，不要混合版本。
不要覆盖注入 DLL、全局 `ShaderFixes`、`d3dx.ini` 或其他 Mod。
完整步骤、快捷键、恢复方法和故障处理见[使用说明](docs/USAGE.md)
及压缩包内的 `INSTALL-zh-CN.md`。

完整包已包含 EXE。改键或调整参数时，也可另下载
`EndfieldJiggleConfigurator-v0.2.1-win64.zip`，将其中的 `Configurator`
文件夹放进 `EFMI/Mods/EndfieldJiggleEFMI/`。双击其中的配置器，应用的值会
保存在 Mod 文件夹中；关闭 EXE 后，游戏下次启动仍会使用这些设置。
基础 Mod 不依赖配置器，也不需要常驻。详见[可选配置器说明](docs/CONFIGURATOR.md)。

### 已具备完整运行时的测试者

从 XXMI 启动游戏，进入原版干员总览，先确认模型在关闭交互时显示正常，
再开启触摸并进行短距离拖动。具体步骤和问题处理见[使用说明](docs/USAGE.md)。

| 按键 | 功能 |
| --- | --- |
| `Ctrl+Shift+F8` | 开启 / 关闭触摸 |
| `Shift + 鼠标左键` | 按住拖动，松开后释放 |
| `Ctrl+Shift+F9` | 显示 / 隐藏诊断状态 |
| `Ctrl+Shift+F7` | 强制关闭触摸 |

基础安装包启动后默认关闭触摸；源码研究版本身不能直接运行。配置器应用
设置后，值会保存在 Mod 文件夹内，不依赖 EXE 常驻。

## 兼容范围

当前适配重点是 **Windows 版游戏的原版干员总览**。
其他场景、游戏更新后的新着色器、透明材质、多角色重叠及第三方换装
可能需要额外适配，不能保证响应位置、方向或显示效果。
已有 EJTouch 适配包可调用本版恢复的换装桥接。普通换装仍需要绘制包装器。
QAQM 状态文件缺失与桥接缺失是独立问题，详见[换装兼容说明](docs/OUTFIT-COMPATIBILITY.md)。

程序的“换装适配”页可分析并在原目录包装支持的索引绘制，支持撤销。
不复制换装模型，不新建第二份换装 Mod；原目录 INI 会修改并保存备份。
适配完成后可在游戏内按 F10 重载，具体限制见[换装兼容说明](docs/OUTFIT-COMPATIBILITY.md)。
自定义顶点着色器、动态绘制范围等不受支持，程序会报告，不能承诺任意换装兼容。
本版本不提供滚轮深度控制、独立部件分组编辑或自动更新。

## 常见问题

**基础 Mod 压缩包为什么没有 EXE？**

基础 `win64.zip` 是由 EFMI 加载的运行时 Mod，不需要 EXE。改键和调整参数时，
使用完整包中的配置器或单独的可选配置器附件；仅使用默认配置时不必打开它。

**是不是每个角色都需要单独导出资源？**

设计上按共享渲染接口适配，不按角色维护白名单。若出现未覆盖的接口，
需要补充接口适配；这不等于每换一名角色都要重新配置。

**只有衣服或部分皮肤响应怎么办？**

不同表面可能走不同的绘制路径。记录具体角色、部位、场景和诊断状态后反馈，
不要跨角色复制着色器或覆盖其他 Mod 来尝试修复。

**能保证账号安全吗？**

不能。本项目不承诺游戏规则允许使用，也不保证不会出现账号、反作弊、
兼容性或数据方面的风险。请先阅读[免责声明](DISCLAIMER.md)并自行决定是否使用。

更多操作与故障处理见[使用说明](docs/USAGE.md)。

## 分享与反馈

欢迎分享**本仓库或 Release 的原始链接**，请说明所分享的是安装包还是源码，
不要以“全角色通用安装包”“保证不封号”或“官方插件”等描述传播。

转载或发布修改版时，请遵守 GPL-3.0-only，保留适用的许可证、版权和来源说明，
并明确标注修改内容。项目许可证不授予其他作者 Mod 或游戏资源的再分发权限。
发布源码的说明见[开发文档](docs/DEVELOPMENT.md)。

反馈请使用 [GitHub Issues](https://github.com/woakato/EndfieldJiggle-EFMI/issues)，
提供版本、角色、场景、操作步骤及诊断截图。截图和日志请先遮挡 UID、
账号信息、本机个人路径或其他不希望公开的内容。

## 致谢与许可

- [JiggleForge](https://github.com/wlytsgd2/JiggleForge)：输入、运动与形变核心及参考实现。
- [XXMI Launcher](https://github.com/SpectrumQT/XXMI-Launcher) /
  [EFMI](https://github.com/SpectrumQT/EFMI-Package)：所对接的启动与渲染框架。
- 参与实机测试、问题反馈与兼容性验证的使用者。

原创及适用的复用源码采用 [GPL-3.0-only](LICENSE)。
可安装包包含与本机所测游戏渲染接口对应的着色器兼容规则及缓存；
这些集成材料不因此获得 GPL 许可。第三方内容见
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；游戏及其他作者的素材
不因此改为 GPL 许可。
使用、修改或分享前请阅读[免责声明](DISCLAIMER.md)。
