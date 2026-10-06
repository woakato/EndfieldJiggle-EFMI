# 可选配置器

## 关系

`EndfieldJiggleEFMI-v0.2.0-win64.zip` 是游戏运行时 Mod。安装后即由
EFMI / XXMI 在游戏启动时读取；不需要启动或后台运行配置器。

`EndfieldJiggleConfigurator-v0.2.0-win64.zip` 是可选的 Windows x64 设置界面。
它只编辑当前已安装 Mod 的运行参数。应用后可关闭或移除配置器，
下次启动游戏仍会读取 Mod 文件夹中的已保存值。

配置器不会安装注入器，不修改游戏本体、`d3dx.ini`、`d3d11.dll`、
全局 `ShaderFixes`、着色器或缓存，也不是换装分析器。

## 安装配置器

1. 先按 [使用说明](USAGE.md) 安装 `EndfieldJiggleEFMI-v0.2.0-win64.zip`。
2. 从同一个 GitHub Release 下载配置器 ZIP。
3. 将其中的 `Configurator` 文件夹放进
   `EFMI/Mods/EndfieldJiggleEFMI/`，例如：

   ```text
   EFMI/
     Mods/
       EndfieldJiggleEFMI/
         EndfieldJiggle.ini
         Passes.ini
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

v0.2.0 默认保持关闭触摸：`Ctrl+Shift+F8` 切换，`Ctrl+Shift+F7` 强制关闭，
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
无需下载或安装它。

## 构建

在 Windows x64 和 .NET 8 SDK 环境下：

```powershell
.\tools\Build-Configurator.ps1
.\tools\Test-Configurator.ps1 -ModZipPath 'D:\path\to\EndfieldJiggleEFMI-v0.2.0-win64.zip'
```

构建脚本生成自包含 x64 配置器 ZIP，不把游戏兼容资源或着色器加入配置器包。
