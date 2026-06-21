using System;
using System.Collections.Generic;
using System.IO;

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
                    foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            result.RemovedBytes += new FileInfo(file).Length;
                            result.RemovedFiles++;
                        }
                        catch
                        {
                        }
                    }
                    Directory.Delete(fullPath, true);
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
    }

    internal sealed class CacheCleanupResult
    {
        public int RemovedDirectories { get; set; }
        public int RemovedFiles { get; set; }
        public long RemovedBytes { get; set; }
    }
}
