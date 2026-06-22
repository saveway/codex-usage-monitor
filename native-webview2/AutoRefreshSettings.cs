using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexUsageMonitorV2
{
    internal static class AutoRefreshSettings
    {
        private static readonly int[] AllowedMinutes = { 0, 10, 15, 30, 60 };

        public static int LoadMinutes()
        {
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return 0;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var settings = serializer.Deserialize<AutoRefreshSettingsFile>(
                    File.ReadAllText(AppPaths.SettingsPath, Encoding.UTF8));
                return settings != null && IsAllowed(settings.autoRefreshMinutes)
                    ? settings.autoRefreshMinutes
                    : 0;
            }
            catch (Exception ex)
            {
                AppLogger.Write("Auto refresh settings could not be read; using Off: " + ex.Message);
                return 0;
            }
        }

        public static void SaveMinutes(int minutes)
        {
            if (!IsAllowed(minutes))
            {
                throw new ArgumentOutOfRangeException("minutes");
            }

            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(new AutoRefreshSettingsFile
            {
                autoRefreshMinutes = minutes
            });
            var temporaryPath = AppPaths.SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(AppPaths.SettingsPath))
            {
                File.Delete(AppPaths.SettingsPath);
            }
            File.Move(temporaryPath, AppPaths.SettingsPath);
        }

        private static bool IsAllowed(int minutes)
        {
            return Array.IndexOf(AllowedMinutes, minutes) >= 0;
        }

        private sealed class AutoRefreshSettingsFile
        {
            public int autoRefreshMinutes { get; set; }
        }
    }
}
