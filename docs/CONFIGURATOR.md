# 可选配置器

## 关系

`EndfieldJiggleEFMI-v0.2.1-win64.zip` 是游戏运行时 Mod。安装后即由
EFMI / XXMI 在游戏启动时读取；不需要启动或后台运行配置器。

`EndfieldJiggleConfigurator-v0.2.1-win64.zip` 是可选的 Windows x64 设置界面。
完整包里的 EXE 内置运行时，也可安装、升级、恢复自有 Mod，以及分析和适配换装。
运行参数应用后可关闭或移除配置器，
下次启动游戏仍会读取 Mod 文件夹中的已保存值。

程序不会安装注入器，不修改游戏本体、`d3dx.ini`、`d3d11.dll` 或全局 `ShaderFixes`。
安装只复制内置自有运行时；换装适配须确认后在原目录修改 INI，不改模型或纹理。
补齐 QAQM 状态仅使用该换装匹配备份里已证实的数值声明。

## 安装与换装

完整 ZIP 解压后双击 `Install.cmd`，选择 EFMI 目录，点击“安装 / 升级”。
程序会把自己放入安装目录，后续可直接打开安装目录中的 EXE。
升级保留可读取的设置，备份保存到 `EFMI/EndfieldJiggleBackups/`。
恢复安装前版本时，请从原解压包启动程序，以免替换正在运行的自身。

在“换装适配”页选择或拖入当前 `EFMI/Mods` 内的原换装目录，点击“分析”，
确认支持的绘制后“原目录适配”。不再生成 `EJTouch_...` 的第二份换装。
原 INI 保存在该换装的 `DISABLED_EJTouchBackups/`，支持“恢复原换装”。
已有旧包装器不重复生成；旧复制版与原换装不能同时开启。

“游戏内重载模式（F10）”只允许换装包装器的写入/恢复，仍会阻止 XXMI 同时改文件。
按提示关闭触摸、切换角色使目标离开画面，操作结束后再回游戏按 F10。
不自动发送按键，不监控或自动复制新 Mod，不保证自定义着色器热加载。
QAQM 状态恢复、基础运行时安装和参数改动仍要求退出游戏。

## 安装配置器

1. 先按使用说明安装 v0.2.1 Mod。完整 `with-configurator` 包已含下述 EXE，
   无需再下载配置器附件。
2. 从同一个 GitHub Release 下载配置器 ZIP。
3. 将其中的 `Configurator` 文件夹放进
   `EFMI/Mods/EndfieldJiggleEFMI/`，例如：

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

4. 双击 `Configurator/EndfieldJiggleConfigurator.exe`。程序会向上查找
   已安装的 `EndfieldJiggle.ini` 与 `Passes.ini`；也可以手动选择该目录。

## 应用设置

启动设置、开关/关闭/诊断/拖动按键及共享形变参数，会在点击“应用设置”
并确认后写入当前 Mod。游戏和 XXMI 必须先退出。之后关闭配置器，从 XXMI
启动游戏即可使用已保存的设置。

v0.2.1 默认保持关闭触摸：`Ctrl+Shift+F8` 切换，`Ctrl+Shift+F7` 强制关闭，
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
和快捷键工作。可选配置器 ZIP 不属于基础 Mod ZIP；仅想使用默认运行时的玩家
无需打开它。配置器仍支持读取 v0.2.0 运行时；读取或改参不会自动把旧版
升级为 v0.2.1，请用完整的新版运行时目录升级。

## 构建

在 Windows x64 和 .NET 8 SDK 环境下：

```powershell
.\tools\Build-Configurator.ps1
.\tools\Test-Configurator.ps1 -ModZipPath 'D:\path\to\EndfieldJiggleEFMI-v0.2.1-win64.zip'
```

默认构建只生成配置器；传入 `-RuntimeZipPath` 可把经发布回归检查的运行时嵌入 EXE。
`tools/Build-Complete.ps1` 会按顺序构建基础 Mod、内置安装资源的 EXE 和完整包。
