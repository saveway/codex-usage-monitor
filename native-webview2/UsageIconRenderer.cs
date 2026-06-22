using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexUsageMonitorV2
{
    internal static class UsageIconRenderer
    {
        public static Icon Create(int fiveHourRemaining, int weeklyRemaining, ThemePalette palette)
        {
            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }

            using (var bitmap = new Bitmap(64, 64))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var trackPen = CreateRoundPen(palette.GetColor("Track"), 8f))
            using (var fiveHourPen = CreateRoundPen(palette.GetFiveHourColor(fiveHourRemaining), 8f))
            using (var weeklyPen = CreateRoundPen(palette.GetWeeklyColor(weeklyRemaining), 8f))
            using (var centerBrush = new SolidBrush(palette.GetColor("CenterFill")))
            using (var markPen = CreateRoundPen(palette.GetColor("CodexMark"), 2.5f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                graphics.DrawArc(trackPen, 7, 7, 50, 50, -90, 360);
                graphics.DrawArc(
                    fiveHourPen,
                    7,
                    7,
                    50,
                    50,
                    -90,
                    ClampPercentage(fiveHourRemaining) * 3.6f);
                graphics.DrawArc(trackPen, 18, 18, 28, 28, -90, 360);
                graphics.DrawArc(
                    weeklyPen,
                    18,
                    18,
                    28,
                    28,
                    -90,
                    ClampPercentage(weeklyRemaining) * 3.6f);

                graphics.FillEllipse(centerBrush, 22, 22, 20, 20);
                graphics.DrawArc(markPen, 26, 27, 12, 10, 205, 285);
                graphics.DrawLine(markPen, 29, 32, 35, 32);

                var handle = bitmap.GetHicon();
                try
                {
                    using (var icon = Icon.FromHandle(handle))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static Pen CreateRoundPen(Color color, float width)
        {
            return new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
        }

        private static int ClampPercentage(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
