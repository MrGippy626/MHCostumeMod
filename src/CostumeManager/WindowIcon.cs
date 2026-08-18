using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace CostumeManager
{

    internal static class WindowIcon
    {
        internal const string RelativePath = @"Assets\icon.ico";

        internal static void Apply(Window window)
        {
            try
            {
                string ico = Path.Combine(AppContext.BaseDirectory, RelativePath);
                if (!File.Exists(ico)) return;

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                Microsoft.UI.WindowId id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id)?.SetIcon(ico);
            }
            catch { }
        }
    }
}
