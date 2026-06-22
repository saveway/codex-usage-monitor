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
        private static readonly Regex FiveHourLabelRegex = new Regex(
            @"(?:5\s*시간\s*사용\s*한도|5\s*(?:hour|hr)[\s-]*(?:usage\s*)?limit|five\s*hour\s*(?:usage\s*)?limit)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex WeeklyLabelRegex = new Regex(
            @"(?:주간\s*사용\s*한도|weekly\s*(?:usage\s*)?limit|week\s*(?:usage\s*)?limit)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SparkRegex = new Regex(
            @"(?:GPT\s*[-\s.]*5\.?3\s*[-\s]*Codex\s*[-\s]*Spark|Codex\s*[-\s]*Spark|\bSpark\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PercentRegex = new Regex(
            @"(\d{1,3})\s*%",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex CreditRegex = new Regex(
            @"(?:(?:남은\s*)?크레딧|remaining\s+credits|credits?\s+remaining)\s*[:：]?\s*([0-9][0-9,]*(?:\.[0-9]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex CompactKoreanDateRegex = new Regex(
            @"(\d{4})\.\s*(\d{1,2})\.\s*(\d{1,2})\.",
            RegexOptions.CultureInvariant);

        public static UsageSnapshot Parse(string pageText, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(pageText))
            {
                throw new InvalidOperationException("The usage page did not contain readable text.");
            }

            var values = ExtractUsageValues(pageText);
            if (!values.FiveHourPercent.HasValue)
            {
                throw new InvalidOperationException("Could not parse the general 5-hour remaining percentage.");
            }
            if (!values.WeeklyPercent.HasValue)
            {
                throw new InvalidOperationException("Could not parse the general weekly remaining percentage.");
            }

            var fiveHour = values.FiveHourPercent.Value;
            var weekly = values.WeeklyPercent.Value;
            return new UsageSnapshot
            {
                fiveHourRemaining = fiveHour,
                weeklyRemaining = weekly,
                fiveHourPercent = fiveHour,
                weeklyPercent = weekly,
                sparkFiveHourPercent = values.SparkFiveHourPercent,
                sparkWeeklyPercent = values.SparkWeeklyPercent,
                fiveHourResetText = values.FiveHourResetText,
                weeklyResetText = values.WeeklyResetText,
                creditsRemaining = values.CreditsRemaining,
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
                if (snapshot == null)
                {
                    throw new InvalidDataException("Saved usage data is empty.");
                }
                if (!snapshot.fiveHourPercent.HasValue)
                {
                    snapshot.fiveHourPercent = snapshot.fiveHourRemaining;
                }
                if (!snapshot.weeklyPercent.HasValue)
                {
                    snapshot.weeklyPercent = snapshot.weeklyRemaining;
                }
                if (snapshot.fiveHourRemaining < 0 || snapshot.fiveHourRemaining > 100 ||
                    snapshot.weeklyRemaining < 0 || snapshot.weeklyRemaining > 100 ||
                    !IsValidOptionalPercent(snapshot.sparkFiveHourPercent) ||
                    !IsValidOptionalPercent(snapshot.sparkWeeklyPercent) ||
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

        internal static UsageParseResult ExtractUsageValues(string pageText)
        {
            var result = new UsageParseResult();
            foreach (var label in FindUsageLabels(pageText))
            {
                var segment = GetSegmentAfterLabel(pageText, label);
                var value = FindPercentAfterLabel(segment);
                if (!value.HasValue)
                {
                    continue;
                }

                if (label.IsSpark && label.IsFiveHour && !result.SparkFiveHourPercent.HasValue)
                {
                    result.SparkFiveHourPercent = value.Value;
                }
                else if (label.IsSpark && label.IsWeekly && !result.SparkWeeklyPercent.HasValue)
                {
                    result.SparkWeeklyPercent = value.Value;
                }
                else if (!label.IsSpark && label.IsFiveHour && !result.FiveHourPercent.HasValue)
                {
                    result.FiveHourPercent = value.Value;
                    result.FiveHourResetText = FindResetText(segment);
                }
                else if (!label.IsSpark && label.IsWeekly && !result.WeeklyPercent.HasValue)
                {
                    result.WeeklyPercent = value.Value;
                    result.WeeklyResetText = FindResetText(segment);
                }
            }
            result.CreditsRemaining = FindCreditsRemaining(pageText);
            return result;
        }

        private static IEnumerable<UsageLabel> FindUsageLabels(string pageText)
        {
            var labels = new List<UsageLabel>();
            AddLabels(labels, pageText, FiveHourLabelRegex, true);
            AddLabels(labels, pageText, WeeklyLabelRegex, false);
            labels.Sort((left, right) => left.Index.CompareTo(right.Index));
            return labels;
        }

        private static void AddLabels(List<UsageLabel> labels, string pageText, Regex regex, bool fiveHour)
        {
            foreach (Match match in regex.Matches(pageText))
            {
                labels.Add(new UsageLabel
                {
                    Index = match.Index,
                    EndIndex = match.Index + match.Length,
                    IsFiveHour = fiveHour,
                    IsWeekly = !fiveHour,
                    IsSpark = IsSparkLabel(pageText, match.Index, match.Length)
                });
            }
        }

        private static bool IsSparkLabel(string pageText, int labelIndex, int labelLength)
        {
            var lineStart = pageText.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, labelIndex));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var lineEnd = pageText.IndexOfAny(new[] { '\r', '\n' }, labelIndex + labelLength);
            if (lineEnd < 0)
            {
                lineEnd = pageText.Length;
            }

            var line = pageText.Substring(lineStart, lineEnd - lineStart);
            if (SparkRegex.IsMatch(line))
            {
                return true;
            }
            if (lineStart > 0 || lineEnd < pageText.Length)
            {
                return false;
            }

            var prefixStart = Math.Max(0, labelIndex - 96);
            var prefix = pageText.Substring(prefixStart, labelIndex - prefixStart);
            return SparkRegex.IsMatch(prefix);
        }

        private static string GetSegmentAfterLabel(string pageText, UsageLabel label)
        {
            var nextLabelIndex = pageText.Length;
            foreach (var next in FindUsageLabels(pageText))
            {
                if (next.Index > label.Index)
                {
                    nextLabelIndex = next.Index;
                    break;
                }
            }

            var length = Math.Min(nextLabelIndex, label.EndIndex + 320) - label.EndIndex;
            if (length <= 0)
            {
                return string.Empty;
            }

            return pageText.Substring(label.EndIndex, length);
        }

        private static int? FindPercentAfterLabel(string segment)
        {
            var match = PercentRegex.Match(segment);
            int value;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out value) || value < 0 || value > 100)
            {
                return null;
            }
            return value;
        }

        private static string FindResetText(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return null;
            }

            foreach (var rawLine in Regex.Split(segment, @"\r\n|\r|\n"))
            {
                var line = NormalizeWhitespace(rawLine);
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.IndexOf("초기화", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SanitizeDisplayText(line, 96);
                }
            }
            return null;
        }

        private static string FindCreditsRemaining(string pageText)
        {
            foreach (Match match in CreditRegex.Matches(pageText ?? string.Empty))
            {
                if (!IsSparkNearby(pageText, match.Index, match.Length))
                {
                    return match.Groups[1].Value.Replace(",", string.Empty);
                }
            }
            return null;
        }

        private static bool IsSparkNearby(string pageText, int index, int length)
        {
            if (string.IsNullOrEmpty(pageText))
            {
                return false;
            }

            var lineStart = pageText.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, index));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var lineEnd = pageText.IndexOfAny(new[] { '\r', '\n' }, index + length);
            if (lineEnd < 0)
            {
                lineEnd = pageText.Length;
            }

            var line = pageText.Substring(lineStart, lineEnd - lineStart);
            return SparkRegex.IsMatch(line);
        }

        private static bool IsValidOptionalPercent(int? value)
        {
            return !value.HasValue || (value.Value >= 0 && value.Value <= 100);
        }

        internal static string CompactResetText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = NormalizeWhitespace(value)
                .Replace("초기화", string.Empty)
                .Replace("Resets", string.Empty)
                .Replace("resets", string.Empty)
                .Replace("Reset", string.Empty)
                .Replace("reset", string.Empty)
                .Replace(" at ", " ")
                .Replace(" on ", " ")
                .Trim(' ', ':', '-', '–');

            text = text.Replace("오전", "AM").Replace("오후", "PM");
            text = CompactKoreanDateRegex.Replace(text, match =>
                match.Groups[2].Value.PadLeft(2, '0') + "-" + match.Groups[3].Value.PadLeft(2, '0'));
            text = Regex.Replace(text, @"\b(AM|PM)\s*(\d{1,2}):(\d{2})", match =>
                ToTwentyFourHour(match.Groups[2].Value, match.Groups[3].Value, match.Groups[1].Value),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"\b(\d{1,2}):(\d{2})\s*(AM|PM)\b", match =>
                ToTwentyFourHour(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return SanitizeDisplayText(text, 32);
        }

        private static string ToTwentyFourHour(string hourText, string minuteText, string marker)
        {
            int hour;
            if (!int.TryParse(hourText, out hour))
            {
                return hourText + ":" + minuteText;
            }

            if (string.Equals(marker, "AM", StringComparison.OrdinalIgnoreCase))
            {
                hour = hour == 12 ? 0 : hour;
            }
            else if (string.Equals(marker, "PM", StringComparison.OrdinalIgnoreCase))
            {
                hour = hour == 12 ? 12 : hour + 12;
            }
            return hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minuteText;
        }

        private static string NormalizeWhitespace(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        private static string SanitizeDisplayText(string value, int maxLength)
        {
            var text = NormalizeWhitespace(value);
            if (text.Length <= maxLength)
            {
                return text;
            }
            return text.Substring(0, maxLength - 1) + "…";
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
        public int? fiveHourPercent { get; set; }
        public int? weeklyPercent { get; set; }
        public int? sparkFiveHourPercent { get; set; }
        public int? sparkWeeklyPercent { get; set; }
        public string fiveHourResetText { get; set; }
        public string weeklyResetText { get; set; }
        public string creditsRemaining { get; set; }
        public string updatedAt { get; set; }
        public string source { get; set; }
        public string status { get; set; }
    }

    internal sealed class UsageParseResult
    {
        public int? FiveHourPercent { get; set; }
        public int? WeeklyPercent { get; set; }
        public int? SparkFiveHourPercent { get; set; }
        public int? SparkWeeklyPercent { get; set; }
        public string FiveHourResetText { get; set; }
        public string WeeklyResetText { get; set; }
        public string CreditsRemaining { get; set; }
    }

    internal sealed class UsageLabel
    {
        public int Index { get; set; }
        public int EndIndex { get; set; }
        public bool IsFiveHour { get; set; }
        public bool IsWeekly { get; set; }
        public bool IsSpark { get; set; }
    }
}
