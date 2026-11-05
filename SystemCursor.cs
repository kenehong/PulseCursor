using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LoadCursor;

sealed class SystemCursor : IDisposable
{
    const uint Arrow = 32512;
    nint original;
    Bitmap? source;
    Bitmap? tinted;
    Color tintColor;
    uint hotspotX, hotspotY;
    LoadLevel applied;
    Color appliedColor;
    bool usingOriginal = true;
    bool pulse;
    double phase;

    internal SystemCursor()
    {
        try
        {
            original = CheckHandle(CopyImage(CheckHandle(LoadCursorW(0, (nint)Arrow)), 2, 0, 0, 0));
            source = Snapshot(original);
            Check(GetIconInfo(original, out var info));
            hotspotX = info.XHotspot;
            hotspotY = info.YHotspot;
            FreeBitmaps(info);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Apply(LoadLevel level, Color color, bool showPulse = false)
    {
        bool nextPulse = level == LoadLevel.Critical && showPulse && AnimationsEnabled() &&
            !SystemInformation.HighContrast;
        if (!usingOriginal && level == applied && color == appliedColor && pulse == nextPulse)
            return;
        applied = level;
        appliedColor = color;
        pulse = nextPulse;
        phase = 0;
        Paint();
    }

    internal void Animate(double seconds)
    {
        if (!usingOriginal && pulse)
        {
            phase = (phase + Math.Min(seconds, 0.1) / 2) % 1;
            Paint();
        }
    }

    void Paint()
    {
        using var image = Image(appliedColor, pulse ? phase : -1);
        Check(SetSystemCursor(CreateCursor(image), Arrow));
        usingOriginal = false;
    }

    internal Bitmap Image(Color color, double pulsePhase = -1)
    {
        if (source == null)
            throw new ObjectDisposedException(nameof(SystemCursor));
        if (tinted == null || color.ToArgb() != tintColor.ToArgb())
        {
            tinted?.Dispose();
            tinted = new Bitmap(source);
            for (int x = 0; x < source.Width; x++)
                for (int y = 0; y < source.Height; y++)
                    tinted.SetPixel(x, y, TintPixel(source.GetPixel(x, y), color));
            tintColor = color;
        }
        if (pulsePhase < 0)
            return new Bitmap(tinted);
        int padding = (int)Math.Ceiling(source.Width * 0.6);
        var result = new Bitmap(source.Width + padding * 2, source.Height + padding * 2);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            foreach (double offset in new[] { 0d, 0.5 })
            {
                double progress = (pulsePhase + offset) % 1;
                float radius = (float)(padding * (0.25 + 0.65 * progress));
                using var pen = new Pen(Color.FromArgb((int)(110 * (1 - progress)), color), Math.Max(1, source.Width / 24f));
                graphics.DrawEllipse(pen, padding + hotspotX - radius, padding + hotspotY - radius, radius * 2, radius * 2);
            }
            graphics.DrawImageUnscaled(tinted, padding, padding);
        }
        return result;
    }

    internal static Bitmap CurrentImage() => Snapshot(CheckHandle(LoadCursorW(0, (nint)Arrow)));

    static Bitmap Snapshot(nint handle)
    {
        using var icon = Icon.FromHandle(handle);
        return icon.ToBitmap();
    }

    nint CreateCursor(Bitmap image)
    {
        nint icon = image.GetHicon();
        try
        {
            Check(GetIconInfo(icon, out var info));
            try
            {
                info.IsIcon = false;
                info.XHotspot = hotspotX + (uint)((image.Width - source!.Width) / 2);
                info.YHotspot = hotspotY + (uint)((image.Height - source.Height) / 2);
                return CheckHandle(CreateIconIndirect(ref info));
            }
            finally { FreeBitmaps(info); }
        }
        finally { DestroyIcon(icon); }
    }

    internal static Color TintPixel(Color pixel, Color color)
    {
        // Keep alpha, the black outline, size and click hotspot; tint the light fill.
        int light = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
        return Color.FromArgb(pixel.A, color.R * light / 255, color.G * light / 255, color.B * light / 255);
    }

    internal static void ReloadScheme() => Check(SystemParametersInfoW(0x57, 0, 0, 0));

    static bool AnimationsEnabled()
    {
        Check(SystemParametersInfoW(0x1042, 0, out int enabled, 0));
        return enabled != 0;
    }

    internal void Restore()
    {
        if (usingOriginal)
            return;
        Check(SetSystemCursor(CheckHandle(CopyImage(original, 2, 0, 0, 0)), Arrow));
        usingOriginal = true;
        pulse = false;
    }

    public void Dispose()
    {
        try
        {
            Restore();
        }
        finally
        {
            source?.Dispose();
            source = null;
            tinted?.Dispose();
            tinted = null;
            if (original != 0)
                DestroyCursor(original);
            original = 0;
        }
    }

    static void FreeBitmaps(IconInfo info)
    {
        if (info.Mask != 0) DeleteObject(info.Mask);
        if (info.Color != 0) DeleteObject(info.Color);
    }

    static nint CheckHandle(nint handle) => handle != 0 ? handle : throw new Win32Exception(Marshal.GetLastWin32Error());
    static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint XHotspot, YHotspot;
        public nint Mask, Color;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll", SetLastError = true)]
    static extern nint CopyImage(nint image, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyCursor(nint cursor);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(nint icon);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfoW(uint action, uint parameter, nint value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfoW(uint action, uint parameter, out int value, uint flags);
}
