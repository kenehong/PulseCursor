using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LoadCursor;

static class RuntimeState
{
    internal static string? PauseReason(AppSettings settings, bool paused, bool fullscreen, bool energySaver)
    {
        if (paused) return "Paused";
        if (settings.NoResourcesSelected) return "No resources selected";
        if (settings.PauseInFullscreen && fullscreen) return "Paused in full-screen app";
        if (settings.PauseInEnergySaver && energySaver) return "Paused in energy saver";
        return null;
    }

    internal static bool IsFullscreen()
    {
        nint window = GetForegroundWindow();
        if (window == 0 || window == GetShellWindow() || window == GetDesktopWindow())
            return false;
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == Environment.ProcessId)
            return false;
        if (!GetClientRect(window, out var rect))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read foreground window bounds");
        var topLeft = new Point(rect.Left, rect.Top);
        var bottomRight = new Point(rect.Right, rect.Bottom);
        if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read full-screen state");
        // Client bounds exclude maximized windows with a title bar/taskbar.
        return CoversMonitor(Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y),
            Screen.FromHandle(window).Bounds);
    }

    internal static bool CoversMonitor(Rectangle client, Rectangle monitor) =>
        client.Left <= monitor.Left + 1 && client.Top <= monitor.Top + 1 &&
        client.Right >= monitor.Right - 1 && client.Bottom >= monitor.Bottom - 1;

    internal static bool IsEnergySaver()
    {
        if (!GetSystemPowerStatus(out var power))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read energy saver state");
        return power.SystemStatusFlag == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryPercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll")] static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemPowerStatus(out PowerStatus status);
}
