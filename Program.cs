using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppDataPaths.MigrateLegacyFiles();
            bool snapshot = Array.IndexOf(args, "--snapshot") >= 0;
            bool radarSnapshot = Array.IndexOf(args, "--reset-radar-snapshot") >= 0;
            bool settingsOnly = Array.IndexOf(args, "--settings") >= 0;
            string previewOutput = null;
            const string previewPrefix = "--export-theme-previews=";
            foreach (string argument in args)
            {
                if (argument.StartsWith(previewPrefix, StringComparison.OrdinalIgnoreCase))
                    previewOutput = argument.Substring(previewPrefix.Length).Trim('"');
            }
            if (snapshot || radarSnapshot)
                AttachDiagnosticConsole();

            if (radarSnapshot)
            {
                using (ResetRadarService radarService = new ResetRadarService())
                {
                    bool refreshed = radarService.RefreshNow();
                    ResetRadarData radar = radarService.Snapshot();
                    string[] report = new[]
                    {
                        "RadarStatus=" + radar.Status.ToString(),
                        "RadarLabel=" + radar.StatusLabel,
                        "RadarDetail=" + radar.Detail,
                        "RadarScope=" + radar.ScopeLabel,
                        "RadarEventKind=" + radar.EventKind,
                        "RadarPostId=" + radar.EvidencePostId,
                        "RadarSourceUrl=" + radar.SourceUrl,
                        "RadarConfidence=" + (radar.Confidence.HasValue ? radar.Confidence.Value.ToString("0.####", CultureInfo.InvariantCulture) : String.Empty),
                        "RadarNetworkAvailable=" + radar.NetworkAvailable.ToString(CultureInfo.InvariantCulture),
                        "RadarLastError=" + radar.LastError
                    };
                    foreach (string line in report) Console.WriteLine(line);
                    File.WriteAllLines(AppDataPaths.GetFile("reset-radar-snapshot.txt"), report, new UTF8Encoding(false));
                    return refreshed ? 0 : 1;
                }
            }

            OverlaySettings settings = OverlaySettingsStore.Load();
            using (UsageService service = new UsageService())
            {
                if (!String.IsNullOrWhiteSpace(previewOutput))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (OverlayForm form = new OverlayForm(service, settings))
                        form.ExportThemePreviews(previewOutput);
                    return 0;
                }

                if (settingsOnly)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (SettingsForm form = new SettingsForm(settings))
                    {
                        if (form.ShowDialog() == DialogResult.OK)
                            OverlaySettingsStore.Save(form.SelectedSettings);
                    }
                    return 0;
                }

                if (snapshot)
                {
                    IntPtr window = CodexWindow.Find();
                    bool refreshed = service.RefreshNow();
                    UsageData data = service.Snapshot();
                    string[] report = new[]
                    {
                        String.Format(CultureInfo.InvariantCulture, "CodexWindow={0}", window != IntPtr.Zero ? "found" : "not-found"),
                        "DataSource=" + data.Source,
                        "Error=" + (String.IsNullOrWhiteSpace(data.LastError) ? "none" : "present")
                    };
                    foreach (string line in report) Console.WriteLine(line);
                    File.WriteAllLines(AppDataPaths.GetFile("snapshot.txt"), report, new UTF8Encoding(false));
                    return refreshed ? 0 : 1;
                }

                bool created;
                using (Mutex mutex = new Mutex(true, "Local\\CodexUsageOverlay-7E2EBB20", out created))
                {
                    if (!created)
                        return 0;

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new OverlayForm(service, settings));
                }
            }
            return 0;
        }

        private static void AttachDiagnosticConsole()
        {
            NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
            try
            {
                Stream output = Console.OpenStandardOutput();
                Console.SetOut(new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true });
            }
            catch
            {
                // The report is also written to the private app-data directory.
            }
        }
    }

    internal sealed class UsageService : IDisposable
    {
        private readonly object sync = new object();
        private readonly string cachePath;
        private readonly CodexAppServerClient appServer = new CodexAppServerClient();
        private UsageData data;
        private DateTime lastRefreshUtc = DateTime.MinValue;
        private bool refreshRunning;

        public UsageService()
        {
            cachePath = AppDataPaths.GetFile("usage-cache.ini");
            data = CacheStore.Load(cachePath);
        }

        public UsageData Snapshot()
        {
            lock (sync)
                return data.Clone();
        }

        public void RequestRefresh(int refreshSeconds, bool force)
        {
            refreshSeconds = Math.Max(5, Math.Min(3600, refreshSeconds));
            bool shouldStart = false;
            lock (sync)
            {
                if (!refreshRunning && (force || (DateTime.UtcNow - lastRefreshUtc).TotalSeconds >= refreshSeconds))
                {
                    refreshRunning = true;
                    lastRefreshUtc = DateTime.UtcNow;
                    shouldStart = true;
                }
            }
            if (!shouldStart)
                return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try { RefreshNow(); }
                finally
                {
                    lock (sync)
                        refreshRunning = false;
                }
            });
        }

        public bool RefreshNow()
        {
            UsageData current;
            if (appServer.TryReadUsage(out current))
            {
                current.LastError = String.Empty;
                Merge(current);
                return true;
            }
            lock (sync)
            {
                data.LastError = appServer.LastError ?? String.Empty;
            }
            return false;
        }

        private void Merge(UsageData incoming)
        {
            lock (sync)
            {
                bool changed = UsageDataMerger.MergeInto(data, incoming);
                if (changed)
                {
                    data.UpdatedUtc = DateTime.UtcNow;
                    CacheStore.Save(cachePath, data);
                }
            }
        }

        public void Dispose()
        {
            appServer.Dispose();
        }
    }

    internal sealed class OverlayForm : Form
    {
        private readonly UsageService service;
        private readonly ResetRadarService resetRadarService;
        private readonly System.Windows.Forms.Timer timer;
        private OverlaySettings settings;
        private IntPtr codexWindow = IntPtr.Zero;
        private string displayText = "Codex 用量正在载入";
        private string lastRenderedText = String.Empty;
        private Rectangle lastRenderedBounds = Rectangle.Empty;
        private bool settingsExpanded;
        private bool gearHovered;
        private bool gearPressed;
        private bool radarHovered;
        private OverlaySettings draftSettings;
        private readonly string[] fontOptions;
        private readonly NotifyIcon resetNotifyIcon;
        private readonly GitHubReleaseUpdateService releaseUpdateService;
        private readonly NotifyIcon releaseUpdateNotifyIcon;
        private readonly ResetRadarBannerForm resetRadarBanner;
        private ResetRadarData resetRadar = new ResetRadarData();
        private string lastRadarRevision = String.Empty;
        private string lastRadarClockRevision = String.Empty;
        private string notificationSourceUrl = String.Empty;
        private string releaseUpdateUrl = String.Empty;
        private string lastReleaseUpdateRevision = String.Empty;
        private DateTimeOffset? resetRadarDisplayNow;
        private float dpiScale = 1f;
        private bool radarBannerDismissed;
        private bool radarRefreshRequested;
        private DateTime radarRefreshRequestedUtc;
        private string settingsRevision;
        private bool rightDownStartedInMainUsage;

        private const int HeaderHeight = 28;
        private const int ExpandedHeight = 236;
        // Keep the left edge close to the old compact layout while allowing the
        // right side to expand enough for the reset time and quota details.
        private const int CompactOverlayWidth = 520;
        private const int MaxOverlayWidth = 680;
        private const string RunwayPageUrl = "https://www.codexrunway.com/zh.html";

        public OverlayForm(UsageService service, OverlaySettings settings)
        {
            this.service = service;
            this.settings = settings;
            settingsRevision = OverlaySettingsStore.GetRevision();
            fontOptions = BuildFontOptions(settings.FontName);
            resetRadarService = new ResetRadarService();
            resetRadar = resetRadarService.Snapshot();
            resetNotifyIcon = new NotifyIcon();
            resetNotifyIcon.Icon = SystemIcons.Information;
            resetNotifyIcon.Text = "Codex · 重置雷达";
            resetNotifyIcon.BalloonTipClicked += delegate { OpenExternalUrl(notificationSourceUrl); };
            resetNotifyIcon.DoubleClick += delegate { OpenRadarSource(); };
            releaseUpdateService = new GitHubReleaseUpdateService();
            releaseUpdateNotifyIcon = new NotifyIcon();
            releaseUpdateNotifyIcon.Icon = SystemIcons.Information;
            releaseUpdateNotifyIcon.Text = "Codex Usage Overlay Lite 更新";
            releaseUpdateNotifyIcon.BalloonTipClicked += delegate { OpenReleaseUpdate(); };
            releaseUpdateNotifyIcon.DoubleClick += delegate { OpenReleaseUpdate(); };
            resetRadarBanner = new ResetRadarBannerForm(OpenRunwayPage, DismissRadarBanner);
            ApplyNotificationVisibility();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = MaxOverlayWidth;
            Height = 30;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 250;
            timer.Tick += OnTick;
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                resetRadarService.Dispose();
                releaseUpdateService.Dispose();
                resetRadarBanner.Dispose();
                resetNotifyIcon.Visible = false;
                resetNotifyIcon.Dispose();
                releaseUpdateNotifyIcon.Visible = false;
                releaseUpdateNotifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE |
                    NativeMethods.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        private void OnTick(object sender, EventArgs e)
        {
            ReloadSettingsIfChanged();
            CheckForReleaseUpdate();
            resetRadarService.RequestRefresh(false);
            ResetRadarData latestRadar = resetRadarService.Snapshot();
            bool radarChanged = !String.Equals(latestRadar.RevisionKey, lastRadarRevision, StringComparison.Ordinal);
            if (radarChanged)
            {
                resetRadar = latestRadar;
                lastRadarRevision = latestRadar.RevisionKey;
                radarRefreshRequested = false;
            }
            else if (radarRefreshRequested &&
                DateTime.UtcNow - radarRefreshRequestedUtc >= TimeSpan.FromSeconds(30))
            {
                radarRefreshRequested = false;
            }
            if (settings.ResetNotificationsEnabled)
            {
                ResetRadarNotification notification;
                if (resetRadarService.TryCreateNotification(out notification))
                    ShowResetNotification(notification);
            }

            // Keep the local usage cache fresh even while the overlay is hidden.
            // Visibility follows the focused Codex window, but synchronization
            // must not stop just because another application has focus.
            service.RequestRefresh(settings.RefreshSeconds, false);

            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (CodexWindow.IsCandidate(foregroundWindow))
            {
                // Codex can expose more than one visible ChatGPT window. Follow the
                // one that is actually focused instead of keeping the first window
                // returned by EnumWindows, which may make the overlay disappear.
                codexWindow = foregroundWindow;
            }
            else if (codexWindow == IntPtr.Zero || !NativeMethods.IsWindow(codexWindow))
            {
                codexWindow = CodexWindow.Find();
            }

            if (codexWindow == IntPtr.Zero || NativeMethods.IsIconic(codexWindow) ||
                !NativeMethods.IsWindowVisible(codexWindow) ||
                (!settingsExpanded && NativeMethods.GetForegroundWindow() != codexWindow))
            {
                resetRadarBanner.HideBanner();
                Hide();
                return;
            }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(codexWindow, out rect))
            {
                resetRadarBanner.HideBanner();
                Hide();
                return;
            }

            NativeMethods.RECT visibleRect;
            if (NativeMethods.TryGetVisibleWindowRect(codexWindow, out visibleRect))
                rect = visibleRect;

            int windowWidth = rect.Right - rect.Left;
            float newDpiScale = NativeMethods.GetWindowDpiScale(codexWindow);
            bool dpiChanged = Math.Abs(newDpiScale - dpiScale) > 0.01f;
            dpiScale = newDpiScale;
            int availableWidth = Math.Max(ScalePixels(240), windowWidth - ScalePixels(32));
            int overlayWidth = Math.Min(ScalePixels(MaxOverlayWidth), availableWidth);
            int compactWidth = Math.Min(ScalePixels(CompactOverlayWidth), availableWidth);
            int overlayLeft = rect.Left + (windowWidth - compactWidth) / 2;
            if (overlayLeft + overlayWidth > rect.Right)
                overlayLeft = Math.Max(rect.Left, rect.Right - overlayWidth);
            int titleBarHeight = ScalePixels(36);
            int overlayHeight = ScalePixels(settingsExpanded ? ExpandedHeight : HeaderHeight);
            Screen targetScreen = Screen.FromHandle(codexWindow);
            int visibleTitleBarTop = Math.Max(rect.Top, targetScreen.Bounds.Top);
            int overlayTop = visibleTitleBarTop + (titleBarHeight - ScalePixels(HeaderHeight)) / 2;
            bool radarEventDismissed = !String.IsNullOrWhiteSpace(resetRadar.EvidencePostId) &&
                String.Equals(settings.DismissedRadarEventId, resetRadar.EvidencePostId, StringComparison.Ordinal);
            bool showRadarBanner = !settingsExpanded && !radarBannerDismissed && !radarEventDismissed &&
                ResetRadarBannerForm.ShouldShow(resetRadar);
            int radarBannerHeight = ScalePixels(ResetRadarBannerForm.LogicalHeight);
            int radarBannerGap = ScalePixels(ResetRadarBannerForm.LogicalGap);
            int radarBannerWidth = Math.Min(overlayWidth, ScalePixels(ResetRadarBannerForm.LogicalWidth));
            int radarBannerLeft = overlayLeft + (overlayWidth - radarBannerWidth) / 2;
            int radarBannerTop = overlayTop - radarBannerHeight - radarBannerGap;
            Rectangle desiredBounds = new Rectangle(overlayLeft, overlayTop, overlayWidth, overlayHeight);
            bool boundsChanged = desiredBounds != lastRenderedBounds;
            if (boundsChanged)
            {
                SetBounds(desiredBounds.X, desiredBounds.Y, desiredBounds.Width, desiredBounds.Height, BoundsSpecified.All);
                lastRenderedBounds = desiredBounds;
            }

            bool becameVisible = !Visible;
            if (!Visible)
            {
                Show();
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }
            if (showRadarBanner)
            {
                Rectangle bannerBounds = new Rectangle(
                    radarBannerLeft,
                    radarBannerTop,
                    radarBannerWidth,
                    radarBannerHeight);
                resetRadarBanner.UpdateBanner(resetRadar, settings, bannerBounds, dpiScale);
            }
            else
            {
                resetRadarBanner.HideBanner();
            }

            UsageData usage = service.Snapshot();
            int textWidth = Math.Max(40, ResetRadarBounds.Left - 14);
            displayText = BuildDisplayText(usage, textWidth);
            bool scheduledRadar = resetRadar.Status == ResetRadarStatus.ScheduledToday ||
                resetRadar.Status == ResetRadarStatus.ScheduledUpcoming;
            string radarClockRevision = scheduledRadar && resetRadar.EffectiveAt.HasValue
                ? DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                : String.Empty;
            bool radarClockChanged = !String.Equals(
                radarClockRevision,
                lastRadarClockRevision,
                StringComparison.Ordinal);
            if (becameVisible || boundsChanged || dpiChanged || radarChanged ||
                radarClockChanged || !String.Equals(displayText, lastRenderedText, StringComparison.Ordinal))
            {
                RenderLayered();
                lastRenderedText = displayText;
                lastRadarClockRevision = radarClockRevision;
            }
        }

        private static string BuildDisplayText(UsageData usage, int availableTextWidth)
        {
            string planLabel = usage.Plan.ToUpperInvariant();
            bool hasQuotaData = usage.RateLimitStatus != "待刷新";
            string shortQuota = BuildShortQuotaText(usage, hasQuotaData);
            string weeklyQuota = BuildWeeklyQuotaText(usage, hasQuotaData);
            string tokensText = !String.IsNullOrWhiteSpace(usage.ProfileTokensText)
                ? usage.ProfileTokensText
                : "待刷新";

            System.Collections.Generic.List<string> sections = new System.Collections.Generic.List<string>();
            sections.Add(planLabel);
            if (availableTextWidth >= 500)
            {
                sections.Add(shortQuota);
                sections.Add(weeklyQuota);
                if (IsAbnormalRateLimitStatus(usage.RateLimitStatus))
                    sections.Add(usage.RateLimitStatus);
                if (usage.AvailableResetCredits.HasValue)
                    sections.Add("重置券" + usage.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture));
                sections.Add("Token " + tokensText);
                return String.Join(" | ", sections.ToArray());
            }

            if (availableTextWidth >= 390)
            {
                sections.Clear();
                sections.Add(planLabel);
                sections.Add(shortQuota);
                sections.Add(weeklyQuota);
                if (usage.AvailableResetCredits.HasValue)
                    sections.Add("重置券" + usage.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture));
                sections.Add("Token " + tokensText);
                return String.Join(" | ", sections.ToArray());
            }

            sections.Add(shortQuota);
            sections.Add(weeklyQuota);
            if (IsAbnormalRateLimitStatus(usage.RateLimitStatus))
                sections.Add(usage.RateLimitStatus);
            if (usage.AvailableResetCredits.HasValue)
                sections.Add("重置券" + usage.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture));
            sections.Add("Token " + tokensText);
            return String.Join(" | ", sections.ToArray());
        }

        private static string BuildWeeklyQuotaText(UsageData usage, bool hasQuotaData)
        {
            string result = "周剩余 " + FormatRemaining(usage.WeeklyRemaining, hasQuotaData);
            if (HasResetText(usage.WeeklyResetText))
                result += "·" + FormatResetText(usage.WeeklyResetText);
            return result;
        }

        private static string BuildShortQuotaText(UsageData usage, bool hasQuotaData)
        {
            string label = "5小时";
            if (usage.ShortWindowMinutes.HasValue && usage.ShortWindowMinutes.Value > 0)
            {
                long minutes = usage.ShortWindowMinutes.Value;
                label = minutes % 60 == 0
                    ? (minutes / 60).ToString(CultureInfo.InvariantCulture) + "小时"
                    : minutes.ToString(CultureInfo.InvariantCulture) + "分钟";
            }

            string result = label + " " + FormatRemaining(usage.ShortRemaining, hasQuotaData);
            if (HasResetText(usage.ShortResetText))
                result += "·" + FormatResetText(usage.ShortResetText);
            return result;
        }

        private static bool HasResetText(string resetText)
        {
            return !String.IsNullOrWhiteSpace(resetText) &&
                resetText != "—" && resetText != "待刷新";
        }

        private static string FormatRemaining(int? remaining, bool hasQuotaData)
        {
            return remaining.HasValue
                ? remaining.Value.ToString(CultureInfo.InvariantCulture) + "%"
                : (hasQuotaData ? "—" : "待刷新");
        }

        private static bool IsAbnormalRateLimitStatus(string status)
        {
            return !String.IsNullOrWhiteSpace(status) && status != "正常" && status != "待刷新";
        }

        private int ScalePixels(int logicalPixels)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * dpiScale));
        }

        private int UnscalePixels(int physicalPixels)
        {
            return (int)Math.Round(physicalPixels / Math.Max(0.5f, dpiScale));
        }

        private int CanvasWidth { get { return UnscalePixels(Width); } }
        private int CanvasHeight { get { return UnscalePixels(Height); } }

        private static string FormatResetText(string resetText)
        {
            if (String.IsNullOrWhiteSpace(resetText) || resetText == "—" || resetText == "待刷新")
                return resetText;
            return resetText.Replace(" ", String.Empty) + "重置";
        }

        private void RenderLayered()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
                return;

            using (Bitmap bitmap = BuildRenderedBitmap())
                NativeMethods.UpdateLayeredBitmap(Handle, bitmap, Left, Top);
        }

        private Bitmap BuildRenderedBitmap()
        {
            Bitmap bitmap = UiRendering.CreateLayeredBitmap(Width, Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);
                graphics.ScaleTransform(dpiScale, dpiScale);

                int canvasWidth = CanvasWidth;
                int canvasHeight = CanvasHeight;
                Rectangle pill = new Rectangle(1, 1, canvasWidth - 3, canvasHeight - 3);
                Color shadowColor = Color.FromArgb(34, 0, 139, 255);
                Color borderColor = Color.FromArgb(105, 48, 180, 255);
                Color textColor = Color.FromArgb(255, 132, 219, 255);
                Color glowColor = Color.FromArgb(38, 0, 154, 255);
                Brush background;
                OverlaySettings visualSettings = settingsExpanded && draftSettings != null ? draftSettings : settings;
                bool rainbowText = visualSettings.Theme == "RainbowText";

                if (visualSettings.Theme == "FrostedGlass")
                {
                    shadowColor = Color.FromArgb(24, 80, 105, 130);
                    borderColor = Color.FromArgb(150, 255, 255, 255);
                    textColor = Color.FromArgb(255, 28, 55, 78);
                    glowColor = Color.FromArgb(18, 255, 255, 255);
                    background = new LinearGradientBrush(pill,
                        Color.FromArgb(205, 242, 248, 252), Color.FromArgb(155, 170, 196, 216),
                        LinearGradientMode.Vertical);
                }
                else if (visualSettings.Theme == "OrangeGradient")
                {
                    shadowColor = Color.FromArgb(42, 255, 96, 20);
                    borderColor = Color.FromArgb(155, 255, 213, 135);
                    textColor = Color.FromArgb(255, 255, 250, 235);
                    glowColor = Color.FromArgb(34, 255, 177, 70);
                    background = new LinearGradientBrush(pill,
                        Color.FromArgb(222, 255, 194, 112), Color.FromArgb(222, 255, 119, 132),
                        LinearGradientMode.Horizontal);
                }
                else if (visualSettings.Theme == "PinkGradient")
                {
                    shadowColor = Color.FromArgb(42, 255, 73, 169);
                    borderColor = Color.FromArgb(170, 255, 190, 230);
                    textColor = Color.FromArgb(255, 255, 248, 253);
                    glowColor = Color.FromArgb(42, 255, 91, 181);
                    background = new LinearGradientBrush(pill,
                        Color.FromArgb(238, 255, 119, 187), Color.FromArgb(238, 190, 86, 210),
                        LinearGradientMode.Horizontal);
                }
                else if (visualSettings.Theme == "Custom")
                {
                    Color custom = Color.FromArgb(visualSettings.CustomBackgroundArgb);
                    shadowColor = Color.FromArgb(32, custom.R, custom.G, custom.B);
                    borderColor = Color.FromArgb(135, 255, 255, 255);
                    textColor = Color.White;
                    glowColor = Color.FromArgb(24, 255, 255, 255);
                    background = new SolidBrush(Color.FromArgb(205, custom.R, custom.G, custom.B));
                }
                else if (rainbowText)
                {
                    shadowColor = Color.Transparent;
                    borderColor = Color.Transparent;
                    textColor = Color.FromArgb(255, 25, 105, 145);
                    glowColor = Color.FromArgb(82, 255, 255, 255);
                    background = null;
                }
                else
                {
                    background = new LinearGradientBrush(pill,
                        Color.FromArgb(218, 8, 31, 51), Color.FromArgb(206, 10, 61, 87),
                        LinearGradientMode.Horizontal);
                }

                if (rainbowText)
                {
                    if (settingsExpanded)
                    {
                        Rectangle settingsPanel = new Rectangle(1, HeaderHeight + 1,
                            canvasWidth - 3, Math.Max(1, canvasHeight - HeaderHeight - 3));
                        using (GraphicsPath panelPath = RoundedRectangle(settingsPanel, 10))
                        using (LinearGradientBrush panelBackground = new LinearGradientBrush(settingsPanel,
                            Color.FromArgb(210, 245, 251, 255), Color.FromArgb(178, 186, 220, 238),
                            LinearGradientMode.Vertical))
                        using (Pen panelBorder = new Pen(Color.FromArgb(145, 70, 181, 225), 1f))
                        {
                            graphics.FillPath(panelBackground, panelPath);
                            graphics.DrawPath(panelBorder, panelPath);
                        }
                    }
                }
                else
                {
                    using (GraphicsPath shadowPath = RoundedRectangle(new Rectangle(0, 0, canvasWidth - 1, canvasHeight - 1), 12))
                    using (Brush shadow = new SolidBrush(shadowColor))
                        graphics.FillPath(shadow, shadowPath);

                    using (GraphicsPath pillPath = RoundedRectangle(pill, 10))
                    using (background)
                    using (Pen border = new Pen(borderColor, 1f))
                    {
                        graphics.FillPath(background, pillPath);
                        graphics.DrawPath(border, pillPath);
                    }

                    using (GraphicsPath glassPath = RoundedRectangle(new Rectangle(3, 3, canvasWidth - 7, canvasHeight - 7), 8))
                    using (LinearGradientBrush glassSheen = new LinearGradientBrush(
                        new Rectangle(3, 3, Math.Max(1, canvasWidth - 7), Math.Max(1, canvasHeight - 7)),
                        Color.FromArgb(62, 255, 255, 255), Color.FromArgb(4, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    using (Pen innerHighlight = new Pen(Color.FromArgb(92, 255, 255, 255), 1f))
                    {
                        graphics.FillPath(glassSheen, glassPath);
                        graphics.DrawPath(innerHighlight, glassPath);
                    }
                }

                using (Font font = CreateDisplayFont(visualSettings))
                using (StringFormat format = UiRendering.CreateTextFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    Rectangle gear = GearBounds;
                    Rectangle radar = ResetRadarBounds;
                    RectangleF box = MainUsageBounds;

                    int glowRadius = settingsExpanded ? 1 : 2;
                    for (int x = -glowRadius; x <= glowRadius; x++)
                    {
                        for (int y = -glowRadius; y <= glowRadius; y++)
                        {
                            if (x == 0 && y == 0)
                                continue;
                            int distance = Math.Abs(x) + Math.Abs(y);
                            int alpha = distance <= 2 ? glowColor.A : Math.Max(6, glowColor.A / 3);
                            using (Brush glow = new SolidBrush(Color.FromArgb(alpha, glowColor.R, glowColor.G, glowColor.B)))
                                graphics.DrawString(displayText, font, glow, new RectangleF(box.X + x, box.Y + y, box.Width, box.Height), format);
                        }
                    }

                    using (Brush text = CreateDisplayTextBrush(box, textColor, rainbowText))
                        graphics.DrawString(displayText, font, text, box, format);

                    DrawResetRadar(graphics, resetRadar, visualSettings);

                    if (gearHovered || gearPressed)
                    {
                        Color gearFillColor = gearPressed
                            ? Color.FromArgb(112, textColor.R, textColor.G, textColor.B)
                            : Color.FromArgb(58, textColor.R, textColor.G, textColor.B);
                        using (GraphicsPath gearHighlightPath = RoundedRectangle(GearBounds, 7))
                        using (Brush gearHighlight = new SolidBrush(gearFillColor))
                            graphics.FillPath(gearHighlight, gearHighlightPath);
                    }

                    using (Pen divider = new Pen(Color.FromArgb(70, textColor.R, textColor.G, textColor.B), 1f))
                        graphics.DrawLine(divider, gear.Left, 6, gear.Left, HeaderHeight - 6);
                    using (Font gearFont = new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point))
                    using (Brush gearBrush = new SolidBrush(textColor))
                    using (StringFormat gearFormat = new StringFormat())
                    {
                        gearFormat.Alignment = StringAlignment.Center;
                        gearFormat.LineAlignment = StringAlignment.Center;
                        graphics.DrawString("\uE713", gearFont, gearBrush, gear, gearFormat);
                    }
                }

                if (settingsExpanded && draftSettings != null)
                    DrawInlineSettings(graphics, textColor, borderColor, visualSettings);
            }
            return bitmap;
        }

        public void ExportThemePreviews(string outputDirectory)
        {
            string[] themes = new[] { "NeonBlue", "FrostedGlass", "OrangeGradient", "PinkGradient", "RainbowText" };
            string[] names = new[] { "neon-blue", "frosted-glass", "orange-gradient", "pink-gradient", "rainbow-text" };
            OverlaySettings originalSettings = settings;
            OverlaySettings originalDraft = draftSettings;
            string originalText = displayText;
            bool originalExpanded = settingsExpanded;
            ResetRadarData originalResetRadar = resetRadar;
            DateTimeOffset? originalResetRadarDisplayNow = resetRadarDisplayNow;
            float originalDpiScale = dpiScale;
            Size originalSize = Size;

            Directory.CreateDirectory(outputDirectory);
            try
            {
                displayText = "PRO | 5小时 86%·14:01重置 | 周剩余 58%·8月16日11:24重置 | 重置券2 | Token 3.5亿";
                resetRadar = new ResetRadarData
                {
                    Status = ResetRadarStatus.ScheduledToday,
                    StatusLabel = "今日有预告",
                    Detail = "Tibo 已预告重置 · 预计 8月10日 15:00—8月11日 14:59（本地时间）",
                    ScopeLabel = "全部计划 · 周额度",
                    SourceUrl = "https://x.com/thsottiaux/status/2086189414292865249",
                    EvidencePostId = "2086189414292865249",
                    EffectiveAt = new DateTimeOffset(2026, 8, 10, 15, 0, 0, TimeSpan.FromHours(8)),
                    EffectiveUntil = new DateTimeOffset(2026, 8, 11, 14, 59, 0, TimeSpan.FromHours(8)),
                    Confidence = 0.92d,
                    NetworkAvailable = true
                };
                resetRadarDisplayNow = new DateTimeOffset(2026, 8, 10, 10, 2, 27, TimeSpan.FromHours(8));
                dpiScale = 1f;
                Width = 720;

                for (int index = 0; index < themes.Length; index++)
                {
                    settings = originalSettings.Clone();
                    settings.Theme = themes[index];

                    settingsExpanded = false;
                    draftSettings = null;
                    Height = HeaderHeight;
                    using (Bitmap collapsed = BuildRenderedBitmap())
                        collapsed.Save(Path.Combine(outputDirectory, names[index] + "-collapsed.png"), ImageFormat.Png);

                    settingsExpanded = true;
                    draftSettings = settings.Clone();
                    Height = ExpandedHeight;
                    using (Bitmap expanded = BuildRenderedBitmap())
                        expanded.Save(Path.Combine(outputDirectory, names[index] + "-expanded.png"), ImageFormat.Png);
                }

                OverlaySettings bannerSettings = originalSettings.Clone();
                bannerSettings.Theme = "RainbowText";
                resetRadarBanner.ExportPreviews(
                    outputDirectory,
                    resetRadar,
                    bannerSettings,
                    resetRadarDisplayNow.Value);
            }
            finally
            {
                settings = originalSettings;
                draftSettings = originalDraft;
                displayText = originalText;
                settingsExpanded = originalExpanded;
                resetRadar = originalResetRadar;
                resetRadarDisplayNow = originalResetRadarDisplayNow;
                dpiScale = originalDpiScale;
                Size = originalSize;
            }
        }

        private void DrawInlineSettings(Graphics graphics, Color textColor, Color borderColor, OverlaySettings visualSettings)
        {
            Color boxColor = Color.FromArgb(30, textColor.R, textColor.G, textColor.B);
            using (Pen separator = new Pen(Color.FromArgb(75, borderColor.R, borderColor.G, borderColor.B), 1f))
                graphics.DrawLine(separator, 12, HeaderHeight + 2, CanvasWidth - 12, HeaderHeight + 2);

            using (Font labelFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font valueFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush textBrush = CreateDisplayTextBrush(
                new RectangleF(0, HeaderHeight, CanvasWidth, Math.Max(1, CanvasHeight - HeaderHeight)),
                textColor, visualSettings.Theme == "RainbowText"))
            using (StringFormat left = UiRendering.CreateTextFormat())
            using (StringFormat center = UiRendering.CreateTextFormat())
            {
                left.Alignment = StringAlignment.Near;
                left.LineAlignment = StringAlignment.Center;
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;

                DrawInlineLabel(graphics, "字体", InlineRowBounds(0), labelFont, textBrush, left);
                Rectangle fontBox = InlineValueBounds(0);
                DrawInlineBox(graphics, fontBox, boxColor, borderColor);
                graphics.DrawString("‹", valueFont, textBrush,
                    new Rectangle(FontPreviousBounds.Left, FontPreviousBounds.Top - 1,
                        FontPreviousBounds.Width, FontPreviousBounds.Height), center);
                graphics.DrawString(visualSettings.FontName, valueFont, textBrush,
                    new Rectangle(fontBox.Left + 34, fontBox.Top, fontBox.Width - 68, fontBox.Height), center);
                graphics.DrawString("›", valueFont, textBrush,
                    new Rectangle(FontNextBounds.Left, FontNextBounds.Top - 1,
                        FontNextBounds.Width, FontNextBounds.Height), center);

                DrawInlineLabel(graphics, "外观", InlineRowBounds(1), labelFont, textBrush, left);
                string[] themeLabels = new[] { "荧光蓝", "磨砂", "渐变橙", "渐变粉", "自定义", "彩字" };
                for (int index = 0; index < themeLabels.Length; index++)
                {
                    Rectangle theme = ThemeChoiceBounds(index);
                    bool selected = InlineThemeIndex(visualSettings.Theme) == index;
                    Color fill = selected ? Color.FromArgb(85, textColor.R, textColor.G, textColor.B) : boxColor;
                    DrawInlineBox(graphics, theme, fill, borderColor);
                    graphics.DrawString(themeLabels[index], labelFont, textBrush, theme, center);
                }

                graphics.DrawString("背景颜色", labelFont, textBrush, BackgroundLabelBounds, left);
                Rectangle colorBox = BackgroundColorBounds;
                DrawInlineBox(graphics, colorBox, boxColor, borderColor);
                Color custom = Color.FromArgb(visualSettings.CustomBackgroundArgb);
                using (Brush swatch = new SolidBrush(Color.FromArgb(255, custom.R, custom.G, custom.B)))
                    graphics.FillRectangle(swatch, new Rectangle(colorBox.Left + 8, colorBox.Top + 6, 42, colorBox.Height - 12));
                graphics.DrawString("选择颜色", labelFont, textBrush,
                    new Rectangle(colorBox.Left + 60, colorBox.Top, colorBox.Width - 68, colorBox.Height), left);

                graphics.DrawString("自动刷新", labelFont, textBrush, RefreshLabelBounds, left);
                Rectangle refreshBox = RefreshValueBounds;
                DrawInlineBox(graphics, refreshBox, boxColor, borderColor);
                graphics.DrawString("−", valueFont, textBrush,
                    new Rectangle(RefreshMinusBounds.Left, RefreshMinusBounds.Top - 1,
                        RefreshMinusBounds.Width, RefreshMinusBounds.Height), center);
                graphics.DrawString(visualSettings.RefreshSeconds.ToString(CultureInfo.InvariantCulture) + " 秒", valueFont, textBrush,
                    new Rectangle(refreshBox.Left + 42, refreshBox.Top, refreshBox.Width - 84, refreshBox.Height), center);
                graphics.DrawString("+", valueFont, textBrush,
                    new Rectangle(RefreshPlusBounds.Left, RefreshPlusBounds.Top - 1,
                        RefreshPlusBounds.Width, RefreshPlusBounds.Height), center);

                DrawResetRadarPanel(graphics, textColor, borderColor, visualSettings);

                DrawInlineBox(graphics, ExitBounds, Color.FromArgb(158, 225, 92, 104), Color.FromArgb(220, 255, 170, 178));
                using (Brush exitText = new SolidBrush(Color.White))
                using (StringFormat exitCenter = (StringFormat)center.Clone())
                {
                    exitCenter.FormatFlags |= StringFormatFlags.NoWrap;
                    graphics.DrawString("退出工具", valueFont, exitText, ExitBounds, exitCenter);
                }

                DrawInlineBox(graphics, CancelBounds, boxColor, borderColor);
                DrawInlineBox(graphics, SaveBounds, Color.FromArgb(85, textColor.R, textColor.G, textColor.B), borderColor);
                graphics.DrawString("取消", labelFont, textBrush, CancelBounds, center);
                graphics.DrawString("保存", valueFont, textBrush, SaveBounds, center);
            }
        }

        private static void DrawInlineLabel(Graphics graphics, string text, Rectangle row, Font font, Brush brush, StringFormat format, int labelLeft = 16)
        {
            int labelWidth = labelLeft >= 100 ? 76 : 94;
            graphics.DrawString(text, font, brush, new Rectangle(labelLeft, row.Top, labelWidth, row.Height), format);
        }

        private static void DrawInlineBox(Graphics graphics, Rectangle bounds, Color fillColor, Color borderColor)
        {
            using (GraphicsPath path = RoundedRectangle(bounds, 6))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(Color.FromArgb(105, borderColor.R, borderColor.G, borderColor.B), 1f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
        }

        private void DrawResetRadarPanel(Graphics graphics, Color textColor, Color borderColor, OverlaySettings visualSettings)
        {
            Color fill;
            Color semanticBorder;
            Color dot;
            GetResetRadarColors(resetRadar.Status, out fill, out semanticBorder, out dot);
            Rectangle panel = ResetRadarPanelBounds;
            DrawInlineBox(graphics, panel, Color.FromArgb(38, fill.R, fill.G, fill.B), semanticBorder);

            using (Font titleFont = CreateDisplayFont(visualSettings, 8.5f))
            using (Font detailFont = CreateDisplayFont(visualSettings, 7.8f))
            using (Brush titleBrush = new SolidBrush(textColor))
            using (Brush detailBrush = new SolidBrush(Color.FromArgb(225, textColor.R, textColor.G, textColor.B)))
            using (Brush dotBrush = new SolidBrush(dot))
            using (StringFormat left = UiRendering.CreateTextFormat())
            using (StringFormat detailFormat = UiRendering.CreateTextFormat())
            using (StringFormat center = UiRendering.CreateTextFormat())
            {
                left.Alignment = StringAlignment.Near;
                left.LineAlignment = StringAlignment.Center;
                left.FormatFlags |= StringFormatFlags.NoWrap;
                detailFormat.Alignment = StringAlignment.Near;
                detailFormat.LineAlignment = StringAlignment.Center;
                detailFormat.Trimming = StringTrimming.EllipsisCharacter;
                detailFormat.FormatFlags |= StringFormatFlags.NoWrap;
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;

                graphics.FillEllipse(dotBrush, panel.Left + 10, panel.Top + 10, 7, 7);
                DateTimeOffset displayNow = resetRadarDisplayNow ?? DateTimeOffset.Now;
                string title = "RESET RADAR · " +
                    ResetRadarDisplay.BuildHeadline(resetRadar, displayNow) +
                    ResetRadarDisplay.ConfidenceSuffix(resetRadar) + " · 非官方";
                graphics.DrawString(title, titleFont, titleBrush,
                    new Rectangle(panel.Left + 23, panel.Top + 4, Math.Max(40, ResetSourceBounds.Width - 25), 20), left);
                string detail = ResetRadarDisplay.BuildPrimaryLine(
                    resetRadar,
                    displayNow);
                graphics.DrawString(detail, detailFont, detailBrush,
                    new Rectangle(panel.Left + 10, panel.Top + 23, Math.Max(40, ResetSourceBounds.Width - 12), 19), detailFormat);

                DrawInlineBox(graphics, ResetRadarRefreshBounds,
                    radarRefreshRequested ? Color.FromArgb(70, dot.R, dot.G, dot.B) :
                        Color.FromArgb(34, textColor.R, textColor.G, textColor.B),
                    radarRefreshRequested ? semanticBorder : borderColor);
                graphics.DrawString(radarRefreshRequested ? "检测中..." : "重新检测",
                    titleFont, titleBrush, ResetRadarRefreshBounds, center);

                bool enabled = visualSettings.ResetNotificationsEnabled;
                Color toggleFill = enabled
                    ? Color.FromArgb(105, dot.R, dot.G, dot.B)
                    : Color.FromArgb(34, textColor.R, textColor.G, textColor.B);
                DrawInlineBox(graphics, ResetNotificationBounds, toggleFill, enabled ? semanticBorder : borderColor);
                graphics.DrawString(enabled ? "提醒  开" : "提醒  关", titleFont, titleBrush, ResetNotificationBounds, center);
            }
        }

        private Rectangle InlineRowBounds(int row)
        {
            return new Rectangle(14, 36 + row * 34, CanvasWidth - 28, 27);
        }

        private Rectangle InlineValueBounds(int row)
        {
            Rectangle rowBounds = InlineRowBounds(row);
            return new Rectangle(116, rowBounds.Top, Math.Max(100, CanvasWidth - 132), rowBounds.Height);
        }

        private Rectangle FontPreviousBounds { get { Rectangle box = InlineValueBounds(0); return new Rectangle(box.Left, box.Top, 34, box.Height); } }
        private Rectangle FontNextBounds { get { Rectangle box = InlineValueBounds(0); return new Rectangle(box.Right - 34, box.Top, 34, box.Height); } }
        private Rectangle BackgroundLabelBounds { get { return new Rectangle(16, 104, 74, 27); } }
        private Rectangle BackgroundColorBounds { get { return new Rectangle(92, 104, 174, 27); } }
        private Rectangle RefreshLabelBounds { get { return new Rectangle(280, 104, 72, 27); } }
        private Rectangle RefreshValueBounds { get { return new Rectangle(354, 104, Math.Max(100, CanvasWidth - 370), 27); } }
        private Rectangle RefreshMinusBounds { get { Rectangle box = RefreshValueBounds; return new Rectangle(box.Left, box.Top, 38, box.Height); } }
        private Rectangle RefreshPlusBounds { get { Rectangle box = RefreshValueBounds; return new Rectangle(box.Right - 38, box.Top, 38, box.Height); } }
        private Rectangle ResetRadarPanelBounds { get { return new Rectangle(16, 140, Math.Max(180, CanvasWidth - 32), 46); } }
        private Rectangle ResetRadarRefreshBounds { get { Rectangle panel = ResetRadarPanelBounds; return new Rectangle(panel.Right - 184, panel.Top + 9, 82, 28); } }
        private Rectangle ResetNotificationBounds { get { Rectangle panel = ResetRadarPanelBounds; return new Rectangle(panel.Right - 92, panel.Top + 9, 82, 28); } }
        private Rectangle ResetSourceBounds { get { Rectangle panel = ResetRadarPanelBounds; return new Rectangle(panel.Left, panel.Top, Math.Max(80, panel.Width - 192), panel.Height); } }
        private Rectangle ExitBounds { get { return new Rectangle(Math.Max(228, CanvasWidth - 208), 198, 60, 30); } }
        private Rectangle CancelBounds { get { return new Rectangle(Math.Max(296, CanvasWidth - 140), 198, 60, 30); } }
        private Rectangle SaveBounds { get { return new Rectangle(Math.Max(364, CanvasWidth - 72), 198, 60, 30); } }

        private Rectangle ThemeChoiceBounds(int index)
        {
            Rectangle box = InlineValueBounds(1);
            int width = box.Width / 6;
            int left = box.Left + index * width;
            int right = index == 5 ? box.Right : left + width - 3;
            return new Rectangle(left, box.Top, Math.Max(1, right - left), box.Height);
        }

        private Rectangle GearBounds
        {
            get { return new Rectangle(Math.Max(0, CanvasWidth - 34), 2, 30, HeaderHeight - 4); }
        }

        private Rectangle ResetRadarBounds
        {
            get
            {
                int width = CanvasWidth < 500 ? 22 : 104;
                return new Rectangle(Math.Max(0, GearBounds.Left - width - 6), 5, width, 18);
            }
        }

        private Rectangle MainUsageBounds
        {
            get { return OverlayInteraction.GetMainUsageBounds(ResetRadarBounds.Left, HeaderHeight); }
        }

        private void DrawResetRadar(Graphics graphics, ResetRadarData radar, OverlaySettings visualSettings)
        {
            Rectangle bounds = ResetRadarBounds;
            Color fill;
            Color border;
            Color dot;
            GetResetRadarColors(radar.Status, out fill, out border, out dot);
            if (radarHovered)
                fill = Color.FromArgb(Math.Min(245, fill.A + 35), fill.R, fill.G, fill.B);

            using (GraphicsPath path = RoundedRectangle(bounds, 8))
            using (Brush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border, 1f))
            {
                graphics.FillPath(fillBrush, path);
                graphics.DrawPath(borderPen, path);
            }

            int dotSize = bounds.Width <= 24 ? 8 : 6;
            int dotLeft = bounds.Width <= 24 ? bounds.Left + (bounds.Width - dotSize) / 2 : bounds.Left + 8;
            int dotTop = bounds.Top + (bounds.Height - dotSize) / 2;
            using (Brush dotBrush = new SolidBrush(dot))
            using (Pen pulse = new Pen(Color.FromArgb(130, dot.R, dot.G, dot.B), 1f))
            {
                graphics.DrawEllipse(pulse, dotLeft - 2, dotTop - 2, dotSize + 4, dotSize + 4);
                graphics.FillEllipse(dotBrush, dotLeft, dotTop, dotSize, dotSize);
            }

            if (bounds.Width > 24)
            {
                using (Font font = CreateDisplayFont(visualSettings, 8f))
                using (Brush text = new SolidBrush(Color.White))
                using (StringFormat format = UiRendering.CreateTextFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    string pillLabel = ResetRadarDisplay.BuildPillLabel(
                        radar,
                        resetRadarDisplayNow ?? DateTimeOffset.Now);
                    graphics.DrawString(pillLabel, font, text,
                        new Rectangle(bounds.Left + 17, bounds.Top, bounds.Width - 20, bounds.Height), format);
                }
            }
        }

        private static void GetResetRadarColors(ResetRadarStatus status, out Color fill, out Color border, out Color dot)
        {
            if (status == ResetRadarStatus.CompletedToday)
            {
                fill = Color.FromArgb(205, 18, 126, 87);
                border = Color.FromArgb(235, 92, 224, 163);
                dot = Color.FromArgb(255, 120, 255, 190);
            }
            else if (status == ResetRadarStatus.ScheduledToday || status == ResetRadarStatus.ScheduledUpcoming)
            {
                fill = Color.FromArgb(210, 184, 105, 18);
                border = Color.FromArgb(240, 255, 202, 89);
                dot = Color.FromArgb(255, 255, 224, 118);
            }
            else if (status == ResetRadarStatus.Offline)
            {
                fill = Color.FromArgb(200, 126, 68, 78);
                border = Color.FromArgb(230, 239, 137, 148);
                dot = Color.FromArgb(255, 255, 166, 176);
            }
            else if (status == ResetRadarStatus.NoSignal)
            {
                fill = Color.FromArgb(190, 62, 91, 118);
                border = Color.FromArgb(225, 135, 176, 211);
                dot = Color.FromArgb(255, 163, 207, 239);
            }
            else
            {
                fill = Color.FromArgb(185, 82, 92, 104);
                border = Color.FromArgb(220, 157, 169, 181);
                dot = Color.FromArgb(255, 190, 201, 212);
            }
        }

        private Font CreateDisplayFont(OverlaySettings visualSettings)
        {
            return CreateDisplayFont(visualSettings, 8.5f);
        }

        private Font CreateDisplayFont(OverlaySettings visualSettings, float size)
        {
            return UiRendering.CreateTextFont(visualSettings.FontName, size, FontStyle.Bold);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_LBUTTONDOWN)
            {
                long packed = message.LParam.ToInt64();
                Point client = ToLogicalPoint(new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff))));
                if (GearBounds.Contains(client))
                {
                    gearPressed = true;
                    ToggleInlineSettings();
                    message.Result = IntPtr.Zero;
                    return;
                }
            }
            if (message.Msg == NativeMethods.WM_NCHITTEST)
            {
                long packed = message.LParam.ToInt64();
                int screenX = unchecked((short)(packed & 0xffff));
                int screenY = unchecked((short)((packed >> 16) & 0xffff));
                Point client = ToLogicalPoint(PointToClient(new Point(screenX, screenY)));
                bool interactive = MainUsageBounds.Contains(client) || GearBounds.Contains(client) ||
                    ResetRadarBounds.Contains(client) ||
                    (settingsExpanded && client.Y >= HeaderHeight &&
                        new Rectangle(0, 0, CanvasWidth, CanvasHeight).Contains(client));
                message.Result = (IntPtr)(interactive ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point logicalLocation = ToLogicalPoint(e.Location);
            bool rightDownInUsage = rightDownStartedInMainUsage;
            if (e.Button == MouseButtons.Right)
                rightDownStartedInMainUsage = false;
            if (OverlayInteraction.DecideMouseUp(
                e.Button,
                logicalLocation,
                MainUsageBounds,
                rightDownInUsage) ==
                OverlayMouseAction.ExitApplication)
            {
                Application.Exit();
                return;
            }
            if (e.Button == MouseButtons.Left && ResetRadarBounds.Contains(logicalLocation))
            {
                ShowRadarBanner();
                return;
            }
            if (e.Button == MouseButtons.Left && GearBounds.Contains(logicalLocation))
            {
                gearPressed = false;
                RefreshInlinePanel();
                return;
            }
            if (e.Button != MouseButtons.Left || !settingsExpanded || draftSettings == null)
                return;

            if (ResetRadarRefreshBounds.Contains(logicalLocation)) RequestRadarRefresh();
            else if (ResetNotificationBounds.Contains(logicalLocation)) ToggleResetNotifications();
            else if (ResetSourceBounds.Contains(logicalLocation)) OpenRadarSource();
            else if (FontPreviousBounds.Contains(logicalLocation)) CycleFont(-1);
            else if (FontNextBounds.Contains(logicalLocation)) CycleFont(1);
            else if (BackgroundColorBounds.Contains(logicalLocation)) ChooseInlineColor();
            else if (RefreshMinusBounds.Contains(logicalLocation)) ChangeRefreshSeconds(-5);
            else if (RefreshPlusBounds.Contains(logicalLocation)) ChangeRefreshSeconds(5);
            else if (ExitBounds.Contains(logicalLocation)) Application.Exit();
            else if (CancelBounds.Contains(logicalLocation)) CloseInlineSettings(false);
            else if (SaveBounds.Contains(logicalLocation)) CloseInlineSettings(true);
            else
            {
                for (int index = 0; index < 6; index++)
                {
                    if (ThemeChoiceBounds(index).Contains(logicalLocation))
                    {
                        draftSettings.Theme = InlineThemeName(index);
                        RefreshInlinePanel();
                        break;
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
                rightDownStartedInMainUsage = MainUsageBounds.Contains(ToLogicalPoint(e.Location));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point logicalLocation = ToLogicalPoint(e.Location);
            bool hovered = GearBounds.Contains(logicalLocation);
            bool resetHovered = ResetRadarBounds.Contains(logicalLocation) ||
                (settingsExpanded && (ResetSourceBounds.Contains(logicalLocation) ||
                    ResetRadarRefreshBounds.Contains(logicalLocation) ||
                    ResetNotificationBounds.Contains(logicalLocation)));
            if (hovered != gearHovered || resetHovered != radarHovered)
            {
                gearHovered = hovered;
                radarHovered = resetHovered;
                RefreshInlinePanel();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (gearHovered || gearPressed || radarHovered)
            {
                gearHovered = false;
                gearPressed = false;
                radarHovered = false;
                RefreshInlinePanel();
            }
        }

        private Point ToLogicalPoint(Point physicalPoint)
        {
            return new Point(UnscalePixels(physicalPoint.X), UnscalePixels(physicalPoint.Y));
        }

        private void ToggleInlineSettings()
        {
            if (settingsExpanded)
                CloseInlineSettings(true);
            else
            {
                draftSettings = settings.Clone();
                settingsExpanded = true;
                resetRadarBanner.HideBanner();
                RefreshInlinePanel();
            }
        }

        private void CycleFont(int direction)
        {
            if (fontOptions.Length == 0)
                return;
            int index = Array.IndexOf(fontOptions, draftSettings.FontName);
            if (index < 0) index = 0;
            index = (index + direction + fontOptions.Length) % fontOptions.Length;
            draftSettings.FontName = fontOptions[index];
            RefreshInlinePanel();
        }

        private void ChangeRefreshSeconds(int delta)
        {
            draftSettings.RefreshSeconds = Math.Max(5, Math.Min(3600, draftSettings.RefreshSeconds + delta));
            RefreshInlinePanel();
        }

        private void ToggleResetNotifications()
        {
            draftSettings.ResetNotificationsEnabled = !draftSettings.ResetNotificationsEnabled;
            RefreshInlinePanel();
        }

        private void RequestRadarRefresh()
        {
            if (radarRefreshRequested)
                return;
            radarRefreshRequested = true;
            radarRefreshRequestedUtc = DateTime.UtcNow;
            resetRadarService.RequestRefresh(true);
            RefreshInlinePanel();
        }

        private void ApplyNotificationVisibility()
        {
            if (resetNotifyIcon != null)
                resetNotifyIcon.Visible = settings.ResetNotificationsEnabled;
        }

        private void ShowResetNotification(ResetRadarNotification notification)
        {
            if (notification == null || resetNotifyIcon == null)
                return;
            notificationSourceUrl = notification.SourceUrl ?? String.Empty;
            resetNotifyIcon.Visible = true;
            resetNotifyIcon.ShowBalloonTip(8000, notification.Title, notification.Body, ToolTipIcon.Info);
        }

        private void CheckForReleaseUpdate()
        {
            releaseUpdateService.RequestCheck();
            GitHubReleaseUpdateSnapshot update = releaseUpdateService.Snapshot();
            if (!update.UpdateAvailable || String.IsNullOrWhiteSpace(update.ReleaseUrl))
                return;

            string revision = update.LatestVersion + "|" + update.ReleaseUrl;
            if (String.Equals(revision, lastReleaseUpdateRevision, StringComparison.Ordinal))
                return;

            lastReleaseUpdateRevision = revision;
            releaseUpdateUrl = update.ReleaseUrl;
            releaseUpdateNotifyIcon.Visible = true;
            releaseUpdateNotifyIcon.ShowBalloonTip(
                10000,
                "Codex Usage Overlay Lite 有新版本",
                "发现 v" + update.LatestVersion + "，点击查看 GitHub Release。",
                ToolTipIcon.Info);
        }

        private void OpenReleaseUpdate()
        {
            releaseUpdateNotifyIcon.Visible = false;
            GitHubReleaseUpdateSnapshot update = releaseUpdateService.Snapshot();
            if (update.UpdateAvailable && !String.IsNullOrWhiteSpace(update.ReleaseUrl))
            {
                DialogResult choice = MessageBox.Show(
                    this,
                    "发现 Codex Usage Overlay Lite v" + update.LatestVersion +
                    "。是否下载并自动安装？\r\n\r\n安装包会先通过 SHA-256 校验。",
                    "发现新版本",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (choice == DialogResult.Yes)
                {
                    string error;
                    if (UpdateInstaller.TryStartUpdate(update, out error))
                    {
                        MessageBox.Show(this,
                            "更新安装程序已启动，Overlay 将关闭并完成覆盖更新。",
                            "开始更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Application.Exit();
                        return;
                    }
                    MessageBox.Show(this, error, "更新失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            OpenExternalUrl(releaseUpdateUrl);
        }

        private void OpenRadarSource()
        {
            OpenExternalUrl(resetRadar == null ? String.Empty : resetRadar.SourceUrl);
        }

        private void OpenRunwayPage()
        {
            OpenExternalUrl(RunwayPageUrl);
        }

        private void DismissRadarBanner()
        {
            radarBannerDismissed = true;
            if (resetRadar != null && !String.IsNullOrWhiteSpace(resetRadar.EvidencePostId))
            {
                settings.DismissedRadarEventId = resetRadar.EvidencePostId;
                OverlaySettingsStore.Save(settings);
                settingsRevision = OverlaySettingsStore.GetRevision();
            }
            resetRadarBanner.HideBanner();
        }

        private void ShowRadarBanner()
        {
            radarBannerDismissed = false;
            if (!String.IsNullOrWhiteSpace(settings.DismissedRadarEventId))
            {
                settings.DismissedRadarEventId = String.Empty;
                OverlaySettingsStore.Save(settings);
                settingsRevision = OverlaySettingsStore.GetRevision();
            }
        }

        private static void OpenExternalUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                return;
            bool isTiboStatus = String.Equals(uri.Host, "x.com", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(uri.AbsolutePath, @"^/thsottiaux/status/\d{1,30}$", RegexOptions.CultureInvariant);
            bool isRunwayPage = String.Equals(uri.Host, "www.codexrunway.com", StringComparison.OrdinalIgnoreCase) &&
                String.Equals(uri.AbsolutePath, "/zh.html", StringComparison.Ordinal);
            bool isGitHubRelease = GitHubReleaseUpdateService.IsAllowedReleaseUrl(uri.AbsoluteUri);
            if (!isTiboStatus && !isRunwayPage && !isGitHubRelease)
                return;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(uri.AbsoluteUri);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch
            {
            }
        }

        private void ChooseInlineColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = Color.FromArgb(draftSettings.CustomBackgroundArgb);
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    draftSettings.CustomBackgroundArgb = Color.FromArgb(255, dialog.Color.R, dialog.Color.G, dialog.Color.B).ToArgb();
                    draftSettings.Theme = "Custom";
                    RefreshInlinePanel();
                }
            }
        }

        private void CloseInlineSettings(bool save)
        {
            if (save && draftSettings != null)
            {
                settings = draftSettings.Clone();
                OverlaySettingsStore.Save(settings);
                settingsRevision = OverlaySettingsStore.GetRevision();
                service.RequestRefresh(settings.RefreshSeconds, true);
                ApplyNotificationVisibility();
            }
            settingsExpanded = false;
            draftSettings = null;
            RefreshInlinePanel();
        }

        private void ReloadSettingsIfChanged()
        {
            if (settingsExpanded)
                return;
            string latestRevision = OverlaySettingsStore.GetRevision();
            if (String.Equals(latestRevision, settingsRevision, StringComparison.Ordinal))
                return;

            settings = OverlaySettingsStore.Load();
            settingsRevision = latestRevision;
            lastRenderedText = String.Empty;
            lastRenderedBounds = Rectangle.Empty;
            service.RequestRefresh(settings.RefreshSeconds, true);
            ApplyNotificationVisibility();
        }

        private void RefreshInlinePanel()
        {
            lastRenderedText = String.Empty;
            lastRenderedBounds = Rectangle.Empty;
            int desiredHeight = ScalePixels(settingsExpanded ? ExpandedHeight : HeaderHeight);
            if (Height != desiredHeight)
                SetBounds(Left, Top, Width, desiredHeight, BoundsSpecified.Height);
            RenderLayered();
        }

        private static int InlineThemeIndex(string theme)
        {
            if (theme == "FrostedGlass") return 1;
            if (theme == "OrangeGradient") return 2;
            if (theme == "PinkGradient") return 3;
            if (theme == "Custom") return 4;
            if (theme == "RainbowText") return 5;
            return 0;
        }

        private static string InlineThemeName(int index)
        {
            if (index == 1) return "FrostedGlass";
            if (index == 2) return "OrangeGradient";
            if (index == 3) return "PinkGradient";
            if (index == 4) return "Custom";
            if (index == 5) return "RainbowText";
            return "NeonBlue";
        }

        private static Brush CreateDisplayTextBrush(RectangleF bounds, Color fallback, bool rainbowText)
        {
            if (!rainbowText)
                return new SolidBrush(fallback);

            RectangleF gradientBounds = new RectangleF(bounds.X, bounds.Y, Math.Max(1f, bounds.Width), Math.Max(1f, bounds.Height));
            LinearGradientBrush gradient = new LinearGradientBrush(gradientBounds,
                Color.FromArgb(255, 255, 137, 47), Color.FromArgb(255, 70, 196, 255),
                LinearGradientMode.Horizontal);
            ColorBlend blend = new ColorBlend();
            blend.Positions = new[] { 0f, 0.34f, 0.68f, 1f };
            blend.Colors = new[]
            {
                Color.FromArgb(255, 255, 137, 47),
                Color.FromArgb(255, 255, 48, 145),
                Color.FromArgb(255, 158, 75, 255),
                Color.FromArgb(255, 70, 196, 255)
            };
            gradient.InterpolationColors = blend;
            return gradient;
        }

        private static string[] BuildFontOptions(string currentFont)
        {
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();
            string[] preferred = new[]
            {
                UiRendering.NormalizeFontName(currentFont),
                "Microsoft YaHei UI", "Segoe UI", "SimSun", "Arial"
            };
            foreach (string candidate in preferred)
            {
                if (!UiRendering.IsSafeTextFontName(candidate) || options.Contains(candidate))
                    continue;
                foreach (FontFamily family in FontFamily.Families)
                {
                    if (String.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        options.Add(family.Name);
                        break;
                    }
                }
            }
            if (options.Count == 0)
                options.Add("Microsoft YaHei UI");
            return options.ToArray();
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Rendering is handled by UpdateLayeredWindow for real per-pixel alpha.
        }

    }

    internal static class CodexWindow
    {
        public static IntPtr Find()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (IsCandidate(foreground))
                return foreground;

            IntPtr result = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (!IsCandidate(hwnd))
                    return true;

                result = hwnd;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        public static bool IsCandidate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) ||
                !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) ||
                NativeMethods.GetWindowText(hwnd) != "ChatGPT")
                return false;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return String.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class CacheStore
    {
        public static UsageData Load(string path)
        {
            UsageData result = new UsageData();
            if (!File.Exists(path))
                return result;
            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0)
                        continue;
                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    int number;
                    long longNumber;
                    if (key == "Plan" && value.Length > 0) result.Plan = value;
                    else if (key == "ShortRemaining" && Int32.TryParse(value, out number)) result.ShortRemaining = number;
                    else if (key == "ShortWindowMinutes" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out longNumber)) result.ShortWindowMinutes = longNumber;
                    else if (key == "ShortReset" && value.Length > 0) result.ShortResetText = value;
                    else if (key == "WeeklyRemaining" && Int32.TryParse(value, out number)) result.WeeklyRemaining = number;
                    else if (key == "WeeklyReset" && value.Length > 0) result.WeeklyResetText = value;
                    else if (key == "RateLimitStatus" && value.Length > 0) result.RateLimitStatus = value;
                    else if (key == "AvailableResetCredits" && Int32.TryParse(value, out number)) result.AvailableResetCredits = number;
                    else if (key == "GeneralRemaining" && Int32.TryParse(value, out number)) result.WeeklyRemaining = number;
                    else if (key == "Reset" && value.Length > 0) result.WeeklyResetText = value;
                    else if (key == "ProfileTokensText" && value.Length > 0) result.ProfileTokensText = value;
                    else if (key == "LifetimeTokens" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out longNumber)) result.LifetimeTokens = longNumber;
                    else if (key == "UpdatedUtc")
                    {
                        DateTime updated;
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out updated)) result.UpdatedUtc = updated;
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        public static void Save(string path, UsageData data)
        {
            try
            {
                string temporary = path + ".tmp";
                string[] lines = new[]
                {
                    "Plan=" + data.Plan,
                    "ShortRemaining=" + (data.ShortRemaining.HasValue ? data.ShortRemaining.Value.ToString(CultureInfo.InvariantCulture) : String.Empty),
                    "ShortWindowMinutes=" + (data.ShortWindowMinutes.HasValue ? data.ShortWindowMinutes.Value.ToString(CultureInfo.InvariantCulture) : String.Empty),
                    "ShortReset=" + data.ShortResetText,
                    "WeeklyRemaining=" + (data.WeeklyRemaining.HasValue ? data.WeeklyRemaining.Value.ToString(CultureInfo.InvariantCulture) : String.Empty),
                    "WeeklyReset=" + data.WeeklyResetText,
                    "RateLimitStatus=" + data.RateLimitStatus,
                    "AvailableResetCredits=" + (data.AvailableResetCredits.HasValue ? data.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture) : String.Empty),
                    "ProfileTokensText=" + data.ProfileTokensText,
                    "LifetimeTokens=" + (data.LifetimeTokens.HasValue ? data.LifetimeTokens.Value.ToString(CultureInfo.InvariantCulture) : String.Empty),
                    "UpdatedUtc=" + data.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture)
                };
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch
            {
            }
        }
    }

    internal static class NativeMethods
    {
        internal const int WS_EX_TRANSPARENT = 0x20;
        internal const int WS_EX_TOOLWINDOW = 0x80;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int SW_SHOWNOACTIVATE = 4;
        internal const int GWLP_HWNDPARENT = -8;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_KEYUP = 0x0101;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int VK_RIGHT = 0x27;
        internal const int VK_ESCAPE = 0x1B;
        internal const int ATTACH_PARENT_PROCESS = -1;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int HTCLIENT = 1;
        internal const int HTTRANSPARENT = -1;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X, Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            public int Width, Height;
            public SIZE(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int valueSize);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr screenDc, ref POINT destination,
            ref SIZE size, IntPtr sourceDc, ref POINT source, int colorKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
        [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
        private static extern uint GetDpiForWindowNative(IntPtr hWnd);
        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);
        [DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int processId);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr newLong);

        internal static string GetWindowText(IntPtr hWnd)
        {
            StringBuilder text = new StringBuilder(512);
            GetWindowText(hWnd, text, text.Capacity);
            return text.ToString();
        }

        internal static float GetWindowDpiScale(IntPtr hWnd)
        {
            uint dpi = 96;
            try
            {
                dpi = GetDpiForWindowNative(hWnd);
            }
            catch
            {
                IntPtr dc = IntPtr.Zero;
                try
                {
                    dc = GetDC(hWnd);
                    if (dc != IntPtr.Zero)
                    {
                        int detectedDpi = GetDeviceCaps(dc, 88);
                        if (detectedDpi > 0)
                            dpi = (uint)detectedDpi;
                    }
                }
                catch
                {
                }
                finally
                {
                    if (dc != IntPtr.Zero)
                        ReleaseDC(hWnd, dc);
                }
            }

            if (dpi < 72 || dpi > 768)
                dpi = 96;
            return dpi / 96f;
        }

        internal static bool TryGetVisibleWindowRect(IntPtr hWnd, out RECT rect)
        {
            try
            {
                return DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect,
                    Marshal.SizeOf(typeof(RECT))) == 0;
            }
            catch
            {
                rect = new RECT();
                return false;
            }
        }

        internal static void SetOwner(IntPtr hWnd, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, GWLP_HWNDPARENT, owner);
            else SetWindowLong32(hWnd, GWLP_HWNDPARENT, owner);
        }

        internal static void UpdateLayeredBitmap(IntPtr hWnd, Bitmap bitmap, int left, int top)
        {
            const int ULW_ALPHA = 2;
            const byte AC_SRC_OVER = 0;
            const byte AC_SRC_ALPHA = 1;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memoryDc, bitmapHandle);
                POINT destination = new POINT(left, top);
                POINT source = new POINT(0, 0);
                SIZE size = new SIZE(bitmap.Width, bitmap.Height);
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AC_SRC_ALPHA;
                UpdateLayeredWindow(hWnd, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

    }
}
