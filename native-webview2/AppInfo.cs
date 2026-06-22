using System.Reflection;

namespace CodexUsageMonitorV2
{
    internal static class AppInfo
    {
        public const string Name = "Codex Usage Monitor V2";
        public static readonly string Version = GetVersion();
        public const string Edition = "WebView2 Native Preview";
        public const string RepositoryUrl = "https://github.com/saveway/codex-usage-monitor";
        public const string RuntimeRequiredMessage =
            "Microsoft Edge WebView2 Runtime is required. " +
            "Install, update, or repair the Evergreen WebView2 Runtime, then restart this app.";

        private static string GetVersion()
        {
            var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(
                typeof(AssemblyInformationalVersionAttribute), false);
            if (attributes.Length == 1)
            {
                return "v" + ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;
            }

            return "v" + Assembly.GetExecutingAssembly().GetName().Version;
        }
    }
}
