using System;
using System.Runtime.InteropServices;

namespace CodexUsageMonitorV2
{
    internal static class VirtualDesktopPinning
    {
        private static readonly Guid ImmersiveShellClsid =
            new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239A");
        private static readonly Guid ApplicationViewCollectionIid =
            new Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5");
        private static readonly Guid VirtualDesktopPinnedAppsClsid =
            new Guid("B5A399E7-1C87-46B8-88E9-FC5747B171BD");
        private static readonly Guid VirtualDesktopPinnedAppsIid =
            new Guid("4CE81583-1E4C-4632-A621-07A53543148F");

        public static bool TryPinWindowToAllDesktops(IntPtr hwnd, out string message)
        {
            message = null;
            if (hwnd == IntPtr.Zero)
            {
                message = "Widget handle is not ready.";
                return false;
            }

            object shellObject = null;
            object collectionObject = null;
            object pinnedAppsObject = null;
            object viewObject = null;

            try
            {
                var shellType = Type.GetTypeFromCLSID(ImmersiveShellClsid);
                if (shellType == null)
                {
                    message = "ImmersiveShell COM type was not found.";
                    return false;
                }

                shellObject = Activator.CreateInstance(shellType);
                var serviceProvider = (IServiceProvider)shellObject;

                var collectionService = ApplicationViewCollectionIid;
                var collectionIid = ApplicationViewCollectionIid;
                var collectionHr = serviceProvider.QueryService(
                    ref collectionService,
                    ref collectionIid,
                    out collectionObject);
                if (collectionHr < 0 || collectionObject == null)
                {
                    message = "IApplicationViewCollection is not available. HRESULT=0x" +
                        collectionHr.ToString("X8");
                    return false;
                }

                var collection = (IApplicationViewCollection)collectionObject;
                var viewHr = collection.GetViewForHwnd(hwnd, out viewObject);
                if (viewHr < 0 || viewObject == null)
                {
                    message = "Could not get the widget application view. HRESULT=0x" +
                        viewHr.ToString("X8");
                    return false;
                }

                var pinnedService = VirtualDesktopPinnedAppsClsid;
                var pinnedIid = VirtualDesktopPinnedAppsIid;
                var pinnedHr = serviceProvider.QueryService(
                    ref pinnedService,
                    ref pinnedIid,
                    out pinnedAppsObject);
                if (pinnedHr < 0 || pinnedAppsObject == null)
                {
                    message = "IVirtualDesktopPinnedApps is not available. HRESULT=0x" +
                        pinnedHr.ToString("X8");
                    return false;
                }

                var pinnedApps = (IVirtualDesktopPinnedApps)pinnedAppsObject;
                bool isPinned;
                var isPinnedHr = pinnedApps.IsViewPinned(viewObject, out isPinned);
                if (isPinnedHr >= 0 && isPinned)
                {
                    message = "Widget is already pinned to all virtual desktops.";
                    return true;
                }

                var pinHr = pinnedApps.PinView(viewObject);
                if (pinHr < 0)
                {
                    message = "PinView failed. HRESULT=0x" + pinHr.ToString("X8");
                    return false;
                }

                message = "Widget pinned to all virtual desktops.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
            finally
            {
                ReleaseComObject(viewObject);
                ReleaseComObject(pinnedAppsObject);
                ReleaseComObject(collectionObject);
                ReleaseComObject(shellObject);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value))
            {
                return;
            }

            try
            {
                Marshal.ReleaseComObject(value);
            }
            catch
            {
            }
        }

        [ComImport]
        [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig]
            int QueryService(
                ref Guid guidService,
                ref Guid riid,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
        }

        [ComImport]
        [Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationViewCollection
        {
            [PreserveSig]
            int GetViews([MarshalAs(UnmanagedType.IUnknown)] out object views);

            [PreserveSig]
            int GetViewsByZOrder([MarshalAs(UnmanagedType.IUnknown)] out object views);

            [PreserveSig]
            int GetViewsByAppUserModelId(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.IUnknown)] out object views);

            [PreserveSig]
            int GetViewForHwnd(
                IntPtr hwnd,
                [MarshalAs(UnmanagedType.IUnknown)] out object view);
        }

        [ComImport]
        [Guid("4CE81583-1E4C-4632-A621-07A53543148F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopPinnedApps
        {
            [PreserveSig]
            int IsAppIdPinned(
                [MarshalAs(UnmanagedType.LPWStr)] string appId,
                [MarshalAs(UnmanagedType.Bool)] out bool pinned);

            [PreserveSig]
            int PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

            [PreserveSig]
            int UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

            [PreserveSig]
            int IsViewPinned(
                [MarshalAs(UnmanagedType.IUnknown)] object view,
                [MarshalAs(UnmanagedType.Bool)] out bool pinned);

            [PreserveSig]
            int PinView([MarshalAs(UnmanagedType.IUnknown)] object view);

            [PreserveSig]
            int UnpinView([MarshalAs(UnmanagedType.IUnknown)] object view);
        }
    }
}
