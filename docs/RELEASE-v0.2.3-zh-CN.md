# v0.2.3 原皮黑面修复与目录结构更新

补齐当前游戏原皮颜色绘制的触摸适配，使皮肤、预处理与描边同步变形。
纯 Mod、drop-in 和普通独立版使用相同的修复运行文件。

## 选择下载

| 文件 | 用途 |
| --- | --- |
| `EndfieldJiggleEFMI-v0.2.3-win64.zip` | 已有 EFMI，只需触摸 Mod |
| `EndfieldJiggleEFMI-v0.2.3-drop-in-win64.zip` | 已有 EFMI，另含可选配置器 |
| `EndfieldJiggleEFMI-v0.2.3-standalone-win64.zip` | 包含启动器、官方 EFMI、运行库、触摸 Mod 和配置器 |
| `EndfieldJiggleEFMI-v0.2.3-source.zip` | 项目源码，不是游戏安装包 |

纯 Mod 与 drop-in ZIP 内直接包含 `shaders`、`EndfieldJiggle.ini`、
`Passes.ini` 和 `Outfits.ini`。解压到 `Mods` 下一个独立目录即可，例如：

```text
EFMI/Mods/EndfieldJiggleEFMI-v0.2.3-drop-in-win64/shaders/
EFMI/Mods/EndfieldJiggleEFMI-v0.2.3-drop-in-win64/EndfieldJiggle.ini
```

也可使用原来的 `Mods/EndfieldJiggleEFMI` 目录名。不要把文件散放在 `Mods` 根目录。
drop-in 额外包含 `Configurator`；普通独立版保留完整的启动器与 `EFMI/Mods` 结构。

## 更新内容

- 新增缺失的原皮颜色绘制适配，保留原生材质指令及现有拾取行为。
- 修正零位移时不必要的表面方向变化，以及大坐标下的形变导数精度。
- 配置器兼容新增绘制与扁平包结构；识别、升级和恢复实际带版本号的触摸目录，
  避免另建一份运行时。内嵌安装文件同步更新。
- 普通独立版包含此前的官方启动器等待流程、进程路径查询及缓存路径长度检查。
- 保留原有十二道换装桥接；不代表新增了任意第三方换装的兼容性。

升级前退出游戏，将旧触摸目录备份到 `Mods` 之外，再解压新包并重启游戏。
参数可提前用配置器导出，之后导入；不要用旧 INI 覆盖新版本。
首次安装仍默认关闭触摸：`Ctrl+Shift+F8` 开关，`Shift+鼠标左键` 拖动。

黑面修复运行文件通过 226 项静态/GPU/加载检查和 102 项实际模型顶点回放检查，
并获用户接受发布。独立版启动、其他干员、材质、换装和场景仍需分别验证。
“无法触摸”的独立诊断包维持单独用途，本次未将它替换为黑面修复包。
