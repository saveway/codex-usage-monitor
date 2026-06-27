using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexUsageMonitorV2
{
    internal sealed class AppSettings
    {
        public int autoRefreshMinutes { get; set; }
        public bool? widgetVisible { get; set; }
        public int? widgetSize { get; set; }
        public int? widgetX { get; set; }
        public int? widgetY { get; set; }
        public string graphStyle { get; set; }
        public string logoMode { get; set; }
        public Dictionary<string, string> colors { get; set; }
        public string acknowledgedFiveHourAlertKey { get; set; }
        public string acknowledgedWeeklyAlertKey { get; set; }
    }

    internal static class AppSettingsStore
    {
        private static readonly int[] AllowedAutoRefreshMinutes = { 0, 10, 15, 30, 60 };

        public static AppSettings Load()
        {
            return Load(AppPaths.SettingsPath);
        }

        internal static AppSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return CreateDefault();
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var settings = serializer.Deserialize<AppSettings>(
                    File.ReadAllText(path, Encoding.UTF8)) ?? CreateDefault();
                settings.autoRefreshMinutes = IsAllowedAutoRefresh(settings.autoRefreshMinutes)
                    ? settings.autoRefreshMinutes
                    : 0;
                NormalizeWidgetSettings(settings);
                settings.colors = ThemePalette.Normalize(settings.colors);
                return settings;
            }
            catch (Exception ex)
            {
                AppLogger.Write("App settings could not be read; using defaults: " + ex.Message);
                return CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            Save(AppPaths.SettingsPath, settings);
        }

        internal static void Save(string path, AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }
            if (!IsAllowedAutoRefresh(settings.autoRefreshMinutes))
            {
                throw new ArgumentOutOfRangeException("settings.autoRefreshMinutes");
            }

            NormalizeWidgetSettings(settings);
            settings.colors = ThemePalette.Normalize(settings.colors);
            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(settings);
            var temporaryPath = path + ".tmp";
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporaryPath, path);
        }

        public static bool IsAllowedAutoRefresh(int minutes)
        {
            return Array.IndexOf(AllowedAutoRefreshMinutes, minutes) >= 0;
        }

        private static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                autoRefreshMinutes = 0,
                widgetVisible = false,
                widgetSize = 128,
                widgetX = null,
                widgetY = null,
                graphStyle = WidgetGraphStyle.Rings.ToString(),
                logoMode = WidgetLogoMode.Static.ToString(),
                colors = ThemePalette.CreateDefaultHexValues()
            };
        }

        private static void NormalizeWidgetSettings(AppSettings settings)
        {
            if (settings.widgetSize != 256)
            {
                settings.widgetSize = 128;
            }
            if (!settings.widgetVisible.HasValue)
            {
                settings.widgetVisible = false;
            }
            settings.graphStyle = WidgetGraphStyleHelper.ToSettingValue(
                WidgetGraphStyleHelper.Normalize(settings.graphStyle));
            settings.logoMode = WidgetLogoModeHelper.ToSettingValue(
                WidgetLogoModeHelper.Normalize(settings.logoMode));
        }
    }
}
