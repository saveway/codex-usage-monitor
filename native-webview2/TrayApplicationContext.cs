using System;
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
        private UsageSnapshot lastSnapshot;
        private string currentStatus = "No data";
        private bool exiting;
        private bool disposed;

        public TrayApplicationContext()
        {
            AppLogger.Write("Application starting.");
            var cleanup = ProfileCacheCleaner.Clean();
            AppLogger.Write("Startup cache cleanup removed " + cleanup.RemovedBytes + " bytes.");

            browserForm = new BrowserForm();
            browserForm.UsageUpdated += HandleUsageUpdated;
            browserForm.StatusChanged += HandleStatusChanged;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open/Login usage page", null, async (sender, args) => await OpenBrowserAsync(false));
            menu.Items.Add("Fetch now", null, async (sender, args) => await OpenBrowserAsync(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Reload saved data", null, (sender, args) => ReloadSavedData(true));
            menu.Items.Add("Open data file", null, (sender, args) => OpenFile(AppPaths.DataPath, "No usage data has been saved yet."));
            menu.Items.Add("Open log", null, (sender, args) => OpenFile(AppPaths.LogPath, "The log file does not exist yet."));
            menu.Items.Add("Clear WebView2 cache", null, (sender, args) => ClearWebView2Cache());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (sender, args) => ExitApplication());

            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "5h -- | W -- | Never | No data",
                ContextMenuStrip = menu,
                Visible = true
            };
            notifyIcon.DoubleClick += async (sender, args) => await OpenBrowserAsync(false);
            ReloadSavedData(false);
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

        private async Task OpenBrowserAsync(bool fetchNow)
        {
            if (exiting)
            {
                return;
            }
            await browserForm.OpenAsync(fetchNow);
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
                currentStatus = "No data";
                UpdateTooltip();
                if (notify)
                {
                    notifyIcon.ShowBalloonTip(4000, "Saved usage data", error, ToolTipIcon.Warning);
                }
                return;
            }

            lastSnapshot = snapshot;
            currentStatus = string.Equals(snapshot.status, "ok", StringComparison.OrdinalIgnoreCase) ? "Saved" : "Data loaded";
            UpdateTooltip();
            AppLogger.Write("Saved usage data reloaded without opening WebView2.");
            if (notify)
            {
                notifyIcon.ShowBalloonTip(3000, "Saved usage data", "The tray display was reloaded from the local JSON file.", ToolTipIcon.Info);
            }
        }

        private void ClearWebView2Cache()
        {
            var cleanup = ProfileCacheCleaner.Clean();
            currentStatus = "Cache cleared";
            UpdateTooltip();
            notifyIcon.ShowBalloonTip(
                4000,
                "WebView2 cache cleanup",
                "Removed " + cleanup.RemovedFiles + " cache files (" + FormatBytes(cleanup.RemovedBytes) + "). Login data was preserved.",
                ToolTipIcon.Info);
        }

        private void UpdateTooltip()
        {
            if (lastSnapshot == null)
            {
                notifyIcon.Text = TruncateTooltip("5h -- | W -- | Never | " + currentStatus);
                return;
            }

            DateTime updated;
            var updatedText = DateTime.TryParseExact(
                lastSnapshot.updatedAt,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out updated)
                ? updated.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "Unknown time";
            notifyIcon.Text = TruncateTooltip(
                "5h " + lastSnapshot.fiveHourRemaining + "% | W " + lastSnapshot.weeklyRemaining +
                "% | " + updatedText + " | " + currentStatus);
        }

        private static string CompactStatus(AppStatusEventArgs status)
        {
            if (status.Message.IndexOf("Login required", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Login required";
            }
            if (status.Kind == AppStatusKind.Error)
            {
                return "Error";
            }
            if (status.Kind == AppStatusKind.Warning)
            {
                return "Warning";
            }
            if (status.Kind == AppStatusKind.Success)
            {
                return "OK";
            }
            return "Loading";
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
