using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LoadCursor;

public sealed class AppActivity : INotifyPropertyChanged
{
    string? path, friendlyName;
    string note = "", iconError = "";
    string processName = "Measuring...";
    int revision;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => friendlyName ?? processName;
    public string Usage { get; private set; } = "";
    public string Text => string.IsNullOrEmpty(Usage) ? Name : $"{Name} · {Usage}";
    public string Note => note + iconError;
    public ImageSource? Icon { get; private set; }
    public Visibility IconVisibility => Icon == null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

    internal void Update(AppUsage app)
    {
        processName = app.Name;
        Usage = app.Usage;
        note = app.Note;
        if (path != app.IconPath)
        {
            path = app.IconPath;
            friendlyName = null;
            Icon = null;
            iconError = "";
            LoadIcon(path, ++revision);
        }
        Notify();
    }

    async void LoadIcon(string? file, int current)
    {
        if (file == null) return;
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(file).FileDescription;
            friendlyName = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(file);
            if (icon == null)
                throw new IOException("The executable has no app icon.");
            using var bitmap = icon.ToBitmap();
            var image = await Images.FromBitmapAsync(bitmap);
            if (revision != current) return;
            Icon = image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            if (revision != current) return;
            iconError = $" Icon unavailable: {error.Message}";
            Console.Error.WriteLine($"App icon: {error.Message}");
        }
        finally
        {
            if (revision == current) Notify();
        }
    }

    void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
