EndfieldJiggle EFMI v0.2.7c（预发布）

安装
1. 退出游戏与 XXMI，将旧触摸目录备份到 Mods 之外。
2. 将本包完整解压到 EFMI/Mods 下一个独立目录，INI 与 shaders 保持相邻。
   支持中文、分类及多层目录；启用目录下只保留一份触摸 Mod。
3. 通过现有 EFMI / XXMI 启动游戏。纯 Mod 和拖放包不附带 EFMI 或渲染库；
   需要更新它们时请通过官方 XXMI Launcher 更新。

操作
按住 Alt + 鼠标左键拖动，松开左键释放。触摸默认开启。
Ctrl+Shift+F9 切换触摸，Ctrl+Shift+F7 关闭，Ctrl+Shift+F8 诊断。
需要防止拖动时操作镜头：游戏启动后运行
InputGuard/EndfieldJiggleInputGuard.exe，每次会话运行一次并保持运行。
长按 Alt 隔离镜头位移，左键可用于屏幕 UI；松开恢复，Esc 临时解除。
若松开 Alt 时左键仍按住，直到这次左键抬起才恢复镜头位移。
右键和滚轮仍受隔离；结束后台程序可关闭隔离。剧情时间与自动运镜不冻结。

配置与修复
拖放包含 Configurator/EndfieldJiggleConfigurator.exe，可调整参数及适配换装；
保存后可关闭。纯 Mod 包不含配置器。
诊断页可核对 XXMI 配置指向的 EFMI 目录、输入设置、运行文件及换装。
复现问题后保持游戏运行，导出诊断报告。发现多份启动器配置时请选择本次使用的那份。
QAQM 仅在换装引用它时专项检查，不是其他换装的必要条件。
不能拖动时，退出游戏及 XXMI，打开配置器“安装与恢复 → 修复目录”，
检查预览后执行。纯 Mod 用户可将旧触摸目录备份到 Mods 外后重新完整安装。
配置器安装、升级和恢复有备份；手动解压升级请先保存原目录与参数。

这是非官方社区 Mod，不保证所有角色、剧情或游戏更新均兼容。
本版尚未实机确认空白处 Alt 拖动与 UI 点击，使用前备份原触摸目录。
许可证与第三方来源见 Licenses；源码和完整说明见项目主页。
项目：https://github.com/woakato/EndfieldJiggle-EFMI
