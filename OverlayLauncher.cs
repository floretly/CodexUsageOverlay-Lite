using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class OverlayLauncherProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OverlayLauncherForm());
        }
    }

    internal sealed class OverlayLauncherForm : Form
    {
        private readonly string overlayPath;
        private readonly Label statusLabel;
        private readonly Timer statusTimer;

        public OverlayLauncherForm()
        {
            overlayPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CodexUsageOverlay.exe");

            Text = "Codex Usage Overlay Lite";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 250);
            BackColor = Color.FromArgb(245, 248, 252);

            Label title = new Label();
            title.AutoSize = false;
            title.Text = "Codex Usage Overlay";
            title.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(28, 42, 62);
            title.Location = new Point(26, 20);
            title.Size = new Size(370, 34);

            Label description = new Label();
            description.AutoSize = false;
            description.Text = "一键启动、重启和设置悬浮条\r\n打开并聚焦 Codex 后，悬浮条会显示在顶部。";
            description.Font = new Font("Microsoft YaHei UI", 9.5f);
            description.ForeColor = Color.FromArgb(80, 96, 116);
            description.Location = new Point(28, 62);
            description.Size = new Size(370, 46);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            statusLabel.Location = new Point(28, 112);
            statusLabel.Size = new Size(370, 26);

            Button startButton = CreateButton("启动 Overlay", 28, 158, 112);
            startButton.Click += delegate { StartOverlay(); };

            Button restartButton = CreateButton("重启 Overlay", 154, 158, 112);
            restartButton.Click += delegate { RestartOverlay(); };

            Button settingsButton = CreateButton("打开设置", 280, 158, 112);
            settingsButton.Click += delegate { OpenSettings(); };

            Button folderButton = CreateButton("打开目录", 28, 198, 112);
            folderButton.Click += delegate { OpenFolder(); };

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(statusLabel);
            Controls.Add(startButton);
            Controls.Add(restartButton);
            Controls.Add(settingsButton);
            Controls.Add(folderButton);

            statusTimer = new Timer();
            statusTimer.Interval = 1000;
            statusTimer.Tick += delegate { UpdateStatus(); };
            statusTimer.Start();
            UpdateStatus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && statusTimer != null)
                statusTimer.Dispose();
            base.Dispose(disposing);
        }

        private static Button CreateButton(string text, int left, int top, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Font = new Font("Microsoft YaHei UI", 9f);
            button.Location = new Point(left, top);
            button.Size = new Size(width, 32);
            button.FlatStyle = FlatStyle.System;
            return button;
        }

        private void StartOverlay()
        {
            if (!EnsureOverlayExists())
                return;
            if (IsOverlayRunning())
            {
                UpdateStatus();
                return;
            }

            try
            {
                // A previous installation can leave a same-named process alive.
                // The shared mutex would make the new process exit immediately,
                // which looks like the Start button did nothing. Clean up only
                // same-named Overlay processes, never the Codex application.
                StopOverlayProcesses();
                Process.Start(new ProcessStartInfo
                {
                    FileName = overlayPath,
                    WorkingDirectory = Path.GetDirectoryName(overlayPath),
                    UseShellExecute = true
                });
                UpdateStatus();
            }
            catch (Exception exception)
            {
                ShowError("启动 Overlay 失败", exception);
            }
        }

        private void RestartOverlay()
        {
            if (!EnsureOverlayExists())
                return;

            try
            {
                StopOverlayProcesses();
                StartOverlay();
            }
            catch (Exception exception)
            {
                ShowError("重启 Overlay 失败", exception);
            }
        }

        private void OpenSettings()
        {
            if (!EnsureOverlayExists())
                return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = overlayPath,
                    Arguments = "--settings",
                    WorkingDirectory = Path.GetDirectoryName(overlayPath),
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                ShowError("打开设置失败", exception);
            }
        }

        private void OpenFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(overlayPath),
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                ShowError("打开目录失败", exception);
            }
        }

        private bool EnsureOverlayExists()
        {
            if (File.Exists(overlayPath))
                return true;
            MessageBox.Show(this,
                "找不到 CodexUsageOverlay.exe。请重新安装 Overlay。",
                "文件不存在", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private bool IsOverlayRunning()
        {
            Process[] overlays = Process.GetProcessesByName("CodexUsageOverlay");
            try
            {
                foreach (Process overlay in overlays)
                {
                    try
                    {
                        if (!overlay.HasExited && IsCurrentOverlay(overlay))
                            return true;
                    }
                    catch
                    {
                    }
                }
                return false;
            }
            finally
            {
                foreach (Process overlay in overlays)
                    overlay.Dispose();
            }
        }

        private bool IsCurrentOverlay(Process overlay)
        {
            try
            {
                return String.Equals(overlay.MainModule.FileName, overlayPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void StopOverlayProcesses()
        {
            Process[] overlays = Process.GetProcessesByName("CodexUsageOverlay");
            try
            {
                foreach (Process overlay in overlays)
                {
                    try
                    {
                        if (!overlay.HasExited)
                        {
                            overlay.CloseMainWindow();
                            if (!overlay.WaitForExit(1500) && !overlay.HasExited)
                                overlay.Kill();
                        }
                    }
                    catch
                    {
                        // A process that already exited needs no further action.
                    }
                }
            }
            finally
            {
                foreach (Process overlay in overlays)
                    overlay.Dispose();
            }
        }

        private void UpdateStatus()
        {
            bool running = IsOverlayRunning();
            statusLabel.Text = running ? "● 状态：Overlay 正在运行" : "○ 状态：Overlay 未运行";
            statusLabel.ForeColor = running ? Color.FromArgb(23, 139, 78) : Color.FromArgb(120, 92, 40);
        }

        private void ShowError(string title, Exception exception)
        {
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }
    }
}
