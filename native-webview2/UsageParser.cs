using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexUsageMonitorV2
{
    internal static class UsageParser
    {
        private static readonly Regex FiveHourRegex = new Regex(
            @"(?:5\s*시간|5\s*(?:hour|hr)|five\s*hour)[\s\S]{0,240}?(\d{1,3})\s*%",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex WeeklyRegex = new Regex(
            @"(?:주간|weekly|week)[\s\S]{0,240}?(\d{1,3})\s*%",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static UsageSnapshot Parse(string pageText, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(pageText))
            {
                throw new InvalidOperationException("The usage page did not contain readable text.");
            }

            var fiveHour = MatchPercentage(FiveHourRegex, pageText, "5-hour");
            var weekly = MatchPercentage(WeeklyRegex, pageText, "weekly");
            return new UsageSnapshot
            {
                fiveHourRemaining = fiveHour,
                weeklyRemaining = weekly,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                source = sourceUrl,
                status = "ok"
            };
        }

        public static void Save(UsageSnapshot snapshot)
        {
            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(snapshot);
            var pretty = PrettyPrint(json);
            var temporaryPath = AppPaths.DataPath + ".tmp";
            File.WriteAllText(temporaryPath, pretty, new UTF8Encoding(false));
            if (File.Exists(AppPaths.DataPath))
            {
                File.Delete(AppPaths.DataPath);
            }
            File.Move(temporaryPath, AppPaths.DataPath);
        }

        public static bool TryLoad(out UsageSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (!File.Exists(AppPaths.DataPath))
            {
                error = "No saved usage data is available.";
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                snapshot = serializer.Deserialize<UsageSnapshot>(File.ReadAllText(AppPaths.DataPath, Encoding.UTF8));
                if (snapshot == null ||
                    snapshot.fiveHourRemaining < 0 || snapshot.fiveHourRemaining > 100 ||
                    snapshot.weeklyRemaining < 0 || snapshot.weeklyRemaining > 100 ||
                    string.IsNullOrWhiteSpace(snapshot.updatedAt))
                {
                    throw new InvalidDataException("Saved usage data is incomplete or invalid.");
                }
                return true;
            }
            catch (Exception ex)
            {
                snapshot = null;
                error = "Saved usage data could not be read: " + ex.Message;
                AppLogger.Write(error);
                return false;
            }
        }

        private static int MatchPercentage(Regex regex, string text, string label)
        {
            var match = regex.Match(text);
            int value;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out value) || value < 0 || value > 100)
            {
                throw new InvalidOperationException("Could not parse the " + label + " remaining percentage.");
            }
            return value;
        }

        private static string PrettyPrint(string json)
        {
            var indentation = 0;
            var quoted = false;
            var escaped = false;
            var builder = new StringBuilder();
            foreach (var character in json)
            {
                if (quoted)
                {
                    builder.Append(character);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        quoted = false;
                    }
                    continue;
                }

                switch (character)
                {
                    case '"': quoted = true; builder.Append(character); break;
                    case '{':
                    case '[': indentation++; builder.Append(character).AppendLine().Append(new string(' ', indentation * 2)); break;
                    case '}':
                    case ']': indentation--; builder.AppendLine().Append(new string(' ', indentation * 2)).Append(character); break;
                    case ',': builder.Append(character).AppendLine().Append(new string(' ', indentation * 2)); break;
                    case ':': builder.Append(": "); break;
                    default: if (!char.IsWhiteSpace(character)) builder.Append(character); break;
                }
            }
            return builder.ToString();
        }
    }

    internal sealed class UsageSnapshot
    {
        public int fiveHourRemaining { get; set; }
        public int weeklyRemaining { get; set; }
        public string updatedAt { get; set; }
        public string source { get; set; }
        public string status { get; set; }
    }
}
