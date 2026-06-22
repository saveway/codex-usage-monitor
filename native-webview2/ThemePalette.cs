using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodexUsageMonitorV2
{
    internal enum ThemeColorGroup
    {
        FiveHour,
        Weekly,
        Interface
    }

    internal sealed class ThemeColorDefinition
    {
        public ThemeColorDefinition(string key, string label, string defaultHex, ThemeColorGroup group)
        {
            Key = key;
            Label = label;
            DefaultHex = defaultHex;
            Group = group;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public string DefaultHex { get; private set; }
        public ThemeColorGroup Group { get; private set; }
    }

    internal sealed class ThemePalette
    {
        private static readonly Regex HexPattern = new Regex(
            "^#[0-9A-Fa-f]{6}$",
            RegexOptions.CultureInvariant);

        private static readonly ThemeColorDefinition[] ColorDefinitions =
        {
            new ThemeColorDefinition("FiveNormal", "Normal (76-100%)", "#20BF60", ThemeColorGroup.FiveHour),
            new ThemeColorDefinition("FiveGood", "Good (51-75%)", "#84CC16", ThemeColorGroup.FiveHour),
            new ThemeColorDefinition("FiveCaution", "Caution (26-50%)", "#EAB308", ThemeColorGroup.FiveHour),
            new ThemeColorDefinition("FiveLow", "Low (16-25%)", "#EA580C", ThemeColorGroup.FiveHour),
            new ThemeColorDefinition("FiveDanger", "Danger (6-15%)", "#DC2626", ThemeColorGroup.FiveHour),
            new ThemeColorDefinition("FiveCritical", "Critical (0-5%)", "#B91C1C", ThemeColorGroup.FiveHour),
            new ThemeColorDefinition("WeekNormal", "Normal (76-100%)", "#3A88FF", ThemeColorGroup.Weekly),
            new ThemeColorDefinition("WeekGood", "Good (51-75%)", "#6366F1", ThemeColorGroup.Weekly),
            new ThemeColorDefinition("WeekCaution", "Caution (26-50%)", "#A855F7", ThemeColorGroup.Weekly),
            new ThemeColorDefinition("WeekLow", "Low (16-25%)", "#DB2777", ThemeColorGroup.Weekly),
            new ThemeColorDefinition("WeekDanger", "Danger (6-15%)", "#BE185D", ThemeColorGroup.Weekly),
            new ThemeColorDefinition("WeekCritical", "Critical (0-5%)", "#701A75", ThemeColorGroup.Weekly),
            new ThemeColorDefinition("Track", "Graph track", "#CDE8DA", ThemeColorGroup.Interface),
            new ThemeColorDefinition("Text", "Main text", "#1C533B", ThemeColorGroup.Interface),
            new ThemeColorDefinition("MutedText", "Secondary text", "#417058", ThemeColorGroup.Interface),
            new ThemeColorDefinition("Close", "Close button", "#377D58", ThemeColorGroup.Interface),
            new ThemeColorDefinition("CodexMark", "App mark", "#2A8C5B", ThemeColorGroup.Interface),
            new ThemeColorDefinition("CenterFill", "Center fill", "#FFFFFF", ThemeColorGroup.Interface),
            new ThemeColorDefinition("TransparentEdge", "Transparent edge", "#E8FAF0", ThemeColorGroup.Interface),
            new ThemeColorDefinition("BatteryOutline", "Battery outline", "#5B8D70", ThemeColorGroup.Interface),
            new ThemeColorDefinition("AlertFlash", "Zero alert flash", "#FFFFFF", ThemeColorGroup.Interface)
        };

        private readonly Dictionary<string, string> values;

        public ThemePalette(Dictionary<string, string> source)
        {
            values = Normalize(source);
        }

        public static IEnumerable<ThemeColorDefinition> Definitions
        {
            get { return ColorDefinitions; }
        }

        public Color GetColor(string key)
        {
            string hex;
            if (!values.TryGetValue(key, out hex))
            {
                throw new ArgumentException("Unknown theme color: " + key, "key");
            }
            return ColorTranslator.FromHtml(hex);
        }

        public string GetHex(string key)
        {
            string hex;
            if (!values.TryGetValue(key, out hex))
            {
                throw new ArgumentException("Unknown theme color: " + key, "key");
            }
            return hex;
        }

        public void SetColor(string key, Color color)
        {
            if (!values.ContainsKey(key))
            {
                throw new ArgumentException("Unknown theme color: " + key, "key");
            }
            values[key] = ToHex(color);
        }

        public void Reset()
        {
            values.Clear();
            foreach (var definition in ColorDefinitions)
            {
                values.Add(definition.Key, definition.DefaultHex);
            }
        }

        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }

        public Color GetFiveHourColor(int remaining)
        {
            return GetColor(GetLevelKey("Five", remaining));
        }

        public Color GetWeeklyColor(int remaining)
        {
            return GetColor(GetLevelKey("Week", remaining));
        }

        internal static Dictionary<string, string> CreateDefaultHexValues()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in ColorDefinitions)
            {
                result.Add(definition.Key, definition.DefaultHex);
            }
            return result;
        }

        internal static Dictionary<string, string> Normalize(Dictionary<string, string> source)
        {
            var result = CreateDefaultHexValues();
            if (source == null)
            {
                return result;
            }

            foreach (var definition in ColorDefinitions)
            {
                string candidate;
                if (source.TryGetValue(definition.Key, out candidate) && IsValidHex(candidate))
                {
                    result[definition.Key] = candidate.ToUpperInvariant();
                }
            }
            return result;
        }

        internal static string GetLevelKey(string prefix, int remaining)
        {
            var value = Math.Max(0, Math.Min(100, remaining));
            if (value <= 5)
            {
                return prefix + "Critical";
            }
            if (value <= 15)
            {
                return prefix + "Danger";
            }
            if (value <= 25)
            {
                return prefix + "Low";
            }
            if (value <= 50)
            {
                return prefix + "Caution";
            }
            if (value <= 75)
            {
                return prefix + "Good";
            }
            return prefix + "Normal";
        }

        private static bool IsValidHex(string value)
        {
            return !string.IsNullOrEmpty(value) && HexPattern.IsMatch(value);
        }

        private static string ToHex(Color color)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}",
                color.R,
                color.G,
                color.B);
        }
    }
}
