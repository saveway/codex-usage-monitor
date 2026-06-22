using System;

namespace CodexUsageMonitorV2
{
    internal enum WidgetGraphStyle
    {
        Rings,
        Bars,
        Meters,
        Battery
    }

    internal static class WidgetGraphStyleHelper
    {
        public static WidgetGraphStyle Normalize(string value)
        {
            WidgetGraphStyle style;
            if (Enum.TryParse(value, true, out style) && Enum.IsDefined(typeof(WidgetGraphStyle), style))
            {
                return style;
            }
            return WidgetGraphStyle.Rings;
        }

        public static string ToSettingValue(WidgetGraphStyle style)
        {
            return style.ToString();
        }
    }
}
