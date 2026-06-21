using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CodexUsageMonitorV2
{
    internal sealed class BrowserForm : Form
    {
        private const string UsageUrl = "https://chatgpt.com/codex/cloud/settings/analytics#usage";
        private readonly WebView2 webView;
        private readonly ToolStripStatusLabel statusLabel;
        private bool allowClose;
        private bool initialized;
        private bool fetchAfterNavigation;
        private bool fetchInProgress;

        public event EventHandler<UsageSnapshot> UsageUpdated;
        public event EventHandler<AppStatusEventArgs> StatusChanged;

        public BrowserForm()
        {
            Text = "Codex Usage Monitor V2 - Open/Login";
            Width = 1120;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;

            var statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Ready. Open the usage page or sign in.");
            statusStrip.Items.Add(statusLabel);

            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);
            Controls.Add(statusStrip);
            FormClosing += HandleFormClosing;
        }

        public async Task OpenAsync(bool fetchNow)
        {
            fetchAfterNavigation = fetchNow;
            SetStatus(
                AppStatusKind.Information,
                fetchNow ? "Opening the usage page and reading usage..." : "Opening the ChatGPT sign-in or usage page...",
                false);
            Show();
            WindowState = FormWindowState.Normal;
            Activate();

            if (!await EnsureWebViewAsync())
            {
                return;
            }

            AppLogger.Write((fetchNow ? "Fetch now" : "Open/Login") + " requested.");
            webView.CoreWebView2.Navigate(UsageUrl);
        }

        public void CloseForExit()
        {
            allowClose = true;
            Close();
        }

        private async Task<bool> EnsureWebViewAsync()
        {
            if (initialized)
            {
                return true;
            }

            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrWhiteSpace(version))
                {
                    throw new WebView2RuntimeNotFoundException("Microsoft Edge WebView2 Runtime was not found.");
                }

                ProfileCacheCleaner.Clean();
                var options = new CoreWebView2EnvironmentOptions(
                    "--disk-cache-size=16777216 --media-cache-size=8388608");
                var environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    AppPaths.WebViewProfileDirectory,
                    options);
                await webView.EnsureCoreWebView2Async(environment);
                webView.CoreWebView2.NavigationCompleted += HandleNavigationCompleted;
                initialized = true;
                AppLogger.Write("WebView2 initialized. Runtime version: " + version);
                return true;
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                AppLogger.Write("WebView2 Runtime missing: " + ex.Message);
                SetStatus(AppStatusKind.Error, "WebView2 Runtime is not installed.", true);
                var result = MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime is required.\n\n" +
                    "Install the Evergreen WebView2 Runtime, then restart this app.\n\n" +
                    "Open the Microsoft download page now?",
                    "WebView2 Runtime required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                {
                    Process.Start("https://developer.microsoft.com/microsoft-edge/webview2/");
                }
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Write("WebView2 initialization failed: " + ex);
                SetStatus(AppStatusKind.Error, "WebView2 could not start. See the log for details.", true);
                MessageBox.Show(
                    "WebView2 could not start.\n\n" + ex.Message + "\n\nLog: " + AppPaths.LogPath,
                    "Codex Usage Monitor V2",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        private async void HandleNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            AppLogger.Write(
                "Navigation completed. Success=" + eventArgs.IsSuccess +
                " Destination=" + SafeDestination(webView.Source));
            if (!eventArgs.IsSuccess)
            {
                fetchAfterNavigation = false;
                var networkFailure = IsNetworkFailure(eventArgs.WebErrorStatus);
                var message = networkFailure
                    ? "Network error while opening ChatGPT. Check the connection and try again."
                    : "The ChatGPT usage page could not be opened.";
                SafeDebugSnapshot.Save(networkFailure ? "network_failure" : "page_access_failure", webView.Source, string.Empty);
                SetStatus(networkFailure ? AppStatusKind.Error : AppStatusKind.Warning, message, true);
                return;
            }

            if (fetchAfterNavigation && fetchInProgress)
            {
                return;
            }

            try
            {
                var json = await webView.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
                var serializer = new JavaScriptSerializer();
                var pageText = serializer.Deserialize<string>(json) ?? string.Empty;
                if (fetchAfterNavigation && !fetchInProgress)
                {
                    fetchInProgress = true;
                    SetStatus(AppStatusKind.Information, "Waiting for the usage page content to finish loading...", false);
                    pageText = await ReadPageTextWhenReadyAsync();
                }

                if (LooksLikeLoginRequired(webView.Source, pageText))
                {
                    SafeDebugSnapshot.Save("login_required", webView.Source, pageText);
                    SetStatus(AppStatusKind.Warning, "Login required. Sign in on this ChatGPT page, then choose Fetch now.", fetchAfterNavigation);
                    fetchAfterNavigation = false;
                    fetchInProgress = false;
                    return;
                }

                if (!fetchAfterNavigation)
                {
                    SetStatus(
                        AppStatusKind.Information,
                        IsUsagePage(webView.Source) ? "Usage page loaded. Choose Fetch now to read usage." : "Page loaded. Complete sign-in if prompted.",
                        false);
                    return;
                }

                var snapshot = UsageParser.Parse(pageText, webView.Source.ToString());
                UsageParser.Save(snapshot);
                SafeDebugSnapshot.Delete();
                fetchAfterNavigation = false;
                fetchInProgress = false;
                AppLogger.Write("Usage saved. Both required percentage fields were detected.");
                SetStatus(AppStatusKind.Success, "Usage updated: 5-hour and weekly limits were detected.", true);
                UsageUpdated?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                fetchAfterNavigation = false;
                fetchInProgress = false;
                SafeDebugSnapshot.Save("parse_failure", webView.Source, await ReadPageTextSafely());
                AppLogger.Write("Usage read/parse failed: " + ex.Message);
                SetStatus(AppStatusKind.Warning, "Usage text was found, but the required percentages could not be parsed.", true);
            }
        }

        private async Task<string> ReadPageTextSafely()
        {
            try
            {
                var json = await webView.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
                return new JavaScriptSerializer().Deserialize<string>(json) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<string> ReadPageTextWhenReadyAsync()
        {
            var latest = string.Empty;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                latest = await ReadPageTextSafely();
                if (LooksLikeLoginRequired(webView.Source, latest) || ContainsUsageLabels(latest))
                {
                    return latest;
                }
                await Task.Delay(1000);
            }
            return latest;
        }

        private void SetStatus(AppStatusKind kind, string message, bool notify)
        {
            statusLabel.Text = message;
            StatusChanged?.Invoke(this, new AppStatusEventArgs(kind, message, notify));
        }

        private static bool LooksLikeLoginRequired(Uri source, string pageText)
        {
            var path = source == null ? string.Empty : source.AbsolutePath;
            if (path.IndexOf("/auth/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var hasLoginPrompt = pageText.IndexOf("Log in", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pageText.IndexOf("Sign in", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pageText.IndexOf("로그인", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasLoginPrompt && !ContainsUsageLabels(pageText);
        }

        private static bool ContainsUsageLabels(string pageText)
        {
            return (pageText.IndexOf("5시간", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pageText.IndexOf("5 hour", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pageText.IndexOf("5-hour", StringComparison.OrdinalIgnoreCase) >= 0) &&
                   (pageText.IndexOf("주간", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pageText.IndexOf("weekly", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsUsagePage(Uri source)
        {
            return source != null &&
                source.AbsolutePath.IndexOf("/codex/cloud/settings/analytics", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SafeDestination(Uri source)
        {
            if (source == null)
            {
                return "unknown";
            }

            if (source.Host.EndsWith("chatgpt.com", StringComparison.OrdinalIgnoreCase))
            {
                return IsUsagePage(source) ? "chatgpt.com/usage" : "chatgpt.com/other";
            }
            if (source.Host.EndsWith("openai.com", StringComparison.OrdinalIgnoreCase) ||
                source.Host.IndexOf("accounts.google.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "authentication-provider";
            }
            return "external-page";
        }

        private static bool IsNetworkFailure(CoreWebView2WebErrorStatus status)
        {
            return status == CoreWebView2WebErrorStatus.ConnectionAborted ||
                   status == CoreWebView2WebErrorStatus.ConnectionReset ||
                   status == CoreWebView2WebErrorStatus.Disconnected ||
                   status == CoreWebView2WebErrorStatus.HostNameNotResolved ||
                   status == CoreWebView2WebErrorStatus.ServerUnreachable ||
                   status == CoreWebView2WebErrorStatus.Timeout;
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (allowClose)
            {
                return;
            }
            eventArgs.Cancel = true;
            Hide();
        }
    }
}
