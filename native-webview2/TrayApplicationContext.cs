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
        private UsageSnapshot lastSnapshot;
        private string currentStatus = "nodata";
        private int autoRefreshMinutes;
        private DateTime? nextAutoRefreshAt;
        private bool operationInProgress;
        private bool exiting;
        private bool disposed;

        public TrayApplicationContext()
        {
            AppLogger.Write("Application starting.");
            var cleanup = ProfileCacheCleaner.Clean();
            AppLogger.Write("Startup cache cleanup removed " + cleanup.RemovedBytes + " bytes.");

            appIcon = AppIcon.Create();
            browserForm = new BrowserForm();
            browserForm.UsageUpdated += HandleUsageUpdated;
            browserForm.StatusChanged += HandleStatusChanged;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open/Login usage page", null, async (sender, args) => await OpenBrowserAsync(false, false));
            menu.Items.Add("Fetch now", null, async (sender, args) => await OpenBrowserAsync(true, false));
            menu.Items.Add(CreateAutoRefreshMenu());
            menu.Items.Add(new ToolStripSeparator());
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
            notifyIcon.DoubleClick += async (sender, args) => await OpenBrowserAsync(false, false);

            autoRefreshTimer = new Timer();
            autoRefreshTimer.Tick += HandleAutoRefreshTick;
            ReloadSavedData(false);
            ApplyAutoRefresh(AutoRefreshSettings.LoadMinutes(), false, false);
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
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Write("Tray icon cleanup failed: " + ex.Message);
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
            if (save)
            {
                try
                {
                    AutoRefreshSettings.SaveMinutes(minutes);
                }
                catch (Exception ex)
                {
                    AppLogger.Write("Auto refresh settings could not be saved: " + ex.Message);
                    notifyIcon.ShowBalloonTip(4000, AppInfo.Name, "Auto refresh setting could not be saved. See the log.", ToolTipIcon.Error);
                    return;
                }
            }

            autoRefreshMinutes = minutes;
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
            UpdateTooltip();
            notifyIcon.ShowBalloonTip(
                4000,
                "Codex usage updated",
                "5-hour " + snapshot.fiveHourRemaining +
                "% / Weekly " + snapshot.weeklyRemaining + "% remaining",
                ToolTipIcon.Info);
        }

        private void HandleStatusChanged(object sender, AppStatusEventArgs status)
        {
            currentStatus = CompactStatus(status);
            UpdateTooltip();
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
                UpdateTooltip();
                if (notify)
                {
                    notifyIcon.ShowBalloonTip(4000, "Saved usage data", error, ToolTipIcon.Warning);
                }
                return;
            }

            lastSnapshot = snapshot;
            currentStatus = string.Equals(snapshot.status, "ok", StringComparison.OrdinalIgnoreCase) ? "saved" : "data";
            UpdateTooltip();
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
            notifyIcon.ShowBalloonTip(
                4000,
                "WebView2 cache cleanup",
                "Removed " + cleanup.RemovedFiles + " cache files (" + FormatBytes(cleanup.RemovedBytes) + "). Login data was preserved.",
                ToolTipIcon.Info);
        }

        private void UpdateTooltip()
        {
            var autoText = autoRefreshMinutes <= 0
                ? "Aoff"
                : "A" + autoRefreshMinutes + ">N" +
                  (nextAutoRefreshAt.HasValue ? nextAutoRefreshAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "--:--");
            if (lastSnapshot == null)
            {
                notifyIcon.Text = TruncateTooltip(
                    AppInfo.Name + "|5h-- W--|Unever|" + currentStatus + "|" + autoText);
                return;
            }

            DateTime updated;
            var updatedText = DateTime.TryParseExact(
                lastSnapshot.updatedAt,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out updated)
                ? updated.ToString("MM-dd/HH:mm", CultureInfo.InvariantCulture)
                : "unknown";
            notifyIcon.Text = TruncateTooltip(
                AppInfo.Name + "|5h" + lastSnapshot.fiveHourRemaining + "% W" + lastSnapshot.weeklyRemaining +
                "%|U" + updatedText + "|" + currentStatus + "|" + autoText);
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
