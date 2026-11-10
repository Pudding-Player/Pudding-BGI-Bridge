# Pudding bgi 桥仓

本仓库独立维护桥源码、插件清单、技能和固定生成输入。与 Pudding 主仓通过 localhost HTTP 契约集成；不复制第三方工具或用户数据。

- 正式源码只有当前检出一份，不创建副本或 worktree。
- 编译输出只进入本仓库 target/；主仓组装时可通过 PUDDING_BUILD_OUTPUT 指定主仓 target/bridge/ 下的对应目录。
- 临时文件使用 target/tmp/，或显式传入的 PUDDING_BUILD_TMP；禁止写系统 Temp。
- 独立构建见 README；原生桥使用 --production，只编译产品代码，不运行宿主或游戏。
- 不主动新增或运行测试；编译、静态契约核对与实际宿主验收分别说明。
- 不提交 bridge.config.json、令牌、模型配置、数据库或日志。
- 本维护工作站删除任务资源必须使用 E:\tools\Remove-Directory.ps1，先核对绝对路径与归属。
