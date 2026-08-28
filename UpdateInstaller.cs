using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace CodexUsageOverlay
{
    internal static class UpdateInstaller
    {
        private const int RequestTimeoutMilliseconds = 30000;
        private const long MaximumInstallerBytes = 100L * 1024L * 1024L;
        private const string InstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
        private static readonly Regex Sha256Pattern = new Regex(
            "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

        public static bool TryStartUpdate(GitHubReleaseUpdateSnapshot update, out string error)
        {
            error = String.Empty;
            string installerUrl = GitHubReleaseUpdateService.GetInstallerDownloadUrl(update);
            if (String.IsNullOrWhiteSpace(installerUrl))
            {
                error = "找不到有效的更新安装包地址。";
                return false;
            }

            string releaseTag = ExtractReleaseTag(update.ReleaseUrl);
            string installerName = "CodexUsageOverlay-Lite-Setup-" + update.LatestVersion + ".exe";
            string tempDirectory = Path.Combine(
                Path.GetTempPath(), "CodexUsageOverlay-update-" + Guid.NewGuid().ToString("N"));
            string installerPath = Path.Combine(tempDirectory, installerName);
            string sumsPath = Path.Combine(tempDirectory, "SHA256SUMS.txt");

            try
            {
                Directory.CreateDirectory(tempDirectory);
                Download(installerUrl, installerPath);
                Download(
                    "https://github.com/floretly/CodexUsageOverlay-Lite/releases/download/" +
                    Uri.EscapeDataString(releaseTag) + "/SHA256SUMS.txt", sumsPath);
                VerifySha256(installerPath, installerName, sumsPath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = InstallerArguments,
                    WorkingDirectory = tempDirectory,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteDirectory(tempDirectory);
                error = "更新下载或校验失败：" + exception.Message;
                return false;
            }
        }

        private static string ExtractReleaseTag(string releaseUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out uri))
                throw new InvalidDataException("Release 地址无效。");
            const string prefix = "/floretly/CodexUsageOverlay-Lite/releases/tag/";
            string path = uri.AbsolutePath;
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Release 地址不属于指定仓库。");
            return Uri.UnescapeDataString(path.Substring(prefix.Length));
        }

        private static void Download(string url, string destinationPath)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/octet-stream, text/plain";
            request.UserAgent = ProductInfo.UserAgent;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.UseDefaultCredentials = false;
            request.Credentials = null;
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new WebException("更新服务器返回 HTTP " + ((int)response.StatusCode).ToString());
                if (response.ContentLength > MaximumInstallerBytes)
                    throw new InvalidDataException("更新文件过大。");

                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            }

            FileInfo file = new FileInfo(destinationPath);
            if (file.Length == 0 || file.Length > MaximumInstallerBytes)
                throw new InvalidDataException("更新文件大小无效。");
        }

        private static void VerifySha256(string installerPath, string installerName, string sumsPath)
        {
            string expected = String.Empty;
            foreach (string line in File.ReadAllLines(sumsPath, Encoding.UTF8))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && String.Equals(parts[parts.Length - 1], installerName, StringComparison.Ordinal))
                {
                    expected = parts[0];
                    break;
                }
            }

            if (!Sha256Pattern.IsMatch(expected))
                throw new InvalidDataException("SHA-256 校验文件中没有找到安装包记录。");

            string actual;
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream input = File.OpenRead(installerPath))
            {
                actual = ToHex(sha256.ComputeHash(input));
            }
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包 SHA-256 校验不一致。");
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
