using System;

namespace CodexUsageOverlay
{
    internal static class GitHubReleaseUpdateTests
    {
        public static void NewerStableReleaseIsDetected()
        {
            GitHubReleaseUpdateSnapshot result = GitHubReleaseUpdateService.EvaluateReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.1.0");
            Assert(result != null && result.UpdateAvailable, "new release was not detected");
            Assert(result.LatestVersion == "1.1.0", result == null ? "missing result" : result.LatestVersion);
        }

        public static void PrereleaseIsIgnored()
        {
            GitHubReleaseUpdateSnapshot result = GitHubReleaseUpdateService.EvaluateReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.1.0-beta.1");
            Assert(result == null, "prerelease was accepted");
        }

        public static void ForeignReleaseUrlIsRejected()
        {
            GitHubReleaseUpdateSnapshot result = GitHubReleaseUpdateService.EvaluateReleaseUrl(
                "https://example.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.1.0");
            Assert(result == null, "foreign release URL was accepted");
        }

        public static void CurrentReleaseDoesNotPrompt()
        {
            GitHubReleaseUpdateSnapshot result = GitHubReleaseUpdateService.EvaluateReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.0.0");
            Assert(result != null && !result.UpdateAvailable, "current release prompted an update");
        }

        public static void ReleaseUrlAllowlistIsStrict()
        {
            Assert(GitHubReleaseUpdateService.IsAllowedReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.1.0"),
                "valid release URL was rejected");
            Assert(!GitHubReleaseUpdateService.IsAllowedReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/latest"),
                "unversioned release URL was accepted");
            Assert(!GitHubReleaseUpdateService.IsAllowedReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.1.0?download=1"),
                "release URL with query was accepted");
        }

        public static void InstallerDownloadUrlUsesReleaseVersion()
        {
            GitHubReleaseUpdateSnapshot update = GitHubReleaseUpdateService.EvaluateReleaseUrl(
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/tag/v1.1.0");
            string url = GitHubReleaseUpdateService.GetInstallerDownloadUrl(update);
            Assert(url ==
                "https://github.com/floretly/CodexUsageOverlay-Lite/releases/download/v1.1.0/" +
                "CodexUsageOverlay-Lite-Setup-1.1.0.exe", url);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
