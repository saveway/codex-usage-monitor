using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CodexUsageMonitorV2
{
    internal static class AppIcon
    {
        private const string ResourceName = "CodexUsageMonitorV2.app.ico";

        public static Icon Create()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("The application icon resource is missing.");
                }
                using (var icon = new Icon(stream))
                {
                    return (Icon)icon.Clone();
                }
            }
        }
    }
}
