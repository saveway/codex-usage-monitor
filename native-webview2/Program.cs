using System;
using System.Windows.Forms;

namespace CodexUsageMonitorV2
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppPaths.EnsureDirectories();

            try
            {
                using (var context = new TrayApplicationContext())
                {
                    Application.Run(context);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Write("Fatal application error: " + ex);
                MessageBox.Show(
                    "Codex Usage Monitor V2 could not start.\n\n" + ex.Message +
                    "\n\nLog: " + AppPaths.LogPath,
                    "Codex Usage Monitor V2",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
