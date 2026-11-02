# v0.2.7 安装与升级

v0.2.7 增加长按 Alt 隔离鼠标镜头输入。松开 Alt 恢复，按 Esc 可临时解除。
模块不冻结相机矩阵或游戏时间，剧情自动运镜不被主动锁定。
使用者已确认本次反馈的问题解决，本版为正式版；详见 [输入模块说明](ALT-INPUT-GUARD.md)。

打包修订 3 将包内说明收为一份 `README.txt`，许可与来源集中在 `Licenses`；
完整说明和测试记录保留在源码包及仓库。运行文件、输入模块及操作方式保持一致。

v0.2.7 修订 2 增加“安装与恢复 → 修复目录”，可备份残留触摸 INI 并补齐选定安装；
旧版和新版目录都无法拖动时，先按 [目录修复说明](DIRECTORY-REPAIR-zh-CN.md) 检查。
继承 v0.2.6 的分类目录、多层嵌套定位及原位升级恢复。配置器的通用换装分析区分模型与辅助绘制，
沿命名空间追踪资源别名，允许确认缺失外观贴图后保留作者原引用进行适配。
缺失网格、骨骼、计算资源或着色器仍会阻止写入。详见
[换装兼容](OUTFIT-COMPATIBILITY.md)。

沿用 v0.2.4 的全部十五个运行文件。角色界面与大世界仍使用 Alt 加鼠标左键，
触摸默认开启，Ctrl+Shift+F9 切换。游戏由 EFMI / XXMI 加载触摸 Mod，
配置器用于安装、配置和换装适配，可在保存后关闭；新增的输入模块需要常驻本次会话。

## 选择安装包

- 已有 XXMI / EFMI：使用 `drop-in-win64.zip`，按下面步骤放入已有 `Mods`。
- 未安装 XXMI Launcher：使用 `standalone-win64.zip`，内置 EFMI 和运行库。
  整包解压后打开 `EndfieldJiggleLauncher.exe`，选择 `Endfield.exe` 并开启等待，
  再通过官方游戏启动器以 DirectX 11 启动。
  详见 [独立版安装说明](STANDALONE.md)；不要把独立包覆盖到已有 EFMI 目录。
- 已有 EFMI 且不需要配置器：使用纯 Mod 的 `win64.zip`，也附带可选输入模块。

## 安装

1. 完全退出游戏与 XXMI。
2. 选择 `EndfieldJiggleEFMI-v0.2.7-drop-in-win64.zip`，包含同版配置器与输入模块。
   纯 Mod 包不包含适配程序；已有运行时可仅使用新版配置器。
3. 已有旧版时先备份到 `Mods` 之外。将 ZIP 解压到 `Mods` 下一个独立目录；
   使用压缩软件的“解压到同名文件夹”即可，也可将文件夹命名为 `EndfieldJiggleEFMI`。
   不要混合版本或覆盖 EFMI 根目录。
4. 从 XXMI 启动游戏，触摸默认开启；确认原版角色或换装显示正常后进行短距离拖动。
5. 需要 Alt 镜头隔离时，运行 Mod 内 `InputGuard/EndfieldJiggleInputGuard.exe`，
   每次游戏会话运行一次并保持运行。它会沿安装位置向上查找 EFMI；独立版自动启动它。

解压后的结构可以是：

```text
Mods/
  任意分类目录/
    EndfieldJiggleEFMI-v0.2.7-drop-in-win64/
      shaders/
      EndfieldJiggle.ini
      Passes.ini
      Outfits.ini
      OutfitPreparation.ini
      Configurator/EndfieldJiggleConfigurator.exe
      InputGuard/EndfieldJiggleInputGuard.exe
      InputGuard/EndfieldJiggleInputGuard.dll
```

纯 Mod 包结构相同，但没有 `Configurator`。压缩包内部没有第二层触摸 Mod 文件夹。
不放分类目录、增加分类层级或重命名触摸目录都受支持；配置器会识别实际安装位置。
请完整移动包内文件，勿将 INI 与 `shaders` 分开放置。启用目录下只保留一份触摸运行时。
`Outfits.ini` 与 `OutfitPreparation.ini` 必须使用同一包内的文件。
已有 `EJ-OUTFIT/1` 包装器不需要为本次修复重新适配。

默认开启触摸，启动时不显示状态提示：
按住 `Alt` 加鼠标左键拖动，松开鼠标左键释放；`Ctrl+Shift+F9` 切换开启/关闭，
`Ctrl+Shift+F7` 关闭，`Ctrl+Shift+F8` 切换诊断。
可在配置器中取消“启动时开启触摸”，之后按 `Ctrl+Shift+F9` 开启，再按一次关闭。
Alt 可一直按住，松开左键后重新抓取其他位置。

## 配置器

拖放包已将 EXE 放在触摸目录的 `Configurator/EndfieldJiggleConfigurator.exe`。
仅 Mod 包不含配置器，但附带可选输入模块 EXE。已保存的参数在关闭配置器后仍生效。
参数保存只更新本 Mod 的参数 INI。
换装适配只在确认后修改所选换装的 INI；不编辑游戏本体、注入 DLL 或全局配置。

手动升级采用干净目录，参数先恢复为默认值；内置安装器升级保留可读取的参数。
内置安装器会保留旧按键和启动选项；需要本版 Alt 操作时，在触摸页点击“默认值”再保存。
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
