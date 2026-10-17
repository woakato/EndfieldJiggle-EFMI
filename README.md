# EndfieldJiggle EFMI

面向《明日方舟：终末地》的开源角色交互实验项目，提供 XXMI 拖放包与独立便携包。
通过鼠标拖动角色表面，并在松开后产生回弹，让兼容的原版模型具有实时形变效果。

项目参考并复用了 [JiggleForge](https://github.com/wlytsgd2/JiggleForge)
的输入、弹簧运动与形变核心，将其适配到终末地的原生渲染流程。
本项目不是 JiggleForge 的官方终末地版本。

[下载可安装版](https://github.com/woakato/EndfieldJiggle-EFMI/releases/latest)
 · [源码与历史版本](https://github.com/woakato/EndfieldJiggle-EFMI/releases)
 · [使用说明](docs/USAGE.md)
 · [独立版说明](docs/STANDALONE.md)
 · [诊断版独立包](docs/DIAGNOSTIC-STANDALONE.md)
 · [可选配置器](docs/CONFIGURATOR.md)
 · [换装兼容](docs/OUTFIT-COMPATIBILITY.md)
 · [免责声明](DISCLAIMER.md)
 · [反馈问题](https://github.com/woakato/EndfieldJiggle-EFMI/issues)

> 已安装 XXMI / EFMI：下载拖放包。未安装 XXMI Launcher：下载独立便携包。
> 源码归档不是游戏安装包。

v0.2.4 于 2026-10-17 同步角色界面与大世界的触摸输入：默认开启，
按住 `Alt+鼠标左键` 拖动，松开左键释放，`Ctrl+Shift+F9` 切换开关。
修复重复抓取仍保留上一次位置的问题，启动时不显示 `TOUCH ON`。
三个安装包与配置器内嵌安装文件同步更新；旧版本用户请下载 v0.2.4 升级。

本版保留 2026-10-16 的通用渲染通道修复：根据共用的原生坐标变换同步
颜色、深度与描边，处理其他原皮仍有触摸黑块的问题。三个安装包和配置器内嵌文件同步更新，
已下载当天较早版本的用户需要重新下载。
完整包同时包含换装绘制前的通用通道识别，配置器支持全部十五个运行文件，
已有 `EJ-OUTFIT/1` 换装包装器不需要重新适配。
纯 Mod 和 drop-in 包采用直接包含 `shaders` 与运行时 INI 的结构，配置器支持带版本号的
文件夹及新增绘制。保留 Mod 库、改键、参数预设、配置导入导出与原有换装桥接。
改键与改参可保存后在游戏内重载；首次安装或升级运行时仍需重启。
独立便携包内置官方 EFMI、配套渲染库与同版触摸插件，提供游戏路径选择及启动入口。
2026-10-15 手动启动修订不再直接运行游戏 EXE：先在本包开启等待，再通过官方游戏启动器
以 DirectX 11 开始游戏。此启动流程尚待实机复验，不绕过安全校验。

## 项目特点

- **共享交互机制**：通过兼容的渲染接口处理模型，不逐角色登记网格白名单。
- **鼠标拖动与回弹**：共享拾取、输入控制、弹簧运动和空间形变场。
- **连续表面响应**：已适配的绘制路径共享形变状态，处理位置及表面方向。
- **默认开启交互**：可在配置器关闭启动默认值；导航或兼容绘制中断时取消当前交互。
- **保留原生绘制**：适配原始着色器流程；拖放包沿用已有 EFMI，独立包内置原版官方渲染库。
- **可选程序**：安装、修改键位和强度、原目录换装适配，不需后台常驻。

完整本地版本已获得跨角色迁移成功的实机反馈。兼容性仍取决于渲染接口、
材质和场景，不能将“通用”理解为所有角色、所有部位或所有换装均可使用。
技术验证细节见[开发与来源记录](docs/PROVENANCE.md)。

## 安装与使用

### 普通玩家

从 [最新 Release](https://github.com/woakato/EndfieldJiggle-EFMI/releases/latest)
按安装情况选一个包：

| 安装情况 | 下载附件 | 使用方式 |
| --- | --- | --- |
| 已有 XXMI / EFMI | `EndfieldJiggleEFMI-v0.2.4-drop-in-win64.zip` | 解压为 `Mods` 下一个目录，含可选配置器 |
| 没有 XXMI Launcher | `EndfieldJiggleEFMI-v0.2.4-standalone-win64.zip` | 整包解压，打开 `EndfieldJiggleLauncher.exe` |
| 已有 EFMI，只需 Mod | `EndfieldJiggleEFMI-v0.2.4-win64.zip` | 仅触摸 Mod，不含程序 |

独立包包含官方 EFMI 1.4.8、XXMI 渲染库 1.2.2、触摸 Mod 与配置器，不需另外安装
XXMI Launcher 或 .NET。选择游戏本体 `Endfield.exe`，点击“等待官方启动”，
再到官方游戏启动器选择 DirectX 11 并开始游戏。后续双击 `Start.cmd` 也只开始等待，
不会直接启动游戏。路径栏用于核对目标；权限错误会提示，不自动提权。
换装放入独立包自己的 `EFMI/Mods`。完整步骤见[独立版说明](docs/STANDALONE.md)。
独立版仍使用 EFMI / XXMI 渲染库；原有 XXMI 安装与独立包分别保存 Mod 和配置。

GitHub 自动提供的 `Source code` 归档是源代码，不是 Mod 包；不要用它代替 Release 附件。
以下拖放步骤适用于已有 XXMI / EFMI 的用户。

安装前退出游戏和 XXMI。纯 Mod / 拖放 ZIP 内直接包含 `shaders`、`EndfieldJiggle.ini`、
`Passes.ini`、`Outfits.ini`、`OutfitPreparation.ini`，请解压到 `Mods` 下一个独立目录，例如
`Mods/EndfieldJiggleEFMI-v0.2.4-drop-in-win64/shaders`；也可命名为 `EndfieldJiggleEFMI`。
不需要再套 `Mods/EndfieldJiggleEFMI`。不打开 EXE 也可使用。需要管理 Mod、修改键位或参数时，
再打开其中的 `Configurator/EndfieldJiggleConfigurator.exe`。
升级前先将旧目录备份到 `Mods` 之外，不要混合版本。
不要覆盖注入 DLL、全局 `ShaderFixes`、`d3dx.ini` 或其他 Mod。
完整步骤、快捷键、恢复方法和故障处理见[使用说明](docs/USAGE.md)
及压缩包内的 `INSTALL-zh-CN.md`。

拖放包已包含 EXE；纯 Mod 包不含配置器。配置器保存的值位于 Mod 文件夹中；
关闭 EXE 后游戏仍会使用这些设置。保存后按配置器显示的 EFMI 重载键应用，
首次安装或升级运行时仍需重启游戏。
基础 Mod 不依赖配置器，也不需要常驻。详见[可选配置器说明](docs/CONFIGURATOR.md)。

### 已具备完整运行时的测试者

从 XXMI 启动游戏，进入角色界面或大世界，在可见角色表面进行短距离拖动。
具体步骤和问题处理见[使用说明](docs/USAGE.md)。

| 按键 | 功能 |
| --- | --- |
| `Ctrl+Shift+F9` | 开启 / 关闭触摸 |
| `Alt + 鼠标左键` | 按住拖动，松开鼠标左键释放 |
| `Ctrl+Shift+F8` | 显示 / 隐藏诊断状态 |
| `Ctrl+Shift+F7` | 强制关闭触摸 |

基础安装包默认开启触摸，启动时不显示状态提示。配置器可取消“启动时开启触摸”；
关闭后按 `Ctrl+Shift+F9` 开启，再按一次关闭。Alt 可一直按住，松开左键后重新抓取。
源码研究版本身不能直接运行；设置保存在 Mod 文件夹内，不依赖 EXE 常驻。

## 兼容范围

Windows 版游戏的角色界面与大世界使用同一套触摸输入；本次修订已获用户实机确认可用。
游戏更新后的新着色器、透明材质、多角色重叠及第三方换装
可能需要额外适配，不能保证响应位置、方向或显示效果。
已有 EJTouch 适配包可调用本版恢复的换装桥接。普通换装仍需要绘制包装器。
QAQM 状态文件缺失与桥接缺失是独立问题，详见[换装兼容说明](docs/OUTFIT-COMPATIBILITY.md)。

程序的“换装适配”页可分析并在用户明确选择的源目录包装支持的索引绘制，支持撤销。
不复制换装模型，不新建第二份换装 Mod；源目录 INI 会修改并保存备份。
源目录可以来自任意 Mod 管理器的 profile、下载目录或暂存目录，不要求配置器识别管理器品牌。
适配完成后需让原管理器重新部署或刷新该 Mod；只有当前 EFMI/Mods 加载树中的目录才能使用
本程序的 QAQM 状态检查与恢复。
可逐项选择支持的绘制；调整已经适配的选择时先恢复原换装，再重新选择与适配。
适配完成后可在游戏内按 F10 重载，具体限制见[换装兼容说明](docs/OUTFIT-COMPATIBILITY.md)。
自定义顶点着色器、动态绘制范围等不受支持，程序会报告，不能承诺任意换装兼容。
本版本不提供滚轮深度控制、独立部件分组编辑或自动更新。

## 常见问题

**基础 Mod 压缩包为什么没有 EXE？**

基础 `win64.zip` 是由 EFMI 加载的运行时 Mod，不需要 EXE。改键和调整参数时，
使用拖放包内的配置器；若安装的是纯 Mod 包，可将其替换为拖放包中的
`Configurator` 文件夹。仅使用默认配置时不必打开配置器。

**独立版是不是完全不用 EFMI？**

独立版内置 EFMI 和配套渲染库，不要求预先安装 XXMI Launcher。
它使用同一版触摸 Mod，并增加本项目自己的便携加载入口。先点击本包的“等待官方启动”，
再由官方游戏启动器以 DirectX 11 开始游戏。没有开启等待时不会自动加载本包。

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
独立包同时保留官方 EFMI / XXMI 库的许可证、对应源码地址及本项目源码快照。
使用、修改或分享前请阅读[免责声明](DISCLAIMER.md)。
