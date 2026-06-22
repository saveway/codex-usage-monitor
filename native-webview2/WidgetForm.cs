using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            using (var mutedBrush = new SolidBrush(palette.GetColor("MutedText")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var fontSmall = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var fontTiny = new Font("Segoe UI", 6.5f))
            {
                g.DrawArc(trackPen, 25, 4, 78, 78, -90, 360);
                g.DrawArc(fiveHourPen, 25, 4, 78, 78, -90, fiveHour * 3.6f);
                g.DrawArc(trackPen, 41, 20, 46, 46, -90, 360);
                g.DrawArc(weeklyPen, 41, 20, 46, 46, -90, weekly * 3.6f);
                g.FillEllipse(centerBrush, 53, 33, 22, 22);
                DrawCodexMark(g, 64, 44, 8f);
                g.DrawString("5h " + fiveHour + "%", fontSmall, textBrush, 8, 84);
                g.DrawString("W " + weekly + "%", fontSmall, textBrush, 72, 84);
                g.DrawString("Codex", fontTiny, mutedBrush, 47, 106);
                g.DrawString("x", fontSmall, closeBrush, 113, 3);
            }
        }

        private void PaintBars(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var mutedBrush = new SolidBrush(palette.GetColor("MutedText")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var trackBrush = new SolidBrush(palette.GetColor("Track")))
            using (var fiveHourBrush = new SolidBrush(palette.GetFiveHourColor(fiveHour)))
            using (var weeklyBrush = new SolidBrush(palette.GetWeeklyColor(weekly)))
            using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var tiny = new Font("Segoe UI", 6.5f))
            {
                DrawCodexMark(g, 64, 20, 8f);
                DrawProgressBar(g, trackBrush, fiveHourBrush, 18, 39, 92, 16, fiveHour);
                DrawProgressBar(g, trackBrush, weeklyBrush, 18, 70, 92, 16, weekly);
                g.DrawString("5h " + fiveHour + "%", font, textBrush, 18, 24);
                g.DrawString("W " + weekly + "%", font, textBrush, 18, 55);
                g.DrawString("Bars", tiny, mutedBrush, 54, 102);
                g.DrawString("x", font, closeBrush, 113, 3);
            }
        }

        private void PaintMeters(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var mutedBrush = new SolidBrush(palette.GetColor("MutedText")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var tiny = new Font("Segoe UI", 6.5f))
            {
                DrawMeter(g, new RectangleF(14, 22, 44, 34), fiveHour, palette.GetFiveHourColor(fiveHour));
                DrawMeter(g, new RectangleF(70, 22, 44, 34), weekly, palette.GetWeeklyColor(weekly));
                DrawCodexMark(g, 64, 71, 8f);
                g.DrawString("5h " + fiveHour + "%", font, textBrush, 12, 84);
                g.DrawString("W " + weekly + "%", font, textBrush, 72, 84);
                g.DrawString("Meters", tiny, mutedBrush, 48, 106);
                g.DrawString("x", font, closeBrush, 113, 3);
            }
        }

        private void PaintBattery(Graphics g)
        {
            var fiveHour = Clamp(snapshot.fiveHourRemaining);
            var weekly = Clamp(snapshot.weeklyRemaining);
            using (var textBrush = new SolidBrush(palette.GetColor("Text")))
            using (var mutedBrush = new SolidBrush(palette.GetColor("MutedText")))
            using (var closeBrush = new SolidBrush(palette.GetColor("Close")))
            using (var outlinePen = new Pen(palette.GetColor("BatteryOutline"), 2f))
            using (var trackBrush = new SolidBrush(palette.GetColor("Track")))
            using (var fiveHourBrush = new SolidBrush(palette.GetFiveHourColor(fiveHour)))
            using (var weeklyBrush = new SolidBrush(palette.GetWeeklyColor(weekly)))
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var tiny = new Font("Segoe UI", 6.5f))
            {
                DrawBattery(g, outlinePen, trackBrush, fiveHourBrush, 18, 35, 88, 18, fiveHour);
                DrawBattery(g, outlinePen, trackBrush, weeklyBrush, 18, 70, 88, 18, weekly);
                g.DrawString("5h " + fiveHour + "%", font, textBrush, 18, 19);
                g.DrawString("W " + weekly + "%", font, textBrush, 18, 54);
                g.DrawString("Battery", tiny, mutedBrush, 47, 106);
                DrawCodexMark(g, 64, 100, 7f);
                g.DrawString("x", font, closeBrush, 113, 3);
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
            using (var needlePen = new Pen(palette.GetColor("Text"), 2f))
            using (var hubBrush = new SolidBrush(palette.GetColor("CenterFill")))
            {
                g.DrawArc(trackPen, bounds, 180, 180);
                g.DrawArc(valuePen, bounds, 180, percent * 1.8f);
                var angle = Math.PI * (1d + percent / 100d);
                var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height);
                var length = bounds.Width * 0.35f;
                var end = new PointF(
                    center.X + (float)Math.Cos(angle) * length,
                    center.Y + (float)Math.Sin(angle) * length);
                g.DrawLine(needlePen, center, end);
                g.FillEllipse(hubBrush, center.X - 3, center.Y - 3, 6, 6);
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
