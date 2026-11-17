using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Win32 screen metrics helpers.
///
/// The app declares PerMonitorV2 DPI awareness (app.manifest), so these return
/// physical pixel dimensions of the primary monitor — the same space SendInput
/// absolute moves operate in. Multi-monitor (virtual desktop) support is a
/// future refinement of M5.
/// </summary>
public static class ScreenInterop
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>Primary screen size in pixels. Returns (1920, 1080) as a safe fallback.</summary>
    public static (int Width, int Height) GetPrimaryScreenSize()
    {
        int w = GetSystemMetrics(SM_CXSCREEN);
        int h = GetSystemMetrics(SM_CYSCREEN);
        return (w > 0 && h > 0) ? (w, h) : (1920, 1080);
    }

    /// <summary>Moves the local cursor. Returns true on success.</summary>
    public static bool TrySetCursorPos(int x, int y) => SetCursorPos(x, y);

    /// <summary>Reads the local cursor position. Returns false if it could not be read.</summary>
    public static bool TryGetCursorPos(out int x, out int y)
    {
        var pt = new POINT();
        if (GetCursorPos(ref pt))
        {
            x = pt.X;
            y = pt.Y;
            return true;
        }
        x = y = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref POINT pt);
}
