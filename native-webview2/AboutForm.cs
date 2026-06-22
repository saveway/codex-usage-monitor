using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageMonitorV2
{
    internal sealed class AboutForm : Form
    {
        private readonly Icon windowIcon;

        public AboutForm()
        {
            Text = "About " + AppInfo.Name;
            windowIcon = AppIcon.Create();
            Icon = windowIcon;
            ClientSize = new Size(520, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15f, FontStyle.Bold),
                Location = new Point(24, 22),
                Text = AppInfo.Name
            };
            var details = new Label
            {
                AutoSize = false,
                Location = new Point(26, 62),
                Size = new Size(468, 190),
                Text =
                    "Version: " + AppInfo.Version + Environment.NewLine +
                    AppInfo.Edition + Environment.NewLine + Environment.NewLine +
                    "Unofficial personal tool; not an OpenAI application." + Environment.NewLine +
                    "Python, Playwright, and Chromium are not bundled." + Environment.NewLine +
                    "Microsoft Edge WebView2 Runtime is required." + Environment.NewLine + Environment.NewLine +
                    "Local data: %LOCALAPPDATA%\\CodexUsageMonitorV2" + Environment.NewLine +
                    "Resolved path: " + AppPaths.RuntimeDirectory
            };
            var repository = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(26, 258),
                Text = AppInfo.RepositoryUrl
            };
            repository.LinkClicked += (sender, args) => Process.Start(AppInfo.RepositoryUrl);

            var close = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(414, 286),
                Size = new Size(80, 28),
                Text = "Close"
            };
            AcceptButton = close;
            CancelButton = close;
            Controls.Add(title);
            Controls.Add(details);
            Controls.Add(repository);
            Controls.Add(close);
            FormClosed += (sender, args) => windowIcon.Dispose();
        }
    }
}
