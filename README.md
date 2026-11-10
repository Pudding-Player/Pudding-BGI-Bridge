<div align="center">

<img src="https://raw.githubusercontent.com/Pudding-Player/Pudding/main/assets/brand/avatar.png" width="72" alt="Pudding" />

# Pudding BetterGI Bridge

[Pudding](https://github.com/Pudding-Player/Pudding) 接入 BetterGI（原神）的桥与配套插件。

[![License: GPL v3](https://img.shields.io/badge/License-GPL--3.0--only-blue.svg)](LICENSE) ![Platform](https://img.shields.io/badge/Platform-Windows-0078D6.svg) ![Language](https://img.shields.io/badge/.NET%208-blueviolet.svg)

[Pudding 主仓](https://github.com/Pudding-Player/Pudding) · [BetterGI](https://github.com/babalae/better-genshin-impact) · [桥说明](bgi-bridge/README.md)

</div>

> 本仓库是 Pudding 的适配桥，不是 BetterGI 本体；BetterGI 由其原项目维护。

## 能力

- **原生引导与托管桥**（`bgi-bridge/`）：随 BetterGI 进程运行的原生组件与托管桥，执行 Pudding 提交的任务与配置修改。
- **恢复工具**：核对 BetterGI 配置备份并在宿主未运行时恢复，操作前先备份当前文件。
- **插件**（`plugins/bgi/`）：供 Pudding 使用的工具清单与领域技能，Pudding 据此发现和调用 BetterGI 能力。

## 接入

主仓通过 `scripts/bridges.lock.json` 锁定本仓提交，构建脚本自动获取并组装；BetterGI 原生组件随安装包放入安装目录 `bridge/`。源码只保存空凭据模板，本地 `bridge.config.json` 不参与安装包。桥不自动启动 BetterGI 或游戏。

## 构建

Windows 需要 .NET 8 SDK 和 Visual Studio 2022 C++ Build Tools：

```cmd
bgi-bridge\build.cmd
```

产物位于 `target/bridge/`，中间文件位于 `target/dotnet/`。

桥协议、组件布局与验证边界的完整说明见[桥说明](bgi-bridge/README.md)，插件清单见 [`plugins/bgi/.pudding-plugin/plugin.json`](plugins/bgi/.pudding-plugin/plugin.json)。

## 许可

本仓库以 [GNU GPL v3.0（GPL-3.0-only）](LICENSE) 发布，Copyright © 2026 Pudding contributors。BetterGI 与第三方依赖分别遵循其原许可与资源使用条件。
