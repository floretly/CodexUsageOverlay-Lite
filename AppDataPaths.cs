using System;
using System.IO;

namespace CodexUsageOverlay
{
    internal static class AppDataPaths
    {
        private static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Codex Usage Overlay Lite");
            }
        }

        public static string GetFile(string name)
        {
            Directory.CreateDirectory(DataDirectory);
            return Path.Combine(DataDirectory, name);
        }

        public static void MigrateLegacyFiles()
        {
            string[] persistentFiles = new[]
            {
                "settings.ini",
                "usage-cache.ini",
                "reset-radar-cache.json",
                "reset-radar-state.ini"
            };
            foreach (string name in persistentFiles)
                MoveLegacyFile(name);

            // Snapshot reports can contain account fields. They are diagnostic
            // artifacts, so remove old copies instead of carrying them forward.
            DeleteLegacyFile("snapshot.txt");
            DeleteLegacyFile("reset-radar-snapshot.txt");
        }

        private static void MoveLegacyFile(string name)
        {
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (!File.Exists(legacyPath))
                return;
            try
            {
                string targetPath = GetFile(name);
                if (File.Exists(targetPath))
                {
                    File.Delete(legacyPath);
                    return;
                }
                File.Move(legacyPath, targetPath);
            }
            catch
            {
            }
        }

        private static void DeleteLegacyFile(string name)
        {
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            try
            {
                if (File.Exists(legacyPath))
                    File.Delete(legacyPath);
            }
            catch
            {
            }
        }
    }
}
