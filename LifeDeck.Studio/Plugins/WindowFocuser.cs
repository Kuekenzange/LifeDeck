using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LifeDeck.Studio.Plugins;

public static class WindowFocuser
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public static bool FocusProcessMainWindow(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(p.MainWindowHandle, SW_RESTORE);
                Thread.Sleep(80);
                return SetForegroundWindow(p.MainWindowHandle);
            }
            catch
            {
                // Try the next process instance.
            }
        }
        return false;
    }
}
