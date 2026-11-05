using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace LoadCursor;

static class Images
{
    internal static async Task<BitmapImage> FromBitmapAsync(Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png.ToArray());
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }
}
