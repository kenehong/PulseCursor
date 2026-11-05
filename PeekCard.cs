using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace LoadCursor;

public sealed partial class PeekCard : Microsoft.UI.Xaml.Window, IDisposable
{
    readonly HoldGesture gesture = new();
    readonly NonActivatingWindow native;
    bool disposed;
    int theme = -1;
    LoadLevel shownLevel;
    Point anchor;
    public PeekModel Model { get; } = new();
    internal bool IsVisible => AppWindow.IsVisible;

    internal PeekCard()
    {
        InitializeComponent();
        // A non-activating window never raises the event that normally starts x:Bind.
        Bindings.Initialize();
        native = new NonActivatingWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        CardControl.ActualThemeChanged += OnActualThemeChanged;
        CardControl.Loaded += (_, _) => VisualStateManager.GoToState(CardControl, shownLevel.ToString(), false);
        ApplyNativeTheme();
        AppWindow.Closing += (_, args) =>
        {
            if (!disposed)
            {
                args.Cancel = true;
                HideAndReset();
            }
        };
    }

    void OnOpenTaskManager(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskmgr.exe"))
            { UseShellExecute = true });
        }
        catch (Win32Exception error)
        {
            Console.Error.WriteLine(error);
            MessageBox.Show(error.Message, "Can't open Task Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        gesture.Dismiss();
        HideAndReset();
    }

    internal void ApplyTheme(int value)
    {
        if (value == theme) return;
        theme = value;
        CardControl.RequestedTheme = value switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        ApplyNativeTheme();
    }

    void OnActualThemeChanged(FrameworkElement sender, object args) => ApplyNativeTheme();

    void ApplyNativeTheme()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        SetFrameAttribute(20, CardControl.ActualTheme == ElementTheme.Dark ? 1u : 0u);
        SetFrameAttribute(33, 2);
        // XAML owns the outline; the default light DWM border must not surround it.
        SetFrameAttribute(34, 0xFFFFFFFE);
    }

    void SetFrameAttribute(int attribute, uint value) =>
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(native.Handle, attribute, ref value, sizeof(uint)));

    internal void HideAndReset()
    {
        if (IsVisible)
        {
            AppWindow.Hide();
            Model.Reset();
        }
    }

    internal void Poll(Sample sample, LoadLevel level, AppSettings settings, bool? shiftOverride = null)
    {
        Point mouse = Cursor.Position;
        var bounds = new Rectangle(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        bool shiftDown = GetAsyncKeyState(0x10) < 0;
        bool show = gesture.Update(shiftOverride ?? shiftDown, GetAsyncKeyState(0x1B) < 0,
            IsVisible && bounds.Contains(mouse), Environment.TickCount64);
        if (!show)
        {
            HideAndReset();
            return;
        }
        if (IsVisible)
            return;
        RefreshSample(sample, level, settings);
        ShowAt(mouse);
    }

    internal void ShowAt(Point mouse)
    {
        anchor = mouse;
        AppWindow.Move(new Windows.Graphics.PointInt32(mouse.X + 16, mouse.Y + 16));
        FitToScreen(mouse);
        AppWindow.Show(false);
        DispatcherQueue.TryEnqueue(() => { if (!disposed && IsVisible) FitToScreen(mouse); });
    }

    void FitToScreen(Point mouse)
    {
        Rectangle area = Screen.FromPoint(mouse).WorkingArea;
        double scale = GetDpiForWindow(native.Handle) / 96d;
        CardScroll.MaxHeight = Math.Max(64, area.Height / scale - 16);
        CardControl.Measure(new Windows.Foundation.Size(330, double.PositiveInfinity));
        double height = CardControl.DesiredSize.Height;
        var size = new Size((int)Math.Ceiling(330 * scale), (int)Math.Ceiling(Math.Min(height, area.Height / scale - 16) * scale));
        int heightPixels = Math.Max(1, size.Height);
        if (AppWindow.ClientSize.Width != size.Width || AppWindow.ClientSize.Height != heightPixels)
            AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(size.Width, heightPixels));
        Point location = Fit(mouse, new Size(AppWindow.Size.Width, AppWindow.Size.Height), area);
        if (AppWindow.Position.X != location.X || AppWindow.Position.Y != location.Y)
            AppWindow.Move(new Windows.Graphics.PointInt32(location.X, location.Y));
    }

    internal static Point Fit(Point mouse, Size size, Rectangle area) => new(
        Math.Clamp(mouse.X + 16, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(mouse.Y + 16, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));

    internal void RefreshSample(Sample sample, LoadLevel level, AppSettings settings)
    {
        ApplyTheme(settings.Theme);
        Model.Update(sample, level, settings);
        shownLevel = level;
        VisualStateManager.GoToState(CardControl, level.ToString(), false);
        if (IsVisible)
            DispatcherQueue.TryEnqueue(() => { if (!disposed && IsVisible) FitToScreen(anchor); });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CardControl.ActualThemeChanged -= OnActualThemeChanged;
        native.ReleaseHandle();
        Close();
    }

    sealed class NonActivatingWindow : NativeWindow
    {
        internal NonActivatingWindow(nint window)
        {
            AssignHandle(window);
            nint style = GetWindowLongPtrW(window, -20);
            if (SetWindowLongPtrW(window, -20, style | 0x08000080) == 0 && Marshal.GetLastWin32Error() != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Set non-activating popup style");
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = 3; return; }
            base.WndProc(ref message);
        }
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern nint SetWindowLongPtrW(nint window, int index, nint value);
}
