using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexUsageMonitorV2
{
    internal sealed class TrayApplicationContext : ApplicationContext, IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly BrowserForm browserForm;
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
            menu.Items.Add("Open/Login", null, async (sender, args) => await OpenBrowserAsync(false));
            menu.Items.Add("Fetch now", null, async (sender, args) => await OpenBrowserAsync(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open data file", null, (sender, args) => OpenFile(AppPaths.DataPath, "No usage data has been saved yet."));
            menu.Items.Add("Open log", null, (sender, args) => OpenFile(AppPaths.LogPath, "The log file does not exist yet."));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (sender, args) => ExitApplication());

            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Codex Usage Monitor V2",
                ContextMenuStrip = menu,
                Visible = true
            };
            notifyIcon.DoubleClick += async (sender, args) => await OpenBrowserAsync(false);
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
            notifyIcon.Text = (
                "Codex V2: 5h " + snapshot.fiveHourRemaining +
                "% / Weekly " + snapshot.weeklyRemaining + "%");
            notifyIcon.ShowBalloonTip(
                4000,
                "Codex usage updated",
                "5-hour " + snapshot.fiveHourRemaining +
                "% / Weekly " + snapshot.weeklyRemaining + "% remaining",
                ToolTipIcon.Info);
        }

        private void HandleStatusChanged(object sender, AppStatusEventArgs status)
        {
            notifyIcon.Text = TruncateTooltip("Codex V2: " + status.Message);
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
