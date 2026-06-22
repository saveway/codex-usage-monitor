using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CodexUsageMonitorV2
{
    internal sealed class WidgetForm : Form
    {
        private const int LogicalSize = 128;
        private readonly ThemePalette palette;
        private readonly Panel panel;
        private UsageSnapshot snapshot;
        private int logicalWidgetSize = 128;
        private WidgetGraphStyle graphStyle = WidgetGraphStyle.Rings;
        private bool dragging;
        private Point dragStart;

        public event EventHandler WidgetClosedByUser;
        public event EventHandler WidgetMovedOrSized;

        public WidgetForm(ThemePalette palette, ContextMenuStrip menu)
        {
            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }

            this.palette = palette;
            Text = AppInfo.Name + " Widget";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = palette.GetColor("TransparentEdge");
            TransparencyKey = BackColor;
            ClientSize = new Size(LogicalSize, LogicalSize);

            panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ContextMenuStrip = menu
            };
            panel.Paint += PaintWidget;
            panel.MouseDown += HandleMouseDown;
            panel.MouseMove += HandleMouseMove;
            panel.MouseUp += HandleMouseUp;
            panel.MouseClick += HandleMouseClick;
            panel.MouseDoubleClick += HandleMouseDoubleClick;
            Controls.Add(panel);
        }

        public void SetSnapshot(UsageSnapshot value)
        {
            snapshot = value;
            panel.Invalidate();
        }

        public void ApplyTheme()
        {
            BackColor = palette.GetColor("TransparentEdge");
            TransparencyKey = BackColor;
            panel.BackColor = BackColor;
            panel.Invalidate();
        }

        public void ApplySize(int size)
        {
            logicalWidgetSize = size == 256 ? 256 : 128;
            ClientSize = new Size(logicalWidgetSize, logicalWidgetSize);
            panel.Invalidate();
        }

        public void ApplyGraphStyle(WidgetGraphStyle style)
        {
            graphStyle = style;
            panel.Invalidate();
        }

        public int LogicalWidgetSize
        {
            get { return logicalWidgetSize; }
        }

        public void ApplyLocationOrDefault(int? x, int? y)
        {
            var workingArea = Screen.PrimaryScreen.WorkingArea;
            var target = x.HasValue && y.HasValue
                ? new Point(x.Value, y.Value)
                : new Point(workingArea.Right - Width - 16, workingArea.Top + 16);
            Location = ClampToWorkingArea(target, Size);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                WidgetClosedByUser?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnFormClosing(e);
        }

        private void PaintWidget(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(palette.GetColor("TransparentEdge"));
            var scale = panel.Width / (float)LogicalSize;
            var state = g.Save();
            g.ScaleTransform(scale, scale);
            try
            {
                if (snapshot == null)
                {
                    PaintNoData(g);
                }
                else
                {
                    PaintUsage(g);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private void PaintNoData(Graphics g)
        {
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var mutedBrush = new SolidBrush(palette.GetColor("MutedText")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var bodyFont = new Font("Segoe UI", 7f))
            {
                DrawCodexMark(g, 64, 36, 10f);
                g.DrawString("No data yet", titleFont, textBrush, 28, 58);
                g.DrawString("Login or Fetch now", bodyFont, mutedBrush, 22, 78);
                g.DrawString("required", bodyFont, mutedBrush, 45, 92);
                g.DrawString("x", titleFont, closeBrush, 113, 3);
            }
        }

        private void PaintUsage(Graphics g)
        {
            switch (graphStyle)
            {
                case WidgetGraphStyle.Bars:
                    PaintBars(g);
                    break;
                case WidgetGraphStyle.Meters:
                    PaintMeters(g);
                    break;
                case WidgetGraphStyle.Battery:
                    PaintBattery(g);
                    break;
                default:
                    PaintRings(g);
                    break;
            }
        }

        private void PaintRings(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var trackPen = CreateRoundPen(palette.GetColor("Track"), 7f))
            using (var fiveHourPen = CreateRoundPen(palette.GetFiveHourColor(fiveHour), 7f))
            using (var weeklyPen = CreateRoundPen(palette.GetWeeklyColor(weekly), 7f))
            using (var centerBrush = new SolidBrush(palette.GetColor("CenterFill")))
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var fontSmall = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                g.DrawArc(trackPen, 25, 25, 78, 78, -90, 360);
                g.DrawArc(fiveHourPen, 25, 25, 78, 78, -90, fiveHour * 3.6f);
                g.DrawArc(trackPen, 41, 41, 46, 46, -90, 360);
                g.DrawArc(weeklyPen, 41, 41, 46, 46, -90, weekly * 3.6f);
                g.FillEllipse(centerBrush, 53, 53, 22, 22);
                DrawCodexMark(g, 64, 64, 8f);
                DrawUsageLines(g, textBrush, fontSmall, 91, 105);
                g.DrawString("x", fontSmall, closeBrush, 113, 3);
            }
        }

        private void PaintBars(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var trackBrush = new SolidBrush(palette.GetColor("Track")))
            using (var fiveHourBrush = new SolidBrush(palette.GetFiveHourColor(fiveHour)))
            using (var weeklyBrush = new SolidBrush(palette.GetWeeklyColor(weekly)))
            using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var centerBrush = new SolidBrush(palette.GetColor("CenterFill")))
            {
                DrawProgressBar(g, trackBrush, fiveHourBrush, 18, 30, 92, 16, fiveHour);
                DrawProgressBar(g, trackBrush, weeklyBrush, 18, 92, 92, 16, weekly);
                g.FillEllipse(centerBrush, 53, 53, 22, 22);
                DrawCodexMark(g, 64, 64, 8f);
                DrawUsageTextLine(g, BuildFiveHourLine(fiveHour), font, textBrush, 6, 14, 116);
                DrawUsageTextLine(g, BuildWeeklyLine(weekly), font, textBrush, 6, 76, 116);
                g.DrawString("x", font, closeBrush, 113, 3);
            }
        }

        private void PaintMeters(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var centerBrush = new SolidBrush(palette.GetColor("CenterFill")))
            {
                DrawMeter(g, new RectangleF(14, 22, 44, 34), fiveHour, palette.GetFiveHourColor(fiveHour));
                DrawMeter(g, new RectangleF(70, 22, 44, 34), weekly, palette.GetWeeklyColor(weekly));
                g.FillEllipse(centerBrush, 53, 53, 22, 22);
                DrawCodexMark(g, 64, 64, 8f);
                DrawUsageLines(g, textBrush, font, 76, 91);
                g.DrawString("x", font, closeBrush, 113, 3);
            }
        }

        private void PaintBattery(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var outlinePen = new Pen(palette.GetColor("BatteryOutline"), 2f))
            using (var trackBrush = new SolidBrush(palette.GetColor("Track")))
            using (var fiveHourBrush = new SolidBrush(palette.GetFiveHourColor(fiveHour)))
            using (var weeklyBrush = new SolidBrush(palette.GetWeeklyColor(weekly)))
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var centerBrush = new SolidBrush(palette.GetColor("CenterFill")))
            {
                DrawBattery(g, outlinePen, trackBrush, fiveHourBrush, 18, 34, 88, 18, fiveHour);
                DrawBattery(g, outlinePen, trackBrush, weeklyBrush, 18, 92, 88, 18, weekly);
                g.FillEllipse(centerBrush, 53, 53, 22, 22);
                DrawCodexMark(g, 64, 64, 8f);
                DrawUsageTextLine(g, BuildFiveHourLine(fiveHour), font, textBrush, 6, 18, 116);
                DrawUsageTextLine(g, BuildWeeklyLine(weekly), font, textBrush, 6, 76, 116);
                g.DrawString("x", font, closeBrush, 113, 3);
            }
        }

        private void DrawUsageLines(Graphics g, Brush brush, Font baseFont, float firstY, float secondY)
        {
            DrawUsageTextLine(g, BuildFiveHourLine(Clamp(snapshot.fiveHourRemaining)), baseFont, brush, 4, firstY, 120);
            DrawUsageTextLine(g, BuildWeeklyLine(Clamp(snapshot.weeklyRemaining)), baseFont, brush, 4, secondY, 120);
        }

        private void DrawUsageTextLine(Graphics g, string text, Font baseFont, Brush brush, float x, float y, float maxWidth)
        {
            using (var font = CreateFittingFont(g, text, baseFont, maxWidth))
            {
                g.DrawString(text, font, brush, x, y);
            }
        }

        private static Font CreateFittingFont(Graphics g, string text, Font baseFont, float maxWidth)
        {
            var size = baseFont.Size;
            while (size > 4.6f)
            {
                using (var candidate = new Font(baseFont.FontFamily, size, baseFont.Style))
                {
                    if (g.MeasureString(text, candidate).Width <= maxWidth)
                    {
                        return new Font(baseFont.FontFamily, size, baseFont.Style);
                    }
                }
                size -= 0.4f;
            }
            return new Font(baseFont.FontFamily, 4.6f, baseFont.Style);
        }

        private string BuildFiveHourLine(int percent)
        {
            return "5H " + percent + "% / " + FormatFiveHourRemaining(snapshot.fiveHourResetText);
        }

        private string BuildWeeklyLine(int percent)
        {
            return "W " + percent + "% / " + FormatWeeklyRemaining(snapshot.weeklyResetText);
        }

        private static string FormatFiveHourRemaining(string resetText)
        {
            DateTime resetAt;
            var remaining = TryParseResetDateTime(resetText, out resetAt) ? resetAt - DateTime.Now : TimeSpan.Zero;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            var totalHours = Math.Min(99, (int)Math.Floor(remaining.TotalHours));
            return totalHours.ToString("00", CultureInfo.InvariantCulture) +
                "시" + remaining.Minutes.ToString("00", CultureInfo.InvariantCulture) + "분";
        }

        private static string FormatWeeklyRemaining(string resetText)
        {
            DateTime resetAt;
            var remaining = TryParseResetDateTime(resetText, out resetAt) ? resetAt - DateTime.Now : TimeSpan.Zero;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            var totalDays = Math.Min(99, (int)Math.Floor(remaining.TotalDays));
            return totalDays.ToString("00", CultureInfo.InvariantCulture) +
                "일" + remaining.Hours.ToString("00", CultureInfo.InvariantCulture) + "시";
        }

        private static bool TryParseResetDateTime(string resetText, out DateTime resetAt)
        {
            resetAt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(resetText))
            {
                return false;
            }

            var text = Regex.Replace(resetText, @"\s+", " ").Trim();
            text = text.Replace("초기화", string.Empty).Trim();
            var match = Regex.Match(
                text,
                @"(?:(\d{4})\.\s*(\d{1,2})\.\s*(\d{1,2})\.\s*)?(오전|오후|AM|PM)?\s*(\d{1,2}):(\d{2})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            var now = DateTime.Now;
            var year = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : now.Year;
            var month = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : now.Month;
            var day = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : now.Day;
            var marker = match.Groups[4].Value;
            var hour = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);

            if (string.Equals(marker, "오전", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(marker, "AM", StringComparison.OrdinalIgnoreCase))
            {
                hour = hour == 12 ? 0 : hour;
            }
            else if (string.Equals(marker, "오후", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(marker, "PM", StringComparison.OrdinalIgnoreCase))
            {
                hour = hour == 12 ? 12 : hour + 12;
            }

            try
            {
                resetAt = new DateTime(year, month, day, hour, minute, 0);
                if (!match.Groups[1].Success && resetAt < now)
                {
                    resetAt = resetAt.AddDays(1);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DrawProgressBar(Graphics g, Brush trackBrush, Brush fillBrush, int x, int y, int width, int height, int percent)
        {
            g.FillRectangle(trackBrush, x, y, width, height);
            g.FillRectangle(fillBrush, x, y, Math.Max(1, width * percent / 100), height);
            using (var pen = new Pen(Color.FromArgb(120, Color.White), 1f))
            {
                g.DrawRectangle(pen, x, y, width, height);
            }
        }

        private void DrawMeter(Graphics g, RectangleF bounds, int percent, Color color)
        {
            using (var trackPen = CreateRoundPen(palette.GetColor("Track"), 5f))
            using (var valuePen = CreateRoundPen(color, 5f))
            using (var needlePen = new Pen(palette.GetColor("Text"), 1.6f))
            using (var hubBrush = new SolidBrush(palette.GetColor("CenterFill")))
            {
                g.DrawArc(trackPen, bounds, 180, 180);
                g.DrawArc(valuePen, bounds, 180, percent * 1.8f);
                var angle = Math.PI * (180d + percent * 1.8d) / 180d;
                var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                var length = bounds.Width * 0.43f;
                var end = new PointF(
                    center.X + (float)Math.Cos(angle) * length,
                    center.Y + (float)Math.Sin(angle) * length);
                g.DrawLine(needlePen, center, end);
                g.FillEllipse(hubBrush, center.X - 2.5f, center.Y - 2.5f, 5, 5);
            }
        }

        private static void DrawBattery(Graphics g, Pen outlinePen, Brush trackBrush, Brush fillBrush, int x, int y, int width, int height, int percent)
        {
            var body = new Rectangle(x, y, width, height);
            var nub = new Rectangle(x + width + 2, y + height / 3, 4, height / 3);
            g.FillRectangle(trackBrush, body);
            g.FillRectangle(fillBrush, x + 2, y + 2, Math.Max(1, (width - 4) * percent / 100), height - 4);
            g.DrawRectangle(outlinePen, body);
            g.DrawRectangle(outlinePen, nub);
        }

        private void DrawCodexMark(Graphics g, float centerX, float centerY, float fontSize)
        {
            using (var font = new Font("Consolas", fontSize, FontStyle.Bold))
            using (var brush = new SolidBrush(palette.GetColor("CodexMark")))
            {
                const string text = "C>";
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, centerX - size.Width / 2f, centerY - size.Height / 2f);
            }
        }

        private void HandleMouseDown(object sender, MouseEventArgs e)
        {
            var point = ToLogicalPoint(e.Location);
            if (point.X >= 108 && point.Y <= 24)
            {
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragStart = e.Location;
            }
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }
            var screenPoint = panel.PointToScreen(e.Location);
            Location = new Point(screenPoint.X - dragStart.X, screenPoint.Y - dragStart.Y);
        }

        private void HandleMouseUp(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                dragging = false;
                WidgetMovedOrSized?.Invoke(this, EventArgs.Empty);
            }
        }

        private void HandleMouseClick(object sender, MouseEventArgs e)
        {
            var point = ToLogicalPoint(e.Location);
            if (point.X >= 108 && point.Y <= 24)
            {
                Hide();
                WidgetClosedByUser?.Invoke(this, EventArgs.Empty);
            }
        }

        private void HandleMouseDoubleClick(object sender, MouseEventArgs e)
        {
            var point = ToLogicalPoint(e.Location);
            if (point.X >= 48 && point.X <= 80 && point.Y >= 28 && point.Y <= 64)
            {
                var newSize = logicalWidgetSize == 128 ? 256 : 128;
                ApplySize(newSize);
                ApplyLocationOrDefault(Location.X, Location.Y);
                WidgetMovedOrSized?.Invoke(this, EventArgs.Empty);
            }
        }

        private PointF ToLogicalPoint(Point point)
        {
            var scale = panel.Width / (float)LogicalSize;
            return new PointF(point.X / scale, point.Y / scale);
        }

        private static Pen CreateRoundPen(Color color, float width)
        {
            return new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
        }

        private static int Clamp(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        private static Point ClampToWorkingArea(Point point, Size size)
        {
            var workingArea = Screen.FromPoint(point).WorkingArea;
            var x = Math.Max(workingArea.Left, Math.Min(point.X, workingArea.Right - size.Width));
            var y = Math.Max(workingArea.Top, Math.Min(point.Y, workingArea.Bottom - size.Height));
            return new Point(x, y);
        }
    }
}
