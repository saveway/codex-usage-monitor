using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexUsageMonitorV2
{
    internal static class SafeDebugSnapshot
    {
        private static readonly Regex FiveHourLabel = new Regex(
            @"5\s*(?:시간|hour|hr)|five\s*hour",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex WeeklyLabel = new Regex(
            @"주간|weekly|week",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LoginLabel = new Regex(
            @"로그인|log\s*in|sign\s*in|authentication required",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static void Save(string status, Uri source, string pageText)
        {
            var builder = new StringBuilder();
            builder.AppendLine("status=" + SanitizeStatus(status));
            builder.AppendLine("source_host=" + SafeHost(source));
            builder.AppendLine("usage_path=" + IsUsagePath(source));
            builder.AppendLine("five_hour_label=" + Present(FiveHourLabel.IsMatch(pageText ?? string.Empty)));
            builder.AppendLine("weekly_label=" + Present(WeeklyLabel.IsMatch(pageText ?? string.Empty)));
            builder.AppendLine("login_prompt=" + Present(LoginLabel.IsMatch(pageText ?? string.Empty)));
            File.WriteAllText(AppPaths.DebugStatusPath, builder.ToString(), new UTF8Encoding(false));
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(AppPaths.DebugStatusPath))
                {
                    File.Delete(AppPaths.DebugStatusPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Could not delete debug status: " + ex.Message);
            }
        }

        private static string SanitizeStatus(string status)
        {
            return Regex.Replace(status ?? "unknown", @"[^a-z_-]", string.Empty, RegexOptions.IgnoreCase);
        }

        private static string SafeHost(Uri source)
        {
            return source != null && source.Host.EndsWith("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                ? "chatgpt.com"
                : "other";
        }

        private static string IsUsagePath(Uri source)
        {
            return Present(source != null && source.AbsolutePath.IndexOf("/codex/cloud/settings/analytics", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Present(bool value)
        {
            return value ? "present" : "absent";
        }
    }
}
