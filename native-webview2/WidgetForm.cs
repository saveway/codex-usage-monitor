using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
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
        private static bool codexIconLoadAttempted;
        private static Bitmap codexIconBitmap;
        private static Rectangle codexIconVisibleBounds;

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
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
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
                DrawCodexMark(g, LogicalCenterX(), 36, GetLogicalIconSize(WidgetGraphStyle.Rings));
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
                var outerRing = new RectangleF(30, 10, 68, 68);
                var innerRing = new RectangleF(44, 24, 40, 40);
                var ringCenter = CenterOf(outerRing);
                g.DrawArc(trackPen, outerRing, -90, 360);
                g.DrawArc(fiveHourPen, outerRing, -90, fiveHour * 3.6f);
                g.DrawArc(trackPen, innerRing, -90, 360);
                g.DrawArc(weeklyPen, innerRing, -90, weekly * 3.6f);
                g.FillEllipse(centerBrush, ringCenter.X - 11, ringCenter.Y - 11, 22, 22);
                DrawCodexMark(g, ringCenter.X, ringCenter.Y, GetLogicalIconSize(WidgetGraphStyle.Rings));
                DrawUsageLines(g, textBrush, fontSmall, ringCenter.X, 91, 105, 4, 124);
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
                var fiveHourBar = new Rectangle(18, 30, 92, 16);
                var weeklyBar = new Rectangle(18, 92, 92, 16);
                var weeklyTextTop = 76f;
                var widgetCenterX = LogicalCenterX();
                DrawProgressBar(g, trackBrush, fiveHourBrush, fiveHourBar, fiveHour);
                DrawProgressBar(g, trackBrush, weeklyBrush, weeklyBar, weekly);
                var iconAnchor = new PointF(widgetCenterX, (fiveHourBar.Bottom + weeklyTextTop) / 2f);
                g.FillEllipse(centerBrush, iconAnchor.X - 11, iconAnchor.Y - 11, 22, 22);
                DrawCodexMark(g, iconAnchor.X, iconAnchor.Y, GetLogicalIconSize(WidgetGraphStyle.Bars));
                DrawUsageTextLine(g, BuildFiveHourLine(fiveHour), font, textBrush, CenterX(fiveHourBar), 14, 6, 122);
                DrawUsageTextLine(g, BuildWeeklyLine(weekly), font, textBrush, CenterX(weeklyBar), weeklyTextTop, 6, 122);
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
                var fiveHourMeter = new RectangleF(14, 22, 44, 34);
                var weeklyMeter = new RectangleF(70, 22, 44, 34);
                var meterGroup = Union(fiveHourMeter, weeklyMeter);
                var groupCenterX = CenterX(meterGroup);
                var fiveHourTextTop = 86f;
                var weeklyTextTop = 101f;
                var iconAnchor = new PointF(groupCenterX, (meterGroup.Bottom + fiveHourTextTop) / 2f);
                DrawMeter(g, fiveHourMeter, fiveHour, palette.GetFiveHourColor(fiveHour));
                DrawMeter(g, weeklyMeter, weekly, palette.GetWeeklyColor(weekly));
                g.FillEllipse(centerBrush, iconAnchor.X - 11, iconAnchor.Y - 11, 22, 22);
                DrawCodexMark(g, iconAnchor.X, iconAnchor.Y, GetLogicalIconSize(WidgetGraphStyle.Meters));
                DrawUsageLines(g, textBrush, font, groupCenterX, fiveHourTextTop, weeklyTextTop, 4, 124);
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
                var fiveHourBody = new Rectangle(18, 30, 88, 18);
                var weeklyBody = new Rectangle(18, 92, 88, 18);
                var fiveHourTextTop = 14f;
                var weeklyTextTop = 76f;
                var bodyCenterX = CenterX(fiveHourBody);
                var iconAnchor = new PointF(bodyCenterX, (fiveHourBody.Bottom + weeklyTextTop) / 2f);
                DrawBattery(g, outlinePen, trackBrush, fiveHourBrush, fiveHourBody, fiveHour);
                DrawBattery(g, outlinePen, trackBrush, weeklyBrush, weeklyBody, weekly);
                g.FillEllipse(centerBrush, iconAnchor.X - 11, iconAnchor.Y - 11, 22, 22);
                DrawCodexMark(g, iconAnchor.X, iconAnchor.Y, GetLogicalIconSize(WidgetGraphStyle.Battery));
                DrawUsageTextLine(g, BuildFiveHourLine(fiveHour), font, textBrush, bodyCenterX, fiveHourTextTop, 6, 118);
                DrawUsageTextLine(g, BuildWeeklyLine(weekly), font, textBrush, CenterX(weeklyBody), weeklyTextTop, 6, 118);
                g.DrawString("x", font, closeBrush, 113, 3);
            }
        }

        private void DrawUsageLines(Graphics g, Brush brush, Font baseFont, float centerX, float firstY, float secondY, float minX, float maxX)
        {
            DrawUsageTextLine(g, BuildFiveHourLine(Clamp(snapshot.fiveHourRemaining)), baseFont, brush, centerX, firstY, minX, maxX);
            DrawUsageTextLine(g, BuildWeeklyLine(Clamp(snapshot.weeklyRemaining)), baseFont, brush, centerX, secondY, minX, maxX);
        }

        private void DrawUsageTextLine(Graphics g, string text, Font baseFont, Brush brush, float centerX, float y, float minX, float maxX)
        {
            var maxWidth = Math.Max(1f, maxX - minX);
            using (var font = CreateFittingFont(g, text, baseFont, maxWidth))
            {
                var size = g.MeasureString(text, font);
                var x = centerX - size.Width / 2f;
                if (x < minX)
                {
                    x = minX;
                }
                if (x + size.Width > maxX)
                {
                    x = maxX - size.Width;
                }
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

        private static void DrawProgressBar(Graphics g, Brush trackBrush, Brush fillBrush, Rectangle bounds, int percent)
        {
            g.FillRectangle(trackBrush, bounds);
            g.FillRectangle(fillBrush, bounds.X, bounds.Y, Math.Max(1, bounds.Width * percent / 100), bounds.Height);
            using (var pen = new Pen(Color.FromArgb(120, Color.White), 1f))
            {
                g.DrawRectangle(pen, bounds);
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

        private static void DrawBattery(Graphics g, Pen outlinePen, Brush trackBrush, Brush fillBrush, Rectangle body, int percent)
        {
            var nub = new Rectangle(body.Right + 2, body.Y + body.Height / 3, 4, body.Height / 3);
            g.FillRectangle(trackBrush, body);
            g.FillRectangle(fillBrush, body.X + 2, body.Y + 2, Math.Max(1, (body.Width - 4) * percent / 100), body.Height - 4);
            g.DrawRectangle(outlinePen, body);
            g.DrawRectangle(outlinePen, nub);
        }

        private static float LogicalCenterX()
        {
            return LogicalSize / 2f;
        }

        private static float LogicalCenterY()
        {
            return LogicalSize / 2f;
        }

        private float GetLogicalIconSize(WidgetGraphStyle style)
        {
            if (style == WidgetGraphStyle.Battery)
            {
                return 17f;
            }
            if (style == WidgetGraphStyle.Bars || style == WidgetGraphStyle.Meters)
            {
                return 18f;
            }
            return logicalWidgetSize == 256 ? 19f : 26f;
        }

        private static PointF CenterOf(RectangleF bounds)
        {
            return new PointF(CenterX(bounds), bounds.Top + bounds.Height / 2f);
        }

        private static float CenterX(Rectangle bounds)
        {
            return bounds.Left + bounds.Width / 2f;
        }

        private static float CenterX(RectangleF bounds)
        {
            return bounds.Left + bounds.Width / 2f;
        }

        private static RectangleF Union(RectangleF first, RectangleF second)
        {
            return RectangleF.Union(first, second);
        }

        private void DrawCodexMark(Graphics g, float centerX, float centerY, float iconSize)
        {
            var iconBitmap = GetCodexIconBitmap();
            if (iconBitmap != null)
            {
                var rect = CalculateCodexIconDrawRect(centerX, centerY, iconSize, iconBitmap, codexIconVisibleBounds);
                g.DrawImage(iconBitmap, rect);
                return;
            }

            using (var font = new Font("Consolas", iconSize * 0.44f, FontStyle.Bold))
            using (var brush = new SolidBrush(palette.GetColor("CodexMark")))
            {
                const string text = "C>";
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, centerX - size.Width / 2f, centerY - size.Height / 2f);
            }
        }

        private static Bitmap GetCodexIconBitmap()
        {
            if (codexIconLoadAttempted)
            {
                return codexIconBitmap;
            }
            codexIconLoadAttempted = true;

            var path = FindCodexExePath();
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted != null)
                {
                    codexIconBitmap = extracted.ToBitmap();
                    codexIconVisibleBounds = CalculateVisibleBounds(codexIconBitmap);
                    extracted.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Codex widget icon could not be loaded from " + path + ": " + ex.Message);
            }
            return codexIconBitmap;
        }

        private static RectangleF CalculateCodexIconDrawRect(float anchorX, float anchorY, float iconSize, Bitmap bitmap, Rectangle visibleBounds)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0 || visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
            {
                return new RectangleF(anchorX - iconSize / 2f, anchorY - iconSize / 2f, iconSize, iconSize);
            }

            var visibleCenterX = visibleBounds.Left + visibleBounds.Width / 2f;
            var visibleCenterY = visibleBounds.Top + visibleBounds.Height / 2f;
            var normalizedCenterX = visibleCenterX / bitmap.Width;
            var normalizedCenterY = visibleCenterY / bitmap.Height;
            return new RectangleF(
                anchorX - normalizedCenterX * iconSize,
                anchorY - normalizedCenterY * iconSize,
                iconSize,
                iconSize);
        }

        private static Rectangle CalculateVisibleBounds(Bitmap bitmap)
        {
            var left = bitmap.Width;
            var top = bitmap.Height;
            var right = -1;
            var bottom = -1;

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A <= 12)
                    {
                        continue;
                    }

                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x > right) right = x;
                    if (y > bottom) bottom = y;
                }
            }

            if (right < left || bottom < top)
            {
                return new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            }
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private static string FindCodexExePath()
        {
            const string exactPath = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.616.4196.0_x64__2p2nqsd0c76g0\app\Codex.exe";
            if (File.Exists(exactPath))
            {
                return exactPath;
            }

            try
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "WindowsApps");
                if (!Directory.Exists(root))
                {
                    return null;
                }

                var bestPath = null as string;
                var bestName = null as string;
                foreach (var directory in Directory.GetDirectories(root, "OpenAI.Codex_*_x64__2p2nqsd0c76g0"))
                {
                    var candidate = Path.Combine(directory, "app", "Codex.exe");
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }
                    var name = Path.GetFileName(directory);
                    if (bestName == null || string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        bestName = name;
                        bestPath = candidate;
                    }
                }
                return bestPath;
            }
            catch (Exception ex)
            {
                AppLogger.Write("Codex widget icon search failed: " + ex.Message);
                return null;
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
