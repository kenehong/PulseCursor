using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LoadCursor;

public sealed partial class SettingsWindow : Microsoft.UI.Xaml.Window, IDisposable, INotifyPropertyChanged
{
    readonly SystemCursor cursor;
    readonly ImageSource?[] previews = new ImageSource?[4];
    readonly string?[] previewColors = new string?[4];
    int previewRevision;
    bool ready, disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public AppSettings Settings { get; }
    public ImageSource? NormalPreview => previews[0];
    public ImageSource? BusyPreview => previews[1];
    public ImageSource? HotPreview => previews[2];
    public ImageSource? CriticalPreview => previews[3];
    internal Task PreviewUpdate { get; private set; } = Task.CompletedTask;

    internal SettingsWindow(AppSettings settings, SystemCursor cursor)
    {
        Settings = settings;
        this.cursor = cursor;
        InitializeComponent();
        Bindings.Initialize();
        nint window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(window) / 96d;
        var area = Screen.FromHandle(window).WorkingArea;
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
            (int)Math.Min(600 * scale, area.Width - 32),
            (int)Math.Min(760 * scale, area.Height - 64)));
        AppWindow.Move(new Windows.Graphics.PointInt32(area.Left + (area.Width - AppWindow.Size.Width) / 2,
            area.Top + (area.Height - AppWindow.Size.Height) / 2));
        AppWindow.Closing += (_, args) =>
        {
            if (!disposed)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };
        Settings.PropertyChanged += SettingsChanged;
        ApplyTheme();
        ready = true;
        RefreshPreviews();
    }

    void SettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        ApplyTheme();
        RefreshPreviews();
    }
    void ApplyTheme() => SettingsRoot.RequestedTheme = Settings.Theme switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    async void RefreshPreviews()
    {
        try
        {
            PreviewUpdate = UpdatePreviewsAsync(++previewRevision);
            await PreviewUpdate;
        }
        catch (Exception error) when (error is IOException or ArgumentException or ExternalException)
        {
            Console.Error.WriteLine($"Cursor preview: {error}");
            if (!disposed)
            {
                ValidationMessage.Message = $"Cursor preview unavailable: {error.Message}";
                ValidationMessage.IsOpen = true;
            }
        }
    }

    async Task UpdatePreviewsAsync(int revision)
    {
        string[] colors = [Settings.NormalColor, Settings.BusyColor, Settings.HotColor, Settings.CriticalColor];
        string[] properties = [nameof(NormalPreview), nameof(BusyPreview), nameof(HotPreview), nameof(CriticalPreview)];
        for (int i = 0; i < colors.Length; i++)
        {
            if (disposed || revision != previewRevision) return;
            if (colors[i] == previewColors[i]) continue;
            using var bitmap = cursor.Image(ColorTranslator.FromHtml(colors[i]));
            var image = await Images.FromBitmapAsync(bitmap);
            if (disposed || revision != previewRevision) return;
            previews[i] = image;
            previewColors[i] = colors[i];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(properties[i]));
        }
    }

    void OnThresholdChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!ready) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed) return;
            var next = (BusyThresholdInput.Value, HotThresholdInput.Value, CriticalThresholdInput.Value);
            if (next == Settings.Thresholds)
            {
                ValidationMessage.IsOpen = false;
                return;
            }
            try
            {
                Settings.SetThresholds(next.Item1, next.Item2, next.Item3);
                ValidationMessage.IsOpen = false;
            }
            catch (ArgumentException error)
            {
                ValidationMessage.Message = error.Message;
                ValidationMessage.IsOpen = true;
            }
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Settings.PropertyChanged -= SettingsChanged;
        Close();
    }

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
}
