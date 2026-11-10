# Pudding BetterGI Bridge

独立维护 BetterGI 原生引导、托管桥、恢复工具，以及配套 Pudding 插件。

- [桥源码与说明](bgi-bridge/README.md)
- [插件清单](plugins/bgi/.pudding-plugin/plugin.json)
- [Pudding 主仓](https://github.com/Pudding-Player/Pudding)

Windows 需要 .NET 8 SDK 和 Visual Studio 2022 C++ Build Tools。执行 `bgi-bridge\build.cmd`，产物位于 `target/bridge/`，中间文件位于 `target/dotnet/`。源码中只保存空凭据模板；现有本地 bridge.config.json 不参与安装包。

主仓通过 scripts/bridges.lock.json 锁定本仓提交，构建脚本自动拉取并组装。独立使用时仍须遵守桥协议、来源限制和调用授权；编译不表示宿主或游戏执行已验收。
