# v0.2.3 安装与升级

本版于 2026-10-16 替换为通用渲染通道修复，同步原皮颜色、深度与描边的触摸形变。
完整包同时包含换装修复及新版配置器；换装绘制前先识别原生通道，避免部分绘制漏掉位移。
已下载当天较早的 v0.2.3 时，请重新下载更新后的包。保留 EJTouch 换装桥接、配置器与拖放包。游戏仍由 EFMI / XXMI
加载触摸 Mod，EXE 用于安装、配置和换装适配，不需要常驻。

## 选择安装包

- 已有 XXMI / EFMI：使用 `drop-in-win64.zip`，按下面步骤放入已有 `Mods`。
- 未安装 XXMI Launcher：使用 `standalone-win64.zip`，内置 EFMI 和运行库。
  整包解压后打开 `EndfieldJiggleLauncher.exe`，选择 `Endfield.exe` 并开启等待，
  再通过官方游戏启动器以 DirectX 11 启动。
  详见 [独立版安装说明](STANDALONE.md)；不要把独立包覆盖到已有 EFMI 目录。
- 已有 EFMI 且不需要程序：使用纯 Mod 的 `win64.zip`。

## 安装

1. 完全退出游戏与 XXMI。
2. 下载 `EndfieldJiggleEFMI-v0.2.3-drop-in-win64.zip`，或不含配置器的纯 Mod 包。
3. 已有旧版时先备份到 `Mods` 之外。将 ZIP 解压到 `Mods` 下一个独立目录；
   使用压缩软件的“解压到同名文件夹”即可，也可将文件夹命名为 `EndfieldJiggleEFMI`。
   不要混合版本或覆盖 EFMI 根目录。
4. 从 XXMI 启动游戏，先确认原版角色或换装显示正常，再开启触摸。

解压后的结构应直接是：

```text
Mods/
  EndfieldJiggleEFMI-v0.2.3-drop-in-win64/
    shaders/
    EndfieldJiggle.ini
    Passes.ini
    Outfits.ini
    OutfitPreparation.ini
    Configurator/EndfieldJiggleConfigurator.exe
```

纯 Mod 包结构相同，但没有 `Configurator`。压缩包内部没有第二层触摸 Mod 文件夹。
旧文件夹名 `EndfieldJiggleEFMI` 也受支持；配置器会识别实际安装位置。
`Outfits.ini` 与 `OutfitPreparation.ini` 必须使用同一包内的文件。
已有 `EJ-OUTFIT/1` 包装器不需要为本次修复重新适配。

默认关闭触摸：`Ctrl+Shift+F8` 开关，`Ctrl+Shift+F7` 强制关闭，
`Ctrl+Shift+F9` 诊断，按住 `Shift+鼠标左键` 拖动。

## 配置器

拖放包已将 EXE 放在触摸目录的 `Configurator/EndfieldJiggleConfigurator.exe`。
仅 Mod 包不含 EXE。已保存的参数在关闭配置器后仍生效。
参数保存只更新本 Mod 的参数 INI。
换装适配只在确认后修改所选换装的 INI；不编辑游戏本体、注入 DLL 或全局配置。

手动升级采用干净目录，参数先恢复为默认值；内置安装器升级保留可读取的参数。
不要用旧 `EndfieldJiggle.ini` / `Passes.ini` 覆盖新文件。
旧目录内的配置与备份仍可用于恢复旧版本。

## 已适配换装

已有 `EJTouch_...` 适配目录可使用本版恢复的桥接，不能同时启用它和原换装。
普通换装可从“Mod 库”双击，或在“换装适配”页拖入原目录，
分析后勾选需要的绘制，点击“适配所选绘制”。
程序只包装支持的绘制，并保留恢复备份，不复制纹理或生成第二份换装。
旧 `EJTouch_...` 已包装目录不要重复适配；原换装与旧复制版不能同时启用。
详细边界及 QAQM 解析警告排查见 `OUTFIT-COMPATIBILITY.md`。

改键、改参及换装适配可使用默认勾选的“F10 重载模式”：先在游戏里关闭触摸，
换装适配时切换到其他角色，使被修改角色离开画面。切回桌面保存，
再切回游戏，松开 Ctrl / Shift / Alt 后按 F10。
程序不会自动向游戏发送按键；
适配或恢复写入期间不要按 F10，XXMI 启动器须关闭。
快捷键与参数保存不需要关闭启动器；写入结束后才能重载。
程序诊断页显示当前 `d3dx.ini` 中的实际重载绑定；不是 F10 时按显示的键。
`F10` 是配置重载，不代替着色器接口变化后的重启。换装切换或模型消失时，
先完全退出游戏再重启。若模型仍不正常，保留日志并恢复旧目录，不要继续
重载或删除资源。停用时将整个适配目录命名为 `DISABLED_...`，
而非仅关闭运行变量。

## 恢复

安装器安装的版本可从原解压包打开程序，选择同一 EFMI 目录，点击“恢复安装前版本”。
如果之后改过自有文件，程序会拒绝强行覆盖。正在运行的已安装 EXE 不能自我替换，
所以请从解压包执行恢复。换装的“恢复原换装”与参数的“恢复上次设置”分别管理。

退出游戏与 XXMI，将新 `EndfieldJiggleEFMI` 移到 `Mods` 之外，
再把原先备份目录恢复到原路径。配置器删除不等于恢复 INI 配置。
拖放包不包含第三方换装模型、纹理、游戏本体或替换注入 DLL。
独立包另行内置官方 EFMI 与配套运行库，不改变已有安装。

黑面修复经过原始着色器、实际模型顶点回放与加载验证，并获用户接受发布；不保证所有干员、
换装、部位或游戏更新后的接口兼容。使用前阅读 `DISCLAIMER.md`。
