using System;

namespace CodexUsageMonitorV2
{
    internal enum WidgetLogoMode
    {
        Static,
        Animated
    }

    internal static class WidgetLogoModeHelper
    {
        public static WidgetLogoMode Normalize(string value)
        {
            WidgetLogoMode mode;
            if (Enum.TryParse(value, true, out mode) && Enum.IsDefined(typeof(WidgetLogoMode), mode))
            {
                return mode;
            }
            return WidgetLogoMode.Static;
        }

        public static string ToSettingValue(WidgetLogoMode mode)
        {
            return mode.ToString();
        }
    }
}
