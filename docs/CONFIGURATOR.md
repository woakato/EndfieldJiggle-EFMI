# 可选配置器

## 关系

`EndfieldJiggleEFMI-v0.2.3-win64.zip` 是游戏运行时 Mod。安装后即由
EFMI / XXMI 在游戏启动时读取；不需要启动或后台运行配置器。

拖放包中的 `Configurator/EndfieldJiggleConfigurator.exe` 是可选的 Windows x64
设置界面。它内置运行时，也可安装、升级、恢复自有 Mod，以及分析和适配换装。
运行参数应用后可关闭或移除配置器，下次启动游戏仍会读取 Mod 文件夹中的已保存值。
纯 Mod 包不含 EXE。

程序不会安装注入器，不修改游戏本体、`d3dx.ini`、`d3d11.dll` 或全局 `ShaderFixes`。
安装只复制内置自有运行时；换装适配须确认后在原目录修改 INI，不改模型或纹理。
补齐 QAQM 状态仅使用该换装匹配备份里已证实的数值声明。

## 安装与换装

拖放 ZIP 内直接包含 `shaders` 与 INI；解压到 EFMI 的 `Mods` 下一个独立文件夹。
文件夹可保留压缩包的版本名称，也可命名为 `EndfieldJiggleEFMI`。
首次安装不需要打开 EXE。程序在 `Configurator/EndfieldJiggleConfigurator.exe`，
自动定位所在 Mod，第一屏为当前 Mod 库。
可识别 EFMI / Mods / EXE 所在目录，以及误套一层的
`EndfieldJiggleEFMI/EndfieldJiggleEFMI`。检测到多份启用的运行时时拒绝猜选。
记住上次选择的目录；附近发现 XXMI Launcher 配置时，会只读取 EFMI 的安装路径。
不会全盘搜索或自动搬动文件。多套一层时允许配置与换装适配，
但安装器升级仍需先整理为 `Mods` 的直接子目录。
拖放包采用手动复制安装，不会自动扫描后搬动或覆盖文件。更新前请先备份
实际触摸目录；如要使用配置器的安装、升级或恢复功能，需从其界面
选择 EFMI 路径并逐项确认操作。

在“换装适配”页选择或拖入换装源目录，点击“分析”，确认并勾选支持的绘制后点击
“适配所选绘制”。源目录可以是当前 `EFMI/Mods` 内的换装，也可以是其他 Mod
管理器的 profile、下载目录或暂存目录。不需要为每个管理器单独安装插件或配置。
程序不生成 `EJTouch_...` 的第二份换装，而是修改用户明确选择的源目录；原 INI 保存在
该换装的 `DISABLED_EJTouchBackups/`，支持“恢复原换装”。
若源目录不在当前 `EFMI/Mods` 内，适配完成后必须让原 Mod 管理器重新部署或刷新该 Mod，
否则游戏加载到的仍是旧文件。
已有旧包装器不重复生成；旧复制版与原换装不能同时开启。

“F10 重载模式”允许快捷键、参数及换装包装器的写入/恢复。
换装写入仍会阻止 XXMI 同时改文件；改键和改参不要求关闭启动器。
按提示关闭触摸、切换角色使目标离开画面，操作结束后再回游戏按 F10。
不自动发送按键，不监控或自动复制新 Mod，不保证自定义着色器热加载。
普通绘制适配不依赖 QAQM 检查；QAQM 检查与恢复是当前 EFMI/Mods 中实际加载换装的
可选诊断功能。外部管理器源目录不能由本程序证明实际加载状态，需由管理器负责部署后再检查。
QAQM 状态恢复和基础运行时安装仍要求退出游戏。

## Mod 库与参数

Mod 库按当前 EFMI 的 `Mods` 目录扫描，可搜索、显示停用项并加载作者提供的
`preview.png`、`preview.jpg`、`cover.png`、`cover.jpg` 或 `logo.png`。
双击换装打开分析；外部管理器中的 Mod 请使用“选择换装”或直接拖入源目录。
库扫描不会自动读取其他管理器的虚拟 Mod 列表，也不会自动启用、改名或适配其它 Mod。
“换装适配”页可以搜索绘制、全选或清空选择。索引数不是身体部位名称，
程序不会把猜测的 Body / Cloth 分组当成已验证的解剖划分。
已经适配的项目需先“恢复原换装”再修改选择，不能叠加包装器。

触摸页有轻柔 / 标准 / 明显三档预设，也可调整半径、位移与拖动比例。
键盘按钮只在当前程序窗口内录入组合键，不是全局键盘监听。
鼠标键从列表选择。F10 / F11 保留给 EFMI，单独 Shift 不可设为触摸开关。
“无 Shift 拖动”仅修改待保存的拖动绑定，不重写第三方菜单。
JSON 导入只加载待保存参数；导出不更改当前 Mod。

诊断页显示实际 `reload_config` 与其它 INI 中的潜在按键冲突。
`Mods Display` 是 EFMI 的所有 Mod 总开关，和触摸开关、Draw 诊断不同。
诊断页会分别列出总开关及临时原版 `show_original` 的实际绑定。
扫描不求值第三方 condition，不是“当前一定触发”的结论，
也不读取外部管理器的全局热键。导出的报告包含本机路径，分享前自行脱敏。
Mod 库、按键扫描、换装与依赖分析在后台执行，扫描可取消。
配置写入不能中途取消，完成前阻止退出，以保留完整备份流程。
操作耗时和错误保存在 `%LOCALAPPDATA%/EndfieldJiggle/configurator.log`，
仅写本机，不上传；日志包含目录，分享前脱敏。

## 安装配置器

拖放包已内置配置器。若已安装纯 Mod 包，可从同一 Release 的拖放包中取出
`Configurator` 文件夹，放入 `EFMI/Mods/EndfieldJiggleEFMI/`，例如：

   ```text
   EFMI/
     Mods/
       EndfieldJiggleEFMI/
         EndfieldJiggle.ini
         Passes.ini
         Outfits.ini
         shaders/
         Configurator/
           EndfieldJiggleConfigurator.exe
   ```

双击 `Configurator/EndfieldJiggleConfigurator.exe`。程序会向上查找
   已安装的 `EndfieldJiggle.ini` 与 `Passes.ini`；也可以手动选择该目录。

## 应用设置

启动设置、开关/关闭/诊断/拖动按键及共享形变参数，会在点击“应用设置”
并确认后写入当前 Mod。默认启用 F10 重载模式：关闭触摸、松开拖动后保存，
等待写入结束，回到游戏松开所有修饰键，再按实际重载键，通常为 F10。
重载会初始化非持久状态，触摸开关回到保存的启动默认值。
关闭重载模式时仍需退出游戏和 XXMI。之后可关闭程序，参数不依赖 EXE 常驻。

v0.2.2 默认保持关闭触摸：`Ctrl+Shift+F8` 切换，`Ctrl+Shift+F7` 强制关闭，
`Ctrl+Shift+F9` 切换诊断，按住 `Shift+鼠标左键` 拖动。
“恢复此版本默认”只修改界面中的待应用值，仍需确认“应用设置”才会写入。

程序只改写 `EndfieldJiggle.ini`、`Passes.ini`，以及目录中存在时的
`Outfits.ini`。每次更改前，会将这些文件的原始字节和时间戳备份到：

```text
EFMI/Mods/EndfieldJiggleEFMI/ConfiguratorBackups/
```

“恢复上次设置”会校验文件内容和时间戳。若这些文件在配置后又被其他工具
修改，程序会拒绝覆盖。不要手动删除仍可能需要的备份。

## 不使用配置器

不需要额外服务或常驻进程。游戏继续按 `EndfieldJiggle.ini` 中已有的默认值
和快捷键工作。纯 Mod 包不包含配置器；仅想使用默认运行时的玩家无需额外操作。
拖放包内的配置器仍支持读取旧运行时；读取或改参不会自动升级运行时。
升级时请按安装说明替换整个 Mod 目录。

## 构建

在 Windows x64 和 .NET 8 SDK 环境下：

```powershell
.\tools\Build-Configurator.ps1
.\tools\Test-Configurator.ps1 -ModZipPath 'D:\path\to\EndfieldJiggleEFMI-v0.2.2-win64.zip'
```

默认构建只生成配置器；传入 `-RuntimeZipPath` 可把经发布回归检查的运行时嵌入 EXE。
`tools/Build-Complete.ps1` 会按顺序构建基础 Mod、内置安装资源的 EXE 和完整包。
