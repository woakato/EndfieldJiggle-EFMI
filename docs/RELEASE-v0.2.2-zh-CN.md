# EndfieldJiggle EFMI v0.2.2

## 下载

- `EndfieldJiggleEFMI-v0.2.2-drop-in-win64.zip`：已有 XXMI / EFMI 的用户选此包。包含可选配置器；解压后将 `EndfieldJiggleEFMI` 文件夹放入 EFMI 的 `Mods` 目录即可。配置器无需常驻。
- `EndfieldJiggleEFMI-v0.2.2-standalone-win64.zip`：未安装 XXMI Launcher 的用户选此包。整包解压后打开 `EndfieldJiggleLauncher.exe`，选择游戏本体启动；包含官方 EFMI、配套渲染库、触摸插件和配置器。
- `EndfieldJiggleEFMI-v0.2.2-win64.zip`：纯 Mod 包，不含配置器。

源码请从仓库或源码归档获取。GitHub 自动生成的 `Source code` 压缩包不是游戏安装包。

## 更新内容

- 2026-10-12 补充独立便携包，内置官方 EFMI 1.4.8 与 XXMI-Libs 1.2.2，提供路径选择、启动入口和原有配置器入口。同版触摸运行时和配置器保持一致。
- 更新配置器 Mod 库，支持搜索、停用项筛选和作者预览图。
- 增加逐项绘制选择、参数预设、快捷键录入、JSON 配置导入导出和本机诊断报告。
- 配置器可自动定位 EFMI、Mods 与触摸 Mod 目录，并识别意外重复嵌套的 Mod 文件夹。
- 将目录扫描和配置操作移出界面线程，增加取消、操作日志和耗时记录，降低大型目录处理中界面无响应的风险。
- 诊断页分别显示 EFMI 的 Mods Display 与 `show_original` 绑定；只提示潜在冲突，不修改第三方快捷键。
- 保留 v0.2.1 换装桥接和着色器/缓存文件。

## 安装

退出游戏和 XXMI 后，解压拖放包，将 `EndfieldJiggleEFMI` 整个文件夹放入 EFMI 的 `Mods`。升级前先备份旧目录。纯 Mod 包不含配置器；需要配置器时可从拖放包中取出 `Configurator` 文件夹。

独立包单独解压到可写目录，不覆盖游戏或已有 EFMI。选择 `Endfield.exe` 启动，
后续可双击 `Start.cmd`。详情见[独立版说明](STANDALONE.md)。

## 验证边界

配置器、打包结构、目录定位和隔离安装流程通过本地检查。此次没有重新进行游戏内视觉或同一进程 F10 热重载验证；Ctrl/Shift 导致 EFMI Mods Display 隐藏的实机原因也尚未确认。详见随包的安装与验证说明。

独立包完成程序和打包的隔离检查，首次实际游戏启动、登录和触摸效果仍待用户测试。
详见[独立包验证范围](V022-STANDALONE-VALIDATION.md)。
