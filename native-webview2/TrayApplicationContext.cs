using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexUsageMonitorV2
{
    internal sealed class TrayApplicationContext : ApplicationContext, IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly BrowserForm browserForm;
        private readonly Icon appIcon;
        private readonly Timer autoRefreshTimer;
        private readonly Dictionary<int, ToolStripMenuItem> autoRefreshItems = new Dictionary<int, ToolStripMenuItem>();
        private readonly Dictionary<string, ToolStripMenuItem> colorMenuItems =
            new Dictionary<string, ToolStripMenuItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<WidgetGraphStyle, ToolStripMenuItem> graphStyleItems =
            new Dictionary<WidgetGraphStyle, ToolStripMenuItem>();
        private readonly Dictionary<WidgetLogoMode, ToolStripMenuItem> logoModeItems =
            new Dictionary<WidgetLogoMode, ToolStripMenuItem>();
        private readonly AppSettings settings;
        private readonly ThemePalette palette;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem acknowledgeAlertMenuItem;
        private UsageSnapshot lastSnapshot;
        private WidgetForm widgetForm;
        private Icon usageIcon;
        private string currentStatus = "nodata";
        private int autoRefreshMinutes;
        private DateTime? nextAutoRefreshAt;
        private bool operationInProgress;
        private bool exiting;
        private bool disposed;
        private WidgetGraphStyle currentGraphStyle;
        private WidgetLogoMode currentLogoMode;
        private string currentFiveHourAlertKey;
        private string currentWeeklyAlertKey;
        private string lastNotifiedFiveHourAlertKey;
        private string lastNotifiedWeeklyAlertKey;

        public TrayApplicationContext()
        {
            AppLogger.Write("Application starting.");
            var cleanup = ProfileCacheCleaner.Clean();
            AppLogger.Write("Startup cache cleanup removed " + cleanup.RemovedBytes + " bytes.");

            settings = AppSettingsStore.Load();
            palette = new ThemePalette(settings.colors);
            currentGraphStyle = WidgetGraphStyleHelper.Normalize(settings.graphStyle);
            currentLogoMode = WidgetLogoModeHelper.Normalize(settings.logoMode);
            appIcon = AppIcon.Create();
            browserForm = new BrowserForm();
            browserForm.UsageUpdated += HandleUsageUpdated;
            browserForm.StatusChanged += HandleStatusChanged;

            menu = new ContextMenuStrip();
            menu.Items.Add("Open/Login usage page", null, async (sender, args) => await OpenBrowserAsync(false, false));
            menu.Items.Add("Fetch now", null, async (sender, args) => await OpenBrowserAsync(true, false));
            menu.Items.Add(CreateAutoRefreshMenu());
            menu.Items.Add(CreateColorsMenu());
            menu.Items.Add(CreateGraphStyleMenu());
            menu.Items.Add(CreateLogoModeMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show widget", null, (sender, args) => ShowWidget(true));
            acknowledgeAlertMenuItem = new ToolStripMenuItem("Acknowledge current alert");
            acknowledgeAlertMenuItem.Click += (sender, args) => AcknowledgeCurrentAlert();
            acknowledgeAlertMenuItem.Enabled = false;
            menu.Items.Add(acknowledgeAlertMenuItem);
            menu.Items.Add("Reload saved data", null, (sender, args) => ReloadSavedData(true));
            menu.Items.Add("Open data file", null, (sender, args) => OpenFile(AppPaths.DataPath, "No usage data has been saved yet."));
            menu.Items.Add("Open log", null, (sender, args) => OpenFile(AppPaths.LogPath, "The log file does not exist yet."));
            menu.Items.Add("Clear WebView2 cache", null, (sender, args) => ClearWebView2Cache());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("About", null, (sender, args) => ShowAbout());
            menu.Items.Add("Exit", null, (sender, args) => ExitApplication());

            notifyIcon = new NotifyIcon
            {
                Icon = appIcon,
                Text = AppInfo.Name + " | 5h -- | W -- | Never | No data",
                ContextMenuStrip = menu,
                Visible = true
            };
            notifyIcon.DoubleClick += (sender, args) => ShowWidget(false);

            autoRefreshTimer = new Timer();
            autoRefreshTimer.Tick += HandleAutoRefreshTick;
            ReloadSavedData(false);
            ApplyAutoRefresh(settings.autoRefreshMinutes, false, false);
            ApplyGraphStyle(currentGraphStyle, false, false);
            ApplyLogoMode(currentLogoMode, false, false);
            if (settings.widgetVisible == true)
            {
                ShowWidget(false);
            }
        }

        protected override void ExitThreadCore()
        {
            if (exiting)
            {
                return;
            }
            exiting = true;
            AppLogger.Write("Application exit started.");

            try
            {
                autoRefreshTimer.Stop();
                autoRefreshTimer.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Auto refresh timer cleanup failed: " + ex.Message);
            }

            try
            {
                browserForm.CloseForExit();
                browserForm.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Browser form cleanup failed: " + ex.Message);
            }

            try
            {
                if (widgetForm != null)
                {
                    widgetForm.Close();
                    widgetForm.Dispose();
                    widgetForm = null;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Widget cleanup failed: " + ex.Message);
            }

            try
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Tray icon cleanup failed: " + ex.Message);
            }

            try
            {
                if (usageIcon != null)
                {
                    usageIcon.Dispose();
                    usageIcon = null;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Dynamic usage icon cleanup failed: " + ex.Message);
            }

            try
            {
                appIcon.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Application icon cleanup failed: " + ex.Message);
            }

            try
            {
                ProfileCacheCleaner.Clean();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Exit cache cleanup failed: " + ex.Message);
            }

            AppLogger.Write("Application exit finished.");
            base.ExitThreadCore();
        }

        public new void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (!exiting)
            {
                ExitThreadCore();
            }
            base.Dispose();
        }

        private async Task<FetchResult> OpenBrowserAsync(bool fetchNow, bool automatic)
        {
            if (exiting)
            {
                return FetchResult.Stopped;
            }
            if (operationInProgress || browserForm.IsBusy)
            {
                if (automatic)
                {
                    AppLogger.Write("Automatic refresh skipped because another operation is in progress.");
                }
                else
                {
                    notifyIcon.ShowBalloonTip(3000, AppInfo.Name, "Another usage operation is already in progress.", ToolTipIcon.Warning);
                }
                return FetchResult.Busy;
            }

            operationInProgress = true;
            if (fetchNow)
            {
                autoRefreshTimer.Stop();
                nextAutoRefreshAt = null;
                UpdateTooltip();
            }
            try
            {
                return await browserForm.OpenAsync(fetchNow, automatic);
            }
            finally
            {
                operationInProgress = false;
                if (fetchNow && !exiting && autoRefreshMinutes > 0)
                {
                    ScheduleNextAutoRefresh();
                }
            }
        }

        private ToolStripMenuItem CreateAutoRefreshMenu()
        {
            var parent = new ToolStripMenuItem("Auto refresh");
            AddAutoRefreshItem(parent, "Off", 0);
            AddAutoRefreshItem(parent, "10 minutes", 10);
            AddAutoRefreshItem(parent, "15 minutes", 15);
            AddAutoRefreshItem(parent, "30 minutes", 30);
            AddAutoRefreshItem(parent, "60 minutes", 60);
            return parent;
        }

        private void AddAutoRefreshItem(ToolStripMenuItem parent, string text, int minutes)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (sender, args) => ApplyAutoRefresh(minutes, true, true);
            autoRefreshItems.Add(minutes, item);
            parent.DropDownItems.Add(item);
        }

        private void ApplyAutoRefresh(int minutes, bool save, bool notify)
        {
            var previousMinutes = autoRefreshMinutes;
            autoRefreshMinutes = minutes;
            if (save)
            {
                try
                {
                    SaveSettings();
                }
                catch (Exception ex)
                {
                    autoRefreshMinutes = previousMinutes;
                    AppLogger.Write("Auto refresh settings could not be saved: " + ex.Message);
                    notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Auto refresh setting could not be saved. See the log.", ToolTipIcon.Error);
                    return;
                }
            }

            foreach (var pair in autoRefreshItems)
            {
                pair.Value.Checked = pair.Key == minutes;
            }

            autoRefreshTimer.Stop();
            nextAutoRefreshAt = null;
            if (minutes > 0)
            {
                ScheduleNextAutoRefresh();
            }
            else
            {
                UpdateTooltip();
            }

            if (notify)
            {
                var message = minutes == 0
                    ? "Auto refresh is Off."
                    : "Auto refresh is set to " + minutes + " minutes.";
                notifyIcon.ShowBalloonTip(3000, AppInfo.Name, message, ToolTipIcon.Info);
            }
        }

        private ToolStripMenuItem CreateColorsMenu()
        {
            var parent = new ToolStripMenuItem("Colors");
            var fiveHour = new ToolStripMenuItem("5-hour colors");
            var weekly = new ToolStripMenuItem("Weekly colors");
            var interfaceColors = new ToolStripMenuItem("Interface colors");

            foreach (var definition in ThemePalette.Definitions)
            {
                var group = definition.Group == ThemeColorGroup.FiveHour
                    ? fiveHour
                    : definition.Group == ThemeColorGroup.Weekly ? weekly : interfaceColors;
                var item = new ToolStripMenuItem(definition.Label)
                {
                    Tag = definition.Key,
                    BackColor = palette.GetColor(definition.Key)
                };
                item.Click += HandleColorMenuClick;
                colorMenuItems.Add(definition.Key, item);
                group.DropDownItems.Add(item);
            }

            var reset = new ToolStripMenuItem("Reset all colors");
            reset.Click += (sender, args) => ResetAllColors();
            parent.DropDownItems.Add(fiveHour);
            parent.DropDownItems.Add(weekly);
            parent.DropDownItems.Add(interfaceColors);
            parent.DropDownItems.Add(new ToolStripSeparator());
            parent.DropDownItems.Add(reset);
            return parent;
        }

        private ToolStripMenuItem CreateGraphStyleMenu()
        {
            var parent = new ToolStripMenuItem("Graph style");
            AddGraphStyleItem(parent, WidgetGraphStyle.Rings);
            AddGraphStyleItem(parent, WidgetGraphStyle.Bars);
            AddGraphStyleItem(parent, WidgetGraphStyle.Meters);
            AddGraphStyleItem(parent, WidgetGraphStyle.Battery);
            return parent;
        }

        private ToolStripMenuItem CreateLogoModeMenu()
        {
            var parent = new ToolStripMenuItem("Center logo");
            AddLogoModeItem(parent, WidgetLogoMode.Static, "Static icon");
            AddLogoModeItem(parent, WidgetLogoMode.Animated, "Animated GIF (256 only)");
            return parent;
        }

        private void AddLogoModeItem(ToolStripMenuItem parent, WidgetLogoMode mode, string text)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (sender, args) => ApplyLogoMode(mode, true, true);
            logoModeItems.Add(mode, item);
            parent.DropDownItems.Add(item);
        }

        private void AddGraphStyleItem(ToolStripMenuItem parent, WidgetGraphStyle style)
        {
            var item = new ToolStripMenuItem(style.ToString());
            item.Click += (sender, args) => ApplyGraphStyle(style, true, true);
            graphStyleItems.Add(style, item);
            parent.DropDownItems.Add(item);
        }

        private void ApplyGraphStyle(WidgetGraphStyle style, bool save, bool notify)
        {
            var previous = currentGraphStyle;
            currentGraphStyle = style;
            RefreshGraphStyleChecks();
            if (save)
            {
                try
                {
                    SaveSettings();
                }
                catch (Exception ex)
                {
                    currentGraphStyle = previous;
                    RefreshGraphStyleChecks();
                    AppLogger.Write("Graph style setting could not be saved: " + ex.Message);
                    notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Graph style could not be saved. See the log.", ToolTipIcon.Error);
                    return;
                }
            }

            UpdateWidget();
            if (notify)
            {
                notifyIcon.ShowBalloonTip(2500, AppInfo.Name, "Widget graph style is " + style + ".", ToolTipIcon.Info);
            }
        }

        private void RefreshGraphStyleChecks()
        {
            foreach (var pair in graphStyleItems)
            {
                pair.Value.Checked = pair.Key == currentGraphStyle;
            }
        }

        private void ApplyLogoMode(WidgetLogoMode mode, bool save, bool notify)
        {
            var previous = currentLogoMode;
            currentLogoMode = mode;
            RefreshLogoModeChecks();
            if (save)
            {
                try
                {
                    SaveSettings();
                }
                catch (Exception ex)
                {
                    currentLogoMode = previous;
                    RefreshLogoModeChecks();
                    AppLogger.Write("Widget logo mode setting could not be saved: " + ex.Message);
                    notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Logo mode could not be saved. See the log.", ToolTipIcon.Error);
                    return;
                }
            }

            UpdateWidget();
            if (notify)
            {
                var message = mode == WidgetLogoMode.Animated
                    ? "Widget center logo uses the animated GIF on 256x256 widgets."
                    : "Widget center logo uses the static icon.";
                notifyIcon.ShowBalloonTip(2500, AppInfo.Name, message, ToolTipIcon.Info);
            }
        }

        private void RefreshLogoModeChecks()
        {
            foreach (var pair in logoModeItems)
            {
                pair.Value.Checked = pair.Key == currentLogoMode;
            }
        }

        private void HandleColorMenuClick(object sender, EventArgs args)
        {
            var item = sender as ToolStripMenuItem;
            var key = item == null ? null : item.Tag as string;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            using (var dialog = new ColorDialog
            {
                FullOpen = true,
                Color = palette.GetColor(key)
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                var previous = palette.GetColor(key);
                palette.SetColor(key, dialog.Color);
                try
                {
                    SaveColorSettings();
                }
                catch (Exception ex)
                {
                    palette.SetColor(key, previous);
                    AppLogger.Write("Theme color could not be saved: " + ex.Message);
                    notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Color setting could not be saved. See the log.", ToolTipIcon.Error);
                    return;
                }
            }

            RefreshColorMenuSwatches();
            UpdateUsageIcon();
            UpdateWidget();
            AppLogger.Write("Theme color changed: " + key + ".");
        }

        private void ResetAllColors()
        {
            var previous = palette.ToDictionary();
            palette.Reset();
            try
            {
                SaveColorSettings();
            }
            catch (Exception ex)
            {
                foreach (var pair in previous)
                {
                    palette.SetColor(pair.Key, ColorTranslator.FromHtml(pair.Value));
                }
                AppLogger.Write("Default theme colors could not be restored: " + ex.Message);
                notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Default colors could not be restored. See the log.", ToolTipIcon.Error);
                return;
            }

            RefreshColorMenuSwatches();
            UpdateUsageIcon();
            UpdateWidget();
            notifyIcon.ShowBalloonTip(3000, AppInfo.Name, "All colors were reset to their defaults.", ToolTipIcon.Info);
            AppLogger.Write("All theme colors reset to defaults.");
        }

        private void SaveColorSettings()
        {
            settings.colors = palette.ToDictionary();
            SaveSettings();
        }

        private void SaveSettings()
        {
            settings.autoRefreshMinutes = autoRefreshMinutes;
            settings.colors = palette.ToDictionary();
            settings.graphStyle = WidgetGraphStyleHelper.ToSettingValue(currentGraphStyle);
            settings.logoMode = WidgetLogoModeHelper.ToSettingValue(currentLogoMode);
            AppSettingsStore.Save(settings);
        }

        private void RefreshColorMenuSwatches()
        {
            foreach (var pair in colorMenuItems)
            {
                pair.Value.BackColor = palette.GetColor(pair.Key);
            }
        }

        private void ScheduleNextAutoRefresh()
        {
            if (autoRefreshMinutes <= 0 || exiting)
            {
                return;
            }
            autoRefreshTimer.Stop();
            autoRefreshTimer.Interval = checked(autoRefreshMinutes * 60 * 1000);
            nextAutoRefreshAt = DateTime.Now.AddMinutes(autoRefreshMinutes);
            autoRefreshTimer.Start();
            UpdateTooltip();
        }

        private async void HandleAutoRefreshTick(object sender, EventArgs args)
        {
            autoRefreshTimer.Stop();
            nextAutoRefreshAt = null;
            if (operationInProgress || browserForm.IsBusy)
            {
                AppLogger.Write("Automatic refresh skipped because another operation is in progress.");
                ScheduleNextAutoRefresh();
                return;
            }

            await OpenBrowserAsync(true, true);
        }

        private void HandleUsageUpdated(object sender, UsageSnapshot snapshot)
        {
            lastSnapshot = snapshot;
            currentStatus = "OK";
            var zeroAlertShown = UpdateZeroAlertState(snapshot, true);
            UpdateTooltip();
            UpdateWidget();
            if (!zeroAlertShown)
            {
                notifyIcon.ShowBalloonTip(
                    4000,
                    "Codex usage updated",
                    "5-hour " + snapshot.fiveHourRemaining +
                    "% / Weekly " + snapshot.weeklyRemaining + "% remaining",
                    ToolTipIcon.Info);
            }
        }

        private void HandleStatusChanged(object sender, AppStatusEventArgs status)
        {
            currentStatus = CompactStatus(status);
            UpdateTooltip();
            UpdateWidget();
            if (!status.Notify)
            {
                return;
            }

            var icon = status.Kind == AppStatusKind.Error
                ? ToolTipIcon.Error
                : status.Kind == AppStatusKind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
            notifyIcon.ShowBalloonTip(5000, "Codex Usage Monitor V2", status.Message, icon);
        }

        private static string TruncateTooltip(string value)
        {
            return value.Length <= 63 ? value : value.Substring(0, 60) + "...";
        }

        private void ReloadSavedData(bool notify)
        {
            UsageSnapshot snapshot;
            string error;
            if (!UsageParser.TryLoad(out snapshot, out error))
            {
                lastSnapshot = null;
                currentStatus = "nodata";
                UpdateZeroAlertState(null, false);
                UpdateTooltip();
                UpdateWidget();
                if (notify)
                {
                    notifyIcon.ShowBalloonTip(4000, "Saved usage data", error, ToolTipIcon.Warning);
                }
                return;
            }

            lastSnapshot = snapshot;
            currentStatus = string.Equals(snapshot.status, "ok", StringComparison.OrdinalIgnoreCase) ? "saved" : "data";
            UpdateZeroAlertState(snapshot, notify);
            UpdateTooltip();
            UpdateWidget();
            AppLogger.Write("Saved usage data reloaded without opening WebView2.");
            if (notify)
            {
                notifyIcon.ShowBalloonTip(3000, "Saved usage data", "The tray display was reloaded from the local JSON file.", ToolTipIcon.Info);
            }
        }

        private void ClearWebView2Cache()
        {
            if (operationInProgress || browserForm.IsBusy)
            {
                notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Cache cleanup is unavailable while usage is loading.", ToolTipIcon.Warning);
                return;
            }
            var cleanup = ProfileCacheCleaner.Clean();
            currentStatus = "cache";
            UpdateTooltip();
            UpdateWidget();
            notifyIcon.ShowBalloonTip(
                4000,
                "WebView2 cache cleanup",
                "Removed " + cleanup.RemovedFiles + " cache files (" + FormatBytes(cleanup.RemovedBytes) + "). Login data was preserved.",
                ToolTipIcon.Info);
        }

        private bool UpdateZeroAlertState(UsageSnapshot snapshot, bool notify)
        {
            currentFiveHourAlertKey = null;
            currentWeeklyAlertKey = null;
            if (snapshot == null)
            {
                UpdateAcknowledgeAlertMenu();
                return false;
            }

            var fiveHourReached = IsGeneralLimitReached(snapshot.fiveHourRemaining, snapshot.fiveHourPercent);
            var weeklyReached = IsGeneralLimitReached(snapshot.weeklyRemaining, snapshot.weeklyPercent);
            if (fiveHourReached)
            {
                currentFiveHourAlertKey = BuildAlertKey("5H", snapshot.fiveHourResetText);
            }
            if (weeklyReached)
            {
                currentWeeklyAlertKey = BuildAlertKey("W", snapshot.weeklyResetText);
            }

            UpdateAcknowledgeAlertMenu();
            if (!notify)
            {
                return false;
            }

            var lines = new List<string>();
            if (ShouldNotifyFiveHourAlert())
            {
                lines.Add("5H usage reached 100%. Reset remaining: " + FormatFiveHourRemaining(snapshot.fiveHourResetText));
                lastNotifiedFiveHourAlertKey = currentFiveHourAlertKey;
            }
            if (ShouldNotifyWeeklyAlert())
            {
                lines.Add("Weekly usage reached 100%. Reset remaining: " + FormatWeeklyRemaining(snapshot.weeklyResetText));
                lastNotifiedWeeklyAlertKey = currentWeeklyAlertKey;
            }

            if (lines.Count == 0)
            {
                return false;
            }

            notifyIcon.ShowBalloonTip(
                8000,
                "Codex usage limit reached",
                string.Join(Environment.NewLine, lines.ToArray()),
                ToolTipIcon.Warning);
            AppLogger.Write("Zero alert notification shown for " + string.Join(", ", new List<string>(GetCurrentAlertLabels()).ToArray()) + ".");
            return true;
        }

        private void AcknowledgeCurrentAlert()
        {
            if (!HasPendingAcknowledgeAlert())
            {
                notifyIcon.ShowBalloonTip(2500, AppInfo.Name, "There is no current usage alert to acknowledge.", ToolTipIcon.Info);
                return;
            }

            var labels = new List<string>();
            if (IsActiveUnacknowledgedFiveHourAlert())
            {
                settings.acknowledgedFiveHourAlertKey = currentFiveHourAlertKey;
                labels.Add("5H");
            }
            if (IsActiveUnacknowledgedWeeklyAlert())
            {
                settings.acknowledgedWeeklyAlertKey = currentWeeklyAlertKey;
                labels.Add("Weekly");
            }

            try
            {
                SaveSettings();
                UpdateAcknowledgeAlertMenu();
                notifyIcon.ShowBalloonTip(
                    3000,
                    AppInfo.Name,
                    "Acknowledged current alert: " + string.Join(", ", labels.ToArray()) + ".",
                    ToolTipIcon.Info);
                AppLogger.Write("Zero alert acknowledged: " + string.Join(", ", labels.ToArray()) + ".");
            }
            catch (Exception ex)
            {
                AppLogger.Write("Zero alert acknowledgement could not be saved: " + ex.Message);
                notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Alert acknowledgement could not be saved. See the log.", ToolTipIcon.Error);
            }
        }

        private void UpdateAcknowledgeAlertMenu()
        {
            if (acknowledgeAlertMenuItem == null)
            {
                return;
            }
            acknowledgeAlertMenuItem.Enabled = HasPendingAcknowledgeAlert();
        }

        private bool HasPendingAcknowledgeAlert()
        {
            return IsActiveUnacknowledgedFiveHourAlert() || IsActiveUnacknowledgedWeeklyAlert();
        }

        private bool IsActiveUnacknowledgedFiveHourAlert()
        {
            return !string.IsNullOrEmpty(currentFiveHourAlertKey) &&
                !string.Equals(settings.acknowledgedFiveHourAlertKey, currentFiveHourAlertKey, StringComparison.Ordinal);
        }

        private bool IsActiveUnacknowledgedWeeklyAlert()
        {
            return !string.IsNullOrEmpty(currentWeeklyAlertKey) &&
                !string.Equals(settings.acknowledgedWeeklyAlertKey, currentWeeklyAlertKey, StringComparison.Ordinal);
        }

        private bool ShouldNotifyFiveHourAlert()
        {
            return IsActiveUnacknowledgedFiveHourAlert() &&
                !string.Equals(lastNotifiedFiveHourAlertKey, currentFiveHourAlertKey, StringComparison.Ordinal);
        }

        private bool ShouldNotifyWeeklyAlert()
        {
            return IsActiveUnacknowledgedWeeklyAlert() &&
                !string.Equals(lastNotifiedWeeklyAlertKey, currentWeeklyAlertKey, StringComparison.Ordinal);
        }

        private IEnumerable<string> GetCurrentAlertLabels()
        {
            if (!string.IsNullOrEmpty(currentFiveHourAlertKey))
            {
                yield return "5H";
            }
            if (!string.IsNullOrEmpty(currentWeeklyAlertKey))
            {
                yield return "Weekly";
            }
        }

        private static bool IsGeneralLimitReached(int value, int? percent)
        {
            return value <= 0 || (percent.HasValue && percent.Value <= 0);
        }

        private static string BuildAlertKey(string prefix, string resetText)
        {
            var resetKey = NormalizeAlertKeyPart(resetText);
            if (string.IsNullOrWhiteSpace(resetKey))
            {
                resetKey = "no-reset";
            }
            return prefix + ":" + resetKey;
        }

        private static string NormalizeAlertKeyPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
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

            var text = System.Text.RegularExpressions.Regex.Replace(resetText, @"\s+", " ").Trim();
            text = text.Replace("초기화", string.Empty).Trim();
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:(\d{4})\.\s*(\d{1,2})\.\s*(\d{1,2})\.\s*)?(오전|오후|AM|PM)?\s*(\d{1,2}):(\d{2})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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

        private void UpdateTooltip()
        {
            UpdateUsageIcon();
            var autoText = autoRefreshMinutes <= 0
                ? "Aoff"
                : "A" + autoRefreshMinutes + ">N" +
                  (nextAutoRefreshAt.HasValue ? nextAutoRefreshAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "--:--");
            if (lastSnapshot == null)
            {
                notifyIcon.Text = TruncateTooltip(
                    "Codex V2|5h-- W--|Unever|" + currentStatus + "|" + autoText);
                return;
            }

            var resetText = BuildResetTooltipText(lastSnapshot);
            var creditsText = BuildCreditsTooltipText(lastSnapshot);
            notifyIcon.Text = TruncateTooltip(
                "Codex V2|5h" + lastSnapshot.fiveHourRemaining + " W" + lastSnapshot.weeklyRemaining +
                resetText + creditsText + "|" + autoText);
        }

        private static string BuildResetTooltipText(UsageSnapshot snapshot)
        {
            var fiveHourReset = UsageParser.CompactResetText(snapshot.fiveHourResetText);
            var weeklyReset = UsageParser.CompactResetText(snapshot.weeklyResetText);
            if (string.IsNullOrEmpty(fiveHourReset) && string.IsNullOrEmpty(weeklyReset))
            {
                return string.Empty;
            }
            return "|R" +
                (string.IsNullOrEmpty(fiveHourReset) ? "--" : fiveHourReset) +
                "/" +
                (string.IsNullOrEmpty(weeklyReset) ? "--" : weeklyReset);
        }

        private static string BuildCreditsTooltipText(UsageSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.creditsRemaining))
            {
                return string.Empty;
            }
            return "|C" + snapshot.creditsRemaining.Trim();
        }

        private void UpdateUsageIcon()
        {
            if (notifyIcon == null || exiting)
            {
                return;
            }

            if (lastSnapshot == null)
            {
                notifyIcon.Icon = appIcon;
                if (usageIcon != null)
                {
                    usageIcon.Dispose();
                    usageIcon = null;
                }
                return;
            }

            var nextIcon = UsageIconRenderer.Create(
                lastSnapshot.fiveHourRemaining,
                lastSnapshot.weeklyRemaining,
                palette);
            var previousIcon = usageIcon;
            usageIcon = nextIcon;
            notifyIcon.Icon = usageIcon;
            if (previousIcon != null)
            {
                previousIcon.Dispose();
            }
        }

        private void ShowWidget(bool notify)
        {
            EnsureWidgetForm();
            widgetForm.ApplyTheme();
            widgetForm.ApplySize(settings.widgetSize ?? 128);
            widgetForm.ApplyGraphStyle(currentGraphStyle);
            widgetForm.ApplyLogoMode(currentLogoMode);
            widgetForm.ApplyLocationOrDefault(settings.widgetX, settings.widgetY);
            widgetForm.SetSnapshot(lastSnapshot);
            widgetForm.Show();
            widgetForm.Activate();
            settings.widgetVisible = true;
            CaptureWidgetSettings();
            SaveSettings();
            if (notify)
            {
                notifyIcon.ShowBalloonTip(2500, AppInfo.Name, "Widget is shown. The tray icon remains available.", ToolTipIcon.Info);
            }
            AppLogger.Write("Widget shown.");
        }

        private void EnsureWidgetForm()
        {
            if (widgetForm != null && !widgetForm.IsDisposed)
            {
                return;
            }

            widgetForm = new WidgetForm(palette, menu);
            widgetForm.WidgetClosedByUser += (sender, args) =>
            {
                settings.widgetVisible = false;
                CaptureWidgetSettings();
                SaveSettings();
                AppLogger.Write("Widget hidden by user.");
            };
            widgetForm.WidgetMovedOrSized += (sender, args) =>
            {
                CaptureWidgetSettings();
                SaveSettings();
                AppLogger.Write("Widget position or size saved.");
            };
        }

        private void UpdateWidget()
        {
            if (widgetForm == null || widgetForm.IsDisposed)
            {
                return;
            }
            widgetForm.ApplyTheme();
            widgetForm.ApplyGraphStyle(currentGraphStyle);
            widgetForm.ApplyLogoMode(currentLogoMode);
            widgetForm.SetSnapshot(lastSnapshot);
        }

        private void CaptureWidgetSettings()
        {
            if (widgetForm == null || widgetForm.IsDisposed)
            {
                return;
            }
            settings.widgetSize = widgetForm.LogicalWidgetSize;
            settings.widgetX = widgetForm.Location.X;
            settings.widgetY = widgetForm.Location.Y;
        }

        private static void ShowAbout()
        {
            using (var form = new AboutForm())
            {
                form.ShowDialog();
            }
        }

        private static string CompactStatus(AppStatusEventArgs status)
        {
            if (status.Message.IndexOf("Login required", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "login";
            }
            if (status.Message.IndexOf("Network error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "net";
            }
            if (status.Message.IndexOf("Parse failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "parse";
            }
            if (status.Message.IndexOf("WebView2 Runtime", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "rt";
            }
            if (status.Kind == AppStatusKind.Error)
            {
                return "error";
            }
            if (status.Kind == AppStatusKind.Warning)
            {
                return "warn";
            }
            if (status.Kind == AppStatusKind.Success)
            {
                return "OK";
            }
            return "load";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
            }
            return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
        }

        private void ExitApplication()
        {
            ExitThread();
        }

        private static void OpenFile(string path, string missingMessage)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(missingMessage, "Codex Usage Monitor V2", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start("notepad.exe", "\"" + path + "\"");
        }
    }
}
