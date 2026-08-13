# Codex Usage Overlay Lite

面向 Windows Codex 桌面应用的轻量用量悬浮条。它跟随 Codex 窗口显示套餐、用量状态、重置时间、任务状态和可选的公开重置提醒。

## 特性

- 顶部紧凑悬浮条，默认最大宽度 520 px，不遮挡 Codex 左侧组件。
- 从 Codex CLI app-server 读取本地账户与用量信息；读取失败时保留缓存。
- 支持主题、字体、背景色和刷新间隔设置。
- 支持 Windows 10/11、当前用户安装、桌面快捷方式和启动项。
- 提供 `--snapshot` 只读诊断，不输出账户敏感数据到本 README。

## 系统要求

- Windows 10 或 Windows 11
- 系统自带 .NET Framework 4.x
- 已登录的 Codex 桌面应用或官方 Codex CLI

## 构建

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建输出位于 `bin\CodexUsageOverlay.exe`。安装器脚本为 `installer.iss`。

## 连接 Codex

程序会优先寻找当前用户配置的 `CODEX_CLI_PATH`，然后寻找桌面应用随附的 `codex.exe` 或 PATH 中的 CLI。

```powershell
[Environment]::SetEnvironmentVariable(
  "CODEX_CLI_PATH",
  "$env:LOCALAPPDATA\hermes\node\codex.cmd",
  "User"
)
codex.cmd login --device-auth
```

登录和设备授权必须由用户本人完成。不要把密码、令牌、设备码或验证码写入环境变量或提交到仓库。

## 只读诊断

```powershell
.\bin\CodexUsageOverlay.exe --snapshot
```

重点查看 `CodexWindow`、`DataSource` 和错误状态；不要分享套餐额度、累计 Token、邮箱或其他账户数据。

## 安全边界

本工具不读取或修改 `~/.codex/auth.json`，不上传 Codex 会话或对话正文，也不关闭或修改 Codex 本体。网络重置提醒仅使用公开的非官方来源，并不代表 OpenAI 官方承诺。

## 许可证与来源

本项目基于上游 Codex Usage Overlay 的修改版本发布，遵循 GNU Affero General Public License v3.0。原项目和第三方来源说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，完整许可证见 [LICENSE](LICENSE)。

项目地址：[github.com/floretly/CodexUsageOverlay-Lite](https://github.com/floretly/CodexUsageOverlay-Lite)
