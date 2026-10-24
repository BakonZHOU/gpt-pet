using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexPetPlugin;

public readonly record struct TaskbarBounds(double ScreenTop, double BaselineBottom, double AvailableBottom);

public static class TaskbarAvoidance
{
    public static double ComputeBottom(double screenTop, double screenBottom, double workBottom, IEnumerable<(double Top, double Bottom)> bars)
    {
        // Leave the screen edge available so the mouse can raise an auto-hide taskbar.
        double bottom = Math.Min(workBottom, screenBottom - 2);
        foreach (var bar in bars)
        {
            if (bar.Top < (screenTop + screenBottom) / 2 || bar.Bottom < screenBottom - 1) continue;
            double visible = Math.Max(0, Math.Min(screenBottom, bar.Bottom) - Math.Max(screenTop, bar.Top));
            // A hidden auto-hide taskbar keeps a one/two pixel activation strip.
            // Reserve the whole bar as soon as it rises; do not chase every animation frame.
            if (visible > 4) bottom = Math.Min(bottom, screenBottom - (bar.Bottom - bar.Top) - 2);
        }
        return bottom;
    }
    public static bool ShouldAnchor(double petBottom, double availableBottom, double baselineBottom) => petBottom >= availableBottom - 28 && petBottom <= baselineBottom + 28;
    public static bool CanFall(double availableBottom, double baselineBottom) => baselineBottom - availableBottom <= 2.5;

    public static TaskbarBounds Read(Window window)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return new(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom, SystemParameters.WorkArea.Bottom);
        var bars = new List<(double Top, double Bottom)>();
        void Add(nint hwnd)
        {
            if (hwnd == 0 || !IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var r)) return;
            if (r.Right <= info.Monitor.Left || r.Left >= info.Monitor.Right || r.Right - r.Left < r.Bottom - r.Top) return;
            bars.Add((r.Top, r.Bottom));
        }
        Add(FindWindow("Shell_TrayWnd", null));
        nint previous = 0;
        for (int i = 0; i < 16; i++)
        {
            previous = FindWindowEx(0, previous, "Shell_SecondaryTrayWnd", null);
            if (previous == 0) break;
            Add(previous);
        }
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleY;
        return new(info.Monitor.Top / scale, info.Work.Bottom / scale,
            ComputeBottom(info.Monitor.Top, info.Monitor.Bottom, info.Work.Bottom, bars) / scale);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
