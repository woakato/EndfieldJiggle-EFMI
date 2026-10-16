# 原皮触摸黑面的对照采集

适用情况：2026-10-16，用户确认原皮使用首个纯 Mod 候选后仍然发黑。  
本步骤用于定位该渲染问题，与“无法触摸”的独立诊断包无关。  
这份说明尚未在当前游戏内验证；不会自动修改任何配置。

## 准备

1. 退出游戏，保留当前纯 Mod 和参数，不更换材质或其他运行库。
2. 将当前 `D:\mod\xxmi\游戏包\EFMI\d3dx.ini` 备份到 EFMI 的 `Mods`  
   之外，例如本项目 `reports` 下。记录 XXMI 原有的狩猎模式设置。
3. 在 XXMI 的 EFMI 设置中开启狩猎模式 / Shader Hunting。  
   XXMI 会管理 `hunting`，只改 INI 后再启动可能被启动器覆盖。  
   不需要开启“标记时导出着色器”，也不需要开启详细调试或 API 调用日志。
4. 在现有 `d3dx.ini` 对应节中修改以下已有设置，不要在末尾追加重复节：
   ```ini
   [Hunting]
   hunting = 2
   analyse_frame = no_ctrl shift no_alt VK_F11
   analyse_options = mono

   [Rendering]
   export_shaders = 1

   [Logging]
   calls = 0
   debug = 0
   ```
   保留每个节内的其他设置。`mono` 没有选取 `dump_rt`、`dump_tex`、  
   `dump_cb`、`dump_vb` 或 `dump_ib`，本轮先采单帧调用日志。  
   `export_shaders = 1` 则独立导出本次运行期间遇到的着色器，  
   不限于按键选中的三个帧。只进行这次短测试，不开着导出长期游玩。
5. 重新通过当前正常的启动流程进入 DirectX 11 游戏。  
   从 `hunting = 0` 启动的游戏可能没有帧分析上下文，仅 F10 重载不够。

## 三个状态

保持同一原版角色、同一场景、镜头、光照和触摸参数。  
打开 Num Lock，单按一次**小键盘 0**，确认狩猎信息出现。  
不要按小键盘 1 至 9 逐项隐藏绘制，也不要导出 ShaderFixes 替换文件。

1. 关闭触摸，画面正常时，点按一次 `Shift+F11`。
2. 开启触摸但不拖动，再点按一次 `Shift+F11`。
3. 按住触摸拖动组合键，黑面出现时保持左键和 Shift，点按一次 `F11`，  
   随即松开 F11。不要同时按 Ctrl 或 Alt。

每次等待导出完成，并间隔至少两秒，避免按秒命名的输出目录重名。  
记录每次截图及对应目录时间。另记松开拖动后黑面是否消失。  
如果狩猎界面未出现，或提示缺少 Frame Analysis Context，停止按键，  
先检查是否已经开启狩猎并完整重启；不要通过开启全量 debug 来解决。

## 交付与恢复

新日志通常位于配置的 EFMI 根目录下 `FrameAnalysis-日期-时间` 文件夹。  
保留本次新增的 `ShaderCache` / `ShaderFromGame` 着色器文件。  
不要清空旧缓存，不要把旧抓帧混作本次证据。

完成后退出游戏，将 `export_shaders` 恢复为原值 `0`，关闭 XXMI 的狩猎模式。  
其他临时设置可从本次备份逐项恢复；若配置期间另有修改，不要整份覆盖。  
告知三个目录的时间和状态，维护者可直接读取本机文件，不必上传全部目录。

本轮日志若不足以定位，会按已识别的绘制定向补采资源。  
本步骤不承诺已经取得全部深度、阴影或材质数据，也不代表黑面已经修复。

## 核对依据

- 当前本机 `d3dx.ini`：小键盘 0 切换狩猎，Shift+F11 分析一帧；  
  核对时 `hunting = 0`，原 `analyse_options` 含全量纹理及多类缓冲区。
- 本机 DLL 的 SHA-256 与已核验 XXMI-Libs 1.2.2 一致：  
  `EA97846AF4610B9AF42BEDDCCD7F22A5E73E85B25669CD4391EA8000962FDD7D`。
- 对应上游配置说明：  
  <https://github.com/SpectrumQT/XXMI-Libs-Package/blob/a7847eae82aaac32175c9a7d199673d5c9323bcd/Dependencies/d3dx.ini>
- 已读取对应版本的帧分析资源选择与触发实现：  
  <https://github.com/SpectrumQT/XXMI-Libs-Package/blob/a7847eae82aaac32175c9a7d199673d5c9323bcd/DirectX11/FrameAnalysis.cpp>  
  <https://github.com/SpectrumQT/XXMI-Libs-Package/blob/a7847eae82aaac32175c9a7d199673d5c9323bcd/DirectX11/Hunting.cpp>
- XXMI 的 EFMI 设置映射：  
  <https://github.com/SpectrumQT/XXMI-Launcher/blob/main/src/xxmi_launcher/core/packages/model_importers/efmi_package.py>
