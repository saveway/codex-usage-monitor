using System;
using System.IO;

namespace CodexUsageMonitorV2
{
    internal static class AppPaths
    {
        public static readonly string RuntimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageMonitorV2");

        public static readonly string WebViewProfileDirectory = Path.Combine(
            RuntimeDirectory,
            "webview2-profile");

        public static readonly string DataPath = Path.Combine(RuntimeDirectory, "codex-usage.json");
        public static readonly string LogPath = Path.Combine(RuntimeDirectory, "codex-usage-monitor-v2.log");
        public static readonly string DebugStatusPath = Path.Combine(RuntimeDirectory, "codex-usage-debug-status.txt");
        public static readonly string SettingsPath = Path.Combine(RuntimeDirectory, "codex-usage-settings.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(RuntimeDirectory);
            Directory.CreateDirectory(WebViewProfileDirectory);
        }
    }
}
