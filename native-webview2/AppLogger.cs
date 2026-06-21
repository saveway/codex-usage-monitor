using System;
using System.IO;
using System.Text;

namespace CodexUsageMonitorV2
{
    internal static class AppLogger
    {
        private const long MaxLogBytes = 2L * 1024 * 1024;
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            lock (Sync)
            {
                try
                {
                    RotateIfNeeded();
                    File.AppendAllText(
                        AppPaths.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }

        private static void RotateIfNeeded()
        {
            var file = new FileInfo(AppPaths.LogPath);
            if (!file.Exists || file.Length <= MaxLogBytes)
            {
                return;
            }

            var backupPath = AppPaths.LogPath + ".1";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            using (var source = new FileStream(AppPaths.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                source.Seek(-MaxLogBytes, SeekOrigin.End);
                using (var destination = new FileStream(backupPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    source.CopyTo(destination);
                }
            }
            File.Delete(AppPaths.LogPath);
        }
    }
}
