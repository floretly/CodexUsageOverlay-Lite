# Codex Usage Overlay Lite

一个面向 Windows Codex 桌面应用的轻量级用量悬浮条。它会跟随 Codex 窗口，在顶部显示常用状态，让你不用离开当前会话就能了解 Codex 的使用情况。

本项目是私有仓库中的个人衍生版本，重点是：界面紧凑、信息直接、安装简单、默认不遮挡 Codex 左侧导航和工作区。

## 产品介绍

Codex Usage Overlay Lite 运行在 Windows 桌面上，不修改 Codex 主程序。它通过 Codex CLI 的本地 `app-server` JSON-RPC 接口读取可验证的账户和用量信息，并将结果绘制成一个置顶的无边框悬浮条。

当 Codex 没有打开、窗口被最小化或当前没有聚焦时，悬浮条会自动隐藏；重新聚焦 Codex 后会恢复显示。如果实时接口暂时不可用，程序会保留最近一次可信的本地缓存，并在诊断结果中标明数据来源。

## 功能

### 顶部用量悬浮条

- 跟随 Codex 窗口移动、最大化和高 DPI 显示变化。
- 默认最大宽度为 680 px，左侧位置保持紧凑布局，新增空间主要向右扩展，减少对 Codex 左侧组件的遮挡。
- 显示套餐、5 小时额度剩余及重置时间、周用量剩余及周重置时间和可用重置券。
- 可右键主用量区域退出 Overlay；不会关闭 Codex。
- 雷达提示关闭后会记住当前公告；只有出现新的公告时才会再次显示，可点击主栏右侧雷达按钮手动重新打开。

### 重置提醒

- 可选显示公开来源的重置提醒卡片。
- 对来源、时间格式和日期范围进行校验，只接受符合规则的数据。
- 可选择是否启用 Windows 通知。
- 该提醒是非官方信息，不代表 OpenAI 承诺，也不保证每个账户同时生效。

### 外观设置

通过悬浮条右侧齿轮打开设置面板，可调整：

- 字体：Microsoft YaHei UI、Segoe UI、SimSun 或 Arial 等安全字体。
- 主题：荧光蓝、磨砂玻璃、渐变橙、渐变粉、自定义背景和彩色文字。
- 自定义背景颜色。
- 自动刷新间隔：5–3600 秒，默认 15 秒。
- 重置提醒通知开关。

设置保存在 `%LOCALAPPDATA%\Codex Usage Overlay Lite\settings.ini`，覆盖更新时会尽量保留已有设置。

## 系统要求

- Windows 10 或 Windows 11
- 系统自带 .NET Framework 4.x
- 已安装并登录的 Codex 桌面应用，或已登录的官方 Codex CLI
- 当前用户对 `%LOCALAPPDATA%` 有写入权限

不需要管理员权限，不需要修改 PowerShell 执行策略，也不需要关闭 Windows 安全功能。

## 安装

### 使用安装包

从本仓库的 [Releases](https://github.com/floretly/CodexUsageOverlay-Lite/releases) 下载当前安装包：

直接下载：
https://github.com/floretly/CodexUsageOverlay-Lite/releases/download/v1.0.7/CodexUsageOverlay-Lite-Setup-1.0.7.exe

```text
CodexUsageOverlay-Lite-Setup-1.0.7.exe
```

SHA-256 校验值在同一 Release 的 `SHA256SUMS.txt` 中。

安装包使用当前用户安装，默认路径为：

```text
%LOCALAPPDATA%\Programs\Codex Usage Overlay Lite\CodexUsageOverlay.exe
```

安装程序会：

- 创建桌面快捷方式；
- 创建当前用户启动目录中的自动启动快捷方式；
- 启动 Overlay；
- 支持使用新安装包覆盖更新旧版本。

静默安装参数：

```powershell
.\CodexUsageOverlay-Lite-Setup-1.0.7.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS
```

### 从源码构建

项目使用系统自带 .NET Framework 4.x 编译器：

```powershell
cd .\CodexUsageOverlay-Lite
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建输出：

```text
bin\CodexUsageOverlay.exe
```

构建时还会生成 `bin\CodexUsageOverlayLauncher.exe`。安装后可从桌面打开“Codex Overlay 控制面板”，通过按钮启动 Overlay、重启 Overlay、打开设置或打开安装目录，不需要手动输入 PowerShell 命令。控制面板只管理 Overlay 进程，不会关闭 Codex 桌面应用。

如需生成 Inno Setup 安装包，使用 Inno Setup 编译 `installer.iss`。安装器脚本不会请求管理员权限。

## 首次使用

### 1. 打开并登录 Codex

先启动 Codex 桌面应用并在 Codex 自己的界面完成登录。保持 Codex 窗口打开并聚焦。

### 2. 确认 CLI

在 PowerShell 中检查：

```powershell
Get-Command codex.exe,codex.cmd -ErrorAction SilentlyContinue
```

如果 CLI 尚未登录，由用户本人执行：

```powershell
codex.cmd login --device-auth
```

设备授权、密码、验证码和设备码必须由用户本人完成，不要将它们发给任何人或写入环境变量。

### 3. 指定 CLI 路径（可选）

如果电脑上同时存在多个 Codex CLI，建议指定实际使用的路径。例如：

```powershell
[Environment]::SetEnvironmentVariable(
  "CODEX_CLI_PATH",
  "$env:LOCALAPPDATA\hermes\node\codex.cmd",
  "User"
)
```

设置后关闭并重新打开 PowerShell，再重启 Overlay：

```powershell
Stop-Process -Name CodexUsageOverlay -Force -ErrorAction SilentlyContinue
Start-Process "$env:LOCALAPPDATA\Programs\Codex Usage Overlay Lite\CodexUsageOverlay.exe"
```

`CODEX_CLI_PATH` 只应包含可执行文件路径，不要在其中写入密码、Token 或设备码。

## 验证连接

运行只读快照：

```powershell
& "$env:LOCALAPPDATA\Programs\Codex Usage Overlay Lite\CodexUsageOverlay.exe" --snapshot
```

重点查看以下三项：

```text
CodexWindow=found
DataSource=Codex CLI app-server
Error=none
```

理想状态是 `CodexWindow=found`、`DataSource=Codex CLI app-server` 且 `Error=none`。

如果显示 `DataSource=缓存`，表示实时接口尚未成功读取，程序正在使用最近一次可信缓存；如果显示 `Error=present`，请检查 CLI 登录状态、`CODEX_CLI_PATH` 和网络连接。

## 常见问题

### 悬浮条没有出现

确认：

1. Codex 桌面应用正在运行；
2. Codex 窗口没有最小化；
3. 当前点击并聚焦了 Codex 窗口；
4. `CodexUsageOverlay.exe` 进程存在；
5. 不是在设置面板或快照命令运行后误判了显示状态。

如果同时打开了多个 Codex/ChatGPT 窗口，请在需要显示悬浮条的那个窗口内点击一次；Overlay 会跟随当前真正获得焦点的窗口。桌面上的“Codex Overlay 控制面板”只是管理工具，点击“启动 Overlay”后才会启动实际悬浮条进程。

### 只能看到缓存

依次检查：

```powershell
Test-Path "$env:LOCALAPPDATA\hermes\node\codex.cmd"
[Environment]::GetEnvironmentVariable("CODEX_CLI_PATH", "User")
```

确认 CLI 已登录，再重启 Overlay 并重新执行 `--snapshot`。

### 登录时报网络错误

这是 Codex CLI 到登录服务的网络问题，不是 Overlay 的 UI 问题。请检查本人使用的网络或代理是否允许访问 OpenAI 登录服务，然后由用户本人重新执行：

```powershell
codex.cmd login --device-auth
```

不要为了登录而放宽 Windows 安全策略。

### 如何关闭 Overlay

可以右键主用量区域退出，也可以执行：

```powershell
Stop-Process -Name CodexUsageOverlay -Force
```

这只会关闭 Overlay，不会关闭 Codex。

## 隐私与安全边界

- 不读取或修改 `~/.codex/auth.json`。
- 不上传 Codex 会话、对话正文、邮箱、密码、Token 或设备授权信息。
- 不把凭据写入 `CODEX_CLI_PATH` 或其他环境变量。
- `--snapshot` 只输出 `CodexWindow`、`DataSource` 和错误存在性，不写入套餐、额度、邮箱或 Token 字段。
- 不读取 `.codex\sessions`，不扫描或保存 Codex 对话正文。
- 重置提醒只读公开的非官方来源；网络失败时使用本地缓存。
- 不修改 Codex 本体，不需要管理员权限。

## 许可证与来源

本项目是基于上游 Codex Usage Overlay 的修改版本，遵循 GNU Affero General Public License v3.0。请保留 [LICENSE](LICENSE) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。第三方来源、适配说明和许可证要求以这两个文件为准。

仓库为私有仓库：

https://github.com/floretly/CodexUsageOverlay-Lite
