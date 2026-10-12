# 独立便携版

已有 XXMI / EFMI 的用户继续使用 `drop-in-win64.zip`，把 `EndfieldJiggleEFMI`
文件夹放入现有 EFMI 的 `Mods`。只需默认参数且不需要程序的用户可选纯 Mod 包。

没有 XXMI Launcher 的用户使用 `EndfieldJiggleEFMI-v0.2.2-standalone-win64.zip`。
它包含官方 EFMI 1.4.8、官方 XXMI 渲染库 1.2.2、v0.2.2 触摸 Mod、
现有配置器和本项目的便携启动器，无需另行安装 XXMI Launcher 或 .NET。
“独立”指不依赖 XXMI Launcher 安装，渲染仍使用 EFMI / XXMI 的官方库。

## 首次使用

1. 先通过官方游戏启动器安装、更新游戏，并确认可以正常启动；退出游戏与 XXMI。
2. 将整个独立包解压到可写目录，例如 `D:\Mods\EndfieldJiggle`。
   不要放进游戏目录或已有 EFMI 目录，不要只提取一个 EXE。
3. 打开 `EndfieldJiggleLauncher.exe`，在“游戏路径（Endfield.exe）”中确认自动找到的路径，
   或点击“选择游戏”选取游戏安装目录里的 `Endfield.exe`，然后点击“启动游戏”。
   路径保存在本包的 `launch-settings.json`，也可直接粘贴完整路径。
4. 等待状态显示 EFMI 已加载，之后可关闭启动器。进入干员总览后使用触摸快捷键。
5. 后续双击 `Start.cmd` 即按保存路径启动；未保存路径时显示选择界面。

默认触摸关闭：`Ctrl+Shift+F8` 开关，`Shift+鼠标左键` 拖动，
`Ctrl+Shift+F7` 强制关闭，`Ctrl+Shift+F9` 诊断。
启动器在启动时使用与上游 EFMI 相同的 DirectX 11 参数。
直接从官方游戏启动器打开游戏不会自动加载本包。

游戏要求管理员权限时，启动器会通过 Windows 以管理员权限重新打开，并使用已保存的
路径继续启动。系统可能显示授权窗口，允许后继续；部分机器会按系统设置直接授权。
取消授权会保留选择界面，不反复请求。路径选择和配置界面可直接打开。

## 配置和换装

点击“打开配置器”使用原有的键位、参数、Mod 库和换装适配功能。
启动窗口随之关闭，配置器会自动识别本包的 EFMI。关闭程序后，已保存的触摸设置仍然保留；
保存参数或支持的换装 INI 后可回到游戏松开修饰键并按 `F10`。
首次安装或更新运行时需要退出并重新启动游戏。

点击“Mods 文件夹”，将其他换装 Mod 放进本包的 `EFMI/Mods`。
如需触摸适配，从配置器的 Mod 库选择换装并按
`OUTFIT-COMPATIBILITY.md` 操作。第三方 Mod 的额外依赖仍需自行安装。
本包不包含换装模型、纹理或 QAQM；它们不会从你的其他 EFMI 安装自动复制过来。

同一次游戏会话选择一套 EFMI。独立包与原有 XXMI 的 Mod 和参数分别存放，
不要同时从两边启动、不要混用渲染 DLL。

## 目录和恢复

```text
EndfieldJiggleEFMI-v0.2.2-standalone/
  EndfieldJiggleLauncher.exe
  Start.cmd
  standalone-manifest.json
  Licenses/
  EFMI/
    3dmloader.dll
    d3d11.dll
    d3dcompiler_47.dll
    d3dx.ini
    Core/
    Mods/
      EndfieldJiggleEFMI/
        Configurator/EndfieldJiggleConfigurator.exe
```

启动器只在本包保存路径和日志，不安装文件到游戏目录，不修改游戏资源。
渲染库会写入本包的 EFMI 日志、用户配置和缓存。
移除时退出游戏与程序，备份需要保留的 `Mods` 和设置，再删除整个独立包。
继续用官方游戏启动器启动即可。升级时使用新的完整目录，备份并迁移自有
Mod 和配置，不用旧 `d3dx.ini` 或渲染 DLL 覆盖新包。

## 问题反馈

- 找不到游戏：选择包含 `Endfield.exe`、`Endfield_Data`、
  `GameAssembly.dll` 的游戏目录。
- DLL 缺失或校验失败：完整重新解压发布包，保留原文件名和相对路径。
- “请求的操作需要提升”或错误码 `740`：使用修正后的完整独立包。
  新版会处理启动权限；仍失败时，右键本包启动器选择“以管理员身份运行”。
- 等待 EFMI 超时：退出游戏，检查路径、游戏版本及游戏与启动器的运行权限。
  若游戏需要管理员权限，对本包启动器使用相同的管理员权限后重试。
- 游戏运行而触摸无效：确认此次从独立启动器启动，且本包触摸 Mod 已开启。
  不要同时打开另一套 XXMI。
- 启动失败时查看本包 `Logs/launcher.log` 与 `EFMI/d3d11_log.txt`；
  分享前遮挡账号、UID 和个人路径。

独立启动器经过隔离检查，2026-10-12 已收到用户确认启动成功的实机反馈。
登录、画面与触摸效果未在此次反馈中单独确认。
详见 `VALIDATION-STANDALONE-zh-CN.md` 和 `DISCLAIMER.md`。

许可文本位于 `Licenses`，本项目的构建源码快照位于 `Sources`；
官方依赖的固定版本源码地址见 `THIRD-PARTY-NOTICES.md`。
