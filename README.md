# EndfieldJiggle EFMI

基于 JiggleForge 触摸机制的《明日方舟：终末地》EFMI/XXMI 通用运行时研究。

**v0.1.0-source 是源码研究版，不是可直接安装的 Mod 包。**
本仓库不包含游戏原始着色器、生成的游戏派生 DXBC、角色模型、纹理、
注入 DLL、本机抓取报告或安装记录。下载后直接放进 EFMI 的 `Mods`
目录不会获得可用的触摸功能。

## 当前成果

- 一个共享的鼠标拾取、弹簧运动和空间形变场，不使用逐角色或网格白名单。
- 已适配的原生渲染接口共享形变状态，同时处理当前/上一帧位置和表面方向。
- 兼容的原生颜色绘制经过两帧预热后启用交互；导航和绘制中断会取消交互。
- 本地完整版本通过离线 GPU、原生绘制协议和实际 EFMI 隔离加载检查。
- 2026 年 10 月 4 日，用户反馈本地完整版本“可以，成功迁移”。

这条实机反馈没有提供角色名单或逐部位结果，不代表所有角色、皮肤、
服装、世界场景或第三方换装 Mod 均已验证。公开源码快照与本地完整
安装包不是同一交付物。

## 源码范围

| 目录 | 内容 |
| --- | --- |
| `runtime/Universal.ini` | 通用会话模板；并非独立完整的安装 INI |
| `shaders/` | 输入/运动计算、拾取像素输出和形变片段的 HLSL 源码 |
| `third_party/JiggleForge/` | 复用的输入、运动和形变核心及上游许可证 |
| `tools/` | 原生接口适配、ShaderRegex 生成、签名检查及本地构建工具源码 |
| `tests/` | Windows 原生隔离加载/绘制协议宿主源码 |

`Build-Universal.ps1` 和 `Universal.Common.psm1` 保留本地完整版本的
证据校验逻辑：它们要求未公开的已验收父构建报告、原生接口适配结果和
对应着色器缓存。因此，**本仓库不能独立重建完整可安装包**。
它们不提供游戏资源下载，也不会自动补齐缺失材料。

## 可独立检查的部分

PowerShell 7 下检查公开源码的语法、INI 结构和原生宿主构建：

```powershell
pwsh -NoProfile -File .\tools\Test-PublicSource.ps1
cmake -S tests -B build/native
cmake --build build/native --config Release
```

原生宿主构建需要 Windows、CMake 和 C++ 工具链。构建成功不代表已通过
完整加载测试；运行协议/加载场景还需要自行合法取得的匹配 EFMI 环境、
本地游戏接口数据和测试配置。宿主不是游戏注入器，不应直接放入游戏目录。

HLSL 片段可使用 Windows SDK 的 FXC 单独编译。计算和形变源码的头文件
搜索目录应包含 `third_party/JiggleForge`。测试源码不会部署到游戏。

## 本地完整版本操作

以下按键说明仅适用于另行构建并正确安装的完整版本：

- `Ctrl+Shift+F9`：诊断状态。
- `Ctrl+Shift+F8`：触摸开关，启动时默认关闭。
- `Shift+鼠标左键`：按住、短距离拖动并松开。
- `Ctrl+Shift+F7`：强制关闭触摸。

新着色器接口需要进行接口适配，而不是为每名角色登记白名单。
当前坐标映射针对干员总览；世界场景、透明遮罩、多人重叠和第三方
替换绘制路径尚未验证。公开版没有桌面安装器、滚轮桥接或换装适配器。

## 许可证与来源

原创及复用的 JiggleForge 源码采用 GPL-3.0-only；参见 `LICENSE`、
`THIRD-PARTY-NOTICES.md` 和 `docs/PROVENANCE.md`。
本项目不将游戏代码、资产或其他作者的 Mod 重新许可为 GPL。
不是 JiggleForge、XXMI 或游戏开发商的官方项目。

本次 Release 附带源码 ZIP 和 SHA256 校验文件，不提供二进制安装包。
