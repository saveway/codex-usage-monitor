using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CodexUsageMonitorV2
{
    internal static class ProfileCacheCleaner
    {
        private static readonly string[] CacheDirectories =
        {
            @"EBWebView\Default\Cache",
            @"EBWebView\Default\Code Cache",
            @"EBWebView\Default\GPUCache",
            @"EBWebView\Default\DawnGraphiteCache",
            @"EBWebView\Default\DawnWebGPUCache",
            @"EBWebView\ShaderCache",
            @"EBWebView\GrShaderCache",
            @"EBWebView\GPUPersistentCache",
            @"EBWebView\Default\Service Worker\CacheStorage",
            @"EBWebView\Crashpad\reports",
            @"EBWebView\component_crx_cache"
        };

        public static CacheCleanupResult Clean()
        {
            var result = new CacheCleanupResult();
            foreach (var relativePath in CacheDirectories)
            {
                var fullPath = Path.Combine(AppPaths.WebViewProfileDirectory, relativePath);
                if (!Directory.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    var directoryBytes = 0L;
                    var directoryFiles = 0;
                    foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            directoryBytes += new FileInfo(file).Length;
                            directoryFiles++;
                        }
                        catch
                        {
                        }
                    }

                    DeleteWithRetry(fullPath);
                    result.RemovedBytes += directoryBytes;
                    result.RemovedFiles += directoryFiles;
                    result.RemovedDirectories++;
                }
                catch (Exception ex)
                {
                    AppLogger.Write("Could not remove cache " + relativePath + ": " + ex.Message);
                }
            }

            if (result.RemovedDirectories > 0)
            {
                AppLogger.Write(
                    "Removed " + result.RemovedDirectories + " cache directories, " +
                    result.RemovedFiles + " files, " + result.RemovedBytes + " bytes.");
            }
            return result;
        }

        private static void DeleteWithRetry(string path)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < 4)
                    {
                        Thread.Sleep(250);
                    }
                }
            }
            throw lastError ?? new IOException("Cache directory could not be removed.");
        }
    }

    internal sealed class CacheCleanupResult
    {
        public int RemovedDirectories { get; set; }
        public int RemovedFiles { get; set; }
        public long RemovedBytes { get; set; }
    }
}
