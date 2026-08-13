# Codex Usage Overlay Lite 安装提示

请在 Windows 10/11 上安装或更新 Codex Usage Overlay Lite。

项目地址：
https://github.com/floretly/CodexUsageOverlay-Lite

要求：

- 使用 HTTPS 下载并校验安装包。
- 不修改 Windows 安全策略。
- 不读取或修改 `~/.codex/auth.json`。
- 不上传 Codex 会话、对话正文、邮箱、Token 或其他账户数据。
- 不关闭或修改 Codex 本体；覆盖安装时只允许安装器关闭旧的 Overlay 进程。
- 安装后运行 `CodexUsageOverlay.exe --snapshot`，只报告 CodexWindow、DataSource 和是否存在错误。

如果 Codex CLI 未登录，提示用户本人执行：

```powershell
codex.cmd login --device-auth
```

不要代填密码、令牌、设备码或验证码。
