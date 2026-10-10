# 开发说明

## 公开源码范围

| 目录 | 内容 |
| --- | --- |
| `runtime/Universal.ini` | 通用会话模板，非完整安装配置 |
| `shaders/` | 输入/运动计算、拾取像素输出和形变片段 |
| `third_party/JiggleForge/` | 复用核心及原始许可证、第三方与品牌说明 |
| `tools/` | 适配、签名、ShaderRegex 生成和本地证据校验工具源码 |
| `tests/` | Windows 原生测试宿主与 CMake 配置 |
| `src/EndfieldJiggle.Configurator*` | 安装配置器、原目录换装适配和 QAQM 检查/恢复 |

源代码树不包含生成的完整 `Passes.ini`、游戏原始着色器、完整游戏派生
DXBC、注入 DLL、模型、纹理、本机抓取报告或安装记录。发布页的 Windows
安装 ZIP 另包含可用运行所需的兼容 `Passes.ini` 与缓存；它不是 Git 源码树。
完整构建的配置器 EXE 可嵌入已审核的运行时 ZIP，但不包含替换注入器或第三方换装资源。
未传 `-RuntimeZipPath` 的开发构建仍是无内置运行时的配置器。
来源与验证范围见[PROVENANCE.md](PROVENANCE.md)。

## 独立源码检查

需要 PowerShell 7：

```powershell
pwsh -NoProfile -File .\tools\Test-PublicSource.ps1
```

这条命令检查 PowerShell 语法、模板结构、模块导入和必要源码，
不创建游戏资源、不安装 Mod、不验证实机显示。

原生宿主需要 Windows、CMake 和支持 C++17 的 C++ 工具链：

```powershell
cmake -S tests -B build/native
cmake --build build/native --config Release
```

宿主不是游戏注入器，不应放入游戏目录。运行完整加载或协议场景还需要
自行取得的匹配框架、本地接口资料与测试配置；编译成功不等于这些场景已通过。

构建和测试独立配置器需要 Windows x64 与 .NET 8 SDK：

```powershell
.\tools\Build-Configurator.ps1
.\tools\Test-Configurator.ps1 -ModZipPath 'D:\path\to\EndfieldJiggleEFMI-v0.2.1-win64.zip'
```

测试只把官方 Mod ZIP 解压到 `reports/configurator` 下的隔离目录，验证参数
持久化、备份与还原；不会写入实际 EFMI 或游戏目录。

HLSL 片段可以使用 Windows SDK 的 FXC 编译，头文件搜索路径需要包含
`third_party/JiggleForge`。计算、拾取像素和形变片段分别使用
`cs_5_0`、`ps_5_0` 和 `vs_5_0`，入口为 `main`。

## 为什么不能完整重建

`Build-Universal.ps1` 与 `Universal.Common.psm1` 保留完整本地版本的
证据校验流程，依赖未公开的父构建报告、原生接口适配结果和匹配缓存。
`v0.1.0-source` 源码快照不能独立生成可安装版本；工具不会下载或自动补齐缺失资源。
当前 Windows Mod ZIP 是从已验证本地安装构建出来的独立发行资产。
不要为了通过检查而删除证据约束或伪造构建报告。

v0.2.1 可从 SHA-256 固定的 v0.2.0 安装 ZIP 重建桥接控制层，保留全部
原有兼容着色器字节，不依赖从游戏重新导出：

```powershell
.\tools\Build-Installable.ps1 -BaseModZipPath 'D:\path\to\EndfieldJiggleEFMI-v0.2.0-win64.zip'
.\tools\Test-ReleaseRegression.ps1 -ModZipPath 'D:\path\to\EndfieldJiggleEFMI-v0.2.1-win64.zip'
.\tools\Test-QaqmRecovery.ps1
```

`Build-Installable.ps1 -ConfiguratorZipPath ...` 可同时生成完整包。
`Build-Complete.ps1 -BaseModZipPath ... -OutputDirectory ...` 依次构建内置安装资源的 EXE
和完整包。新 C# 测试项目覆盖安装、换装和状态恢复；输出保存在 `reports`。
QAQM 恢复导出器的 `-EfmiPath` 会跟踪实际明确 include 的状态宿主，
而非仅检查缓存文件是否存在；它不会改写 EFMI 的 include 列表。

设计以渲染接口为适配单位。新接口需要验证输入/输出、常量缓冲区范围、
当前与上一帧投影及拾取路径，而不是把新角色的网格加入白名单。
源码模板中出现的接口处理不构成所有角色支持列表。

## 分发说明

- 分发适用源码或修改版时遵守 GPL-3.0-only，保留必要的许可与来源说明并标明修改。
- 本项目的 GPL 不覆盖游戏素材、其他作者的 Mod 或第三方品牌。
- 不将抓取资源、本机日志、账户信息、安装记录和私人路径加入公开源码或 Release。
- 为版本写明源码版或可安装包；不要用本地完整版本反馈替代发布附件自身的验证。
- 不将普通 ZIP 的时间精度当作游戏派生缓存的完整部署保证。

仓库主分支文档可能比某个历史 Release 更新。版本附件是对应标签的快照，
阅读操作说明时请同时确认自己的版本。
