using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LoadCursor;

static class UiEvidence
{
    internal static async Task RunAsync()
    {
        using var card = new PeekCard();
        var settings = new AppSettings();
        var area = Screen.PrimaryScreen!.WorkingArea;
        var anchor = new Point(area.Right - 400, area.Top + 40);
        foreach (int theme in new[] { 1, 2 })
        {
            settings.Theme = theme;
            card.RefreshSample(new Sample(46, 88, 61, null), LoadLevel.Hot, settings);
            card.ShowAt(anchor);
            await Task.Delay(300);
            card.RefreshSample(new Sample(46, 88, 61, null), LoadLevel.Hot, settings);
            await Task.Delay(100);
            SelfTest.Require(card.Model.CpuApp.Name != "Measuring..." && card.Model.CpuApp.Usage.EndsWith('%'),
                "CPU leader advances from its initial baseline to a measured app");
            SelfTest.Require(RuntimeState.GetForegroundWindow() != WinRT.Interop.WindowNative.GetWindowHandle(card),
                "Peek does not steal focus");
            var root = (FrameworkElement)card.Content;
            SelfTest.Require(root.ActualWidth > 200 && root.ActualHeight > 100 && root.ActualHeight < 300, "Compact card layout");
            var texts = Descendants(root).OfType<Microsoft.UI.Xaml.Controls.TextBlock>()
                .SelectMany(text => new[] { text.Text }.Concat(text.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text))).ToArray();
            SelfTest.Require(texts.Contains("HOT") && texts.Contains("46%") && texts.Contains("88%") && texts.Contains("61%"),
                "Visible card bindings contain live values");
            SelfTest.Require(texts.Contains("CPU top") && !texts.Contains("GPU top") &&
                Descendants(root).OfType<Microsoft.UI.Xaml.Controls.Image>().Count() == 1,
                "Only one CPU app row is rendered, with all system meters retained");
            var gpuMeter = Descendants(root).OfType<PulseMeter>().Single(meter => meter.Label == "GPU");
            SelfTest.Require(gpuMeter.Bars == PeekModel.Bars(88) && gpuMeter.Text == "88%" &&
                texts.Contains(new string('\u2588', 9)), "Segmented GPU meter binding reaches rendered cells");
            await SaveAsync(root, $"card-{(theme == 1 ? "light" : "dark")}.png");
            int height = card.AppWindow.ClientSize.Height;
            settings.MonitorGpu = false;
            card.RefreshSample(new Sample(46, null, 61, null), LoadLevel.Busy, settings);
            await Task.Delay(150);
            SelfTest.Require(card.AppWindow.ClientSize.Height < height,
                $"Open card shrinks when a resource is disabled ({height} -> {card.AppWindow.ClientSize.Height})");
            settings.MonitorGpu = true;
            card.RefreshSample(new Sample(46, 88, 61, null), LoadLevel.Hot, settings);
            await Task.Delay(150);
            SelfTest.Require(card.AppWindow.ClientSize.Height == height, "Open card grows without clipping re-enabled resources");
            card.HideAndReset();
        }
        card.Model.CpuApp.Update(new("PulseCursor", "23%", "CPU app icon fixture.", Environment.ProcessPath));
        await Task.Delay(150);
        SelfTest.Require(card.Model.CpuApp.Icon != null && card.Model.CpuApp.Usage == "23%", "CPU app and executable icon");
        SelfTest.Require(Descendants(card.Content).OfType<Microsoft.UI.Xaml.Controls.Image>().Any(image => image.Source == card.Model.CpuApp.Icon),
            "App icon is bound to the visible card");
        card.RefreshSample(new Sample(12, 88, 98, null), LoadLevel.Hot, settings);
        SelfTest.Require(!card.Model.CpuApp.Text.Contains("GB") && card.Model.MemoryText == "98%" &&
            card.Model.GpuText == "88%", "High GPU or memory usage does not replace the CPU app leader");
        settings.MonitorCpu = false;
        card.RefreshSample(new Sample(null, 88, 98, null), LoadLevel.Hot, settings);
        SelfTest.Require(!card.Model.CpuEnabled && card.Model.CpuApp.Name == "Off" && card.Model.CpuApp.Icon == null,
            "Turning off CPU clears its app leader");
        settings.MonitorCpu = true;
        card.RefreshSample(new Sample(12, null, 98, "Counter unavailable"), LoadLevel.Hot, settings);
        SelfTest.Require(card.Model.CpuApp.Name == "Measuring..." && card.Model.GpuText == "N/A" &&
            card.Model.GpuNote == "Counter unavailable", "Unavailable GPU does not prevent CPU app sampling");
        using var cursor = new SystemCursor();
        using var window = new SettingsWindow(settings, cursor);
        foreach (int theme in new[] { 1, 2 })
        {
            settings.Theme = theme;
            window.AppWindow.Show(false);
            await Task.Delay(250);
            await window.PreviewUpdate;
            var resources = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.CheckBox>()
                .Where(box => box.Content is string text && text is "CPU" or "GPU" or "Memory").ToArray();
            SelfTest.Require(resources.Length == 3 && resources.All(box => box.IsChecked == true),
                "Resource settings bindings");
            var previews = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.Image>()
                .Where(image => image.Name.EndsWith("CursorPreview", StringComparison.Ordinal)).ToArray();
            SelfTest.Require(previews.Length == 4 && previews.All(image => image.Source != null), "Four real cursor previews are bound");
            await SaveAsync((FrameworkElement)window.Content, $"settings-{(theme == 1 ? "light" : "dark")}.png");
            var scroll = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.ScrollViewer>()
                .Single(view => view.Name == "SettingsScroll");
            SelfTest.Require(scroll.ScrollableHeight > 0, "Settings content can scroll");
            scroll.ChangeView(null, scroll.ScrollableHeight / 2, null, disableAnimation: true);
            await Task.Delay(150);
            SelfTest.Require(scroll.VerticalOffset > 0, "Settings scroll reveals lower controls");
            await SaveAsync((FrameworkElement)window.Content, $"settings-colors-{(theme == 1 ? "light" : "dark")}.png");
            await CheckColorPickersAsync(window, settings, theme);
            scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
            await Task.Delay(150);
            SelfTest.Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "Settings can reach the last controls");
            await SaveAsync((FrameworkElement)window.Content, $"settings-behavior-{(theme == 1 ? "light" : "dark")}.png");
            scroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        var busy = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.NumberBox>().Single(box => box.Name == "BusyThresholdInput");
        busy.Value = 55;
        await Task.Delay(100);
        SelfTest.Require(settings.BusyThreshold == 55 && settings.Sensitivity == 3, "Edited NumberBox updates custom settings");
        busy.Value = 99;
        await Task.Delay(100);
        SelfTest.Require(settings.BusyThreshold == 55 &&
            Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.InfoBar>().Any(bar => bar.Name == "ValidationMessage" && bar.IsOpen),
            "Invalid NumberBox input keeps the saved value and shows an error");
        busy.Value = 55;
        var colorButton = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.Button>()
            .Single(button => button.Flyout is Microsoft.UI.Xaml.Controls.Flyout
            {
                Content: Microsoft.UI.Xaml.Controls.ColorPicker picker
            } && picker.Color == Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var colorFlyout = (Microsoft.UI.Xaml.Controls.Flyout)colorButton.Flyout;
        var picker = (Microsoft.UI.Xaml.Controls.ColorPicker)colorFlyout.Content;
        colorButton.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(150);
        colorFlyout.ShowAt(colorButton);
        await Task.Delay(250);
        SelfTest.Require(picker.IsLoaded && picker.ActualWidth > 0, "Color picker opens and loads its template");
        var originalPreview = window.NormalPreview;
        var unchangedPreview = window.BusyPreview;
        picker.Color = Windows.UI.Color.FromArgb(255, 17, 34, 51);
        picker.Color = Windows.UI.Color.FromArgb(255, 68, 85, 102);
        await window.PreviewUpdate;
        await Task.Delay(100);
        SelfTest.Require(settings.NormalColor == "#445566" && window.NormalPreview != originalPreview &&
            window.BusyPreview == unchangedPreview, "Latest color updates its preview without rerendering unchanged cursors");
        SelfTest.Require(Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.Image>()
            .Single(image => image.Name == "NormalCursorPreview").Source == window.NormalPreview, "Live preview reaches the visible image");
        colorFlyout.Hide();
        await Task.Delay(150);
        SelfTest.Require(!colorFlyout.IsOpen, "Color picker closes after editing");
        var themePicker = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.ComboBox>().First();
        themePicker.SelectedIndex = 1;
        await Task.Delay(100);
        SelfTest.Require(settings.Theme == 1, "Theme selection is two-way");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(360, 640));
        await Task.Delay(250);
        var narrowRoot = (FrameworkElement)window.Content;
        foreach (var image in Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.Image>()
            .Where(image => image.Name.EndsWith("CursorPreview", StringComparison.Ordinal)))
        {
            double x = image.TransformToVisual(narrowRoot).TransformPoint(new Windows.Foundation.Point()).X;
            SelfTest.Require(x >= 0 && x + image.ActualWidth <= narrowRoot.ActualWidth, "Narrow settings keep cursor previews inside the window");
        }
        await SaveAsync(narrowRoot, "settings-narrow.png");
        var narrowScroll = Descendants(window.Content).OfType<Microsoft.UI.Xaml.Controls.ScrollViewer>()
            .Single(scroll => scroll.Name == "SettingsScroll");
        narrowScroll.ChangeView(null, narrowScroll.ScrollableHeight / 2, null, disableAnimation: true);
        await Task.Delay(150);
        await SaveAsync(narrowRoot, "settings-narrow-colors.png");
        Console.WriteLine("WinUI Light/Dark cards, native frame, live cursor previews, narrow layout, focus and app icons passed.");
    }

    static async Task CheckColorPickersAsync(SettingsWindow window, AppSettings settings, int theme)
    {
        var selectors = Descendants(window.Content).OfType<ColorSelector>().ToArray();
        Windows.UI.Color[] Colors() =>
            [settings.NormalColorValue, settings.BusyColorValue, settings.HotColorValue, settings.CriticalColorValue];
        var original = Colors();
        SelfTest.Require(selectors.Length == 4, "Four color selectors");
        for (int i = 0; i < selectors.Length; i++)
        {
            var button = Descendants(selectors[i]).OfType<Microsoft.UI.Xaml.Controls.Button>()
                .Single(button => button.Flyout is Microsoft.UI.Xaml.Controls.Flyout);
            var flyout = (Microsoft.UI.Xaml.Controls.Flyout)button.Flyout;
            var picker = (Microsoft.UI.Xaml.Controls.ColorPicker)flyout.Content;
            button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await Task.Delay(100);
            flyout.ShowAt(button);
            await Task.Delay(200);
            SelfTest.Require(flyout.IsOpen && picker.IsLoaded && picker.ActualHeight > 100 &&
                picker.Color == original[i], "Color selector opens the native picker with the current color");
            SelfTest.Require(picker.ActualTheme == ((FrameworkElement)window.Content).ActualTheme,
                "Color picker follows the settings theme");
            if (i == 0)
                await SaveAsync(picker, $"color-picker-{(theme == 1 ? "light" : "dark")}.png");
            var edited = Windows.UI.Color.FromArgb(255, (byte)(17 + 20 * i), 34, 51);
            picker.Color = edited;
            await window.PreviewUpdate;
            var changed = Colors();
            SelfTest.Require(changed.Where((color, index) => color != (index == i ? edited : original[index])).Count() == 0,
                "Open color picker updates only its own saved color");
            picker.Color = original[i];
            await window.PreviewUpdate;
            flyout.Hide();
            await Task.Delay(100);
            SelfTest.Require(!flyout.IsOpen, "Color selector closes without terminating the app");
        }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    static async Task SaveAsync(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        SelfTest.Require(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "Rendered UI is not empty");
        var pixels = await bitmap.GetPixelsAsync();
        var bytes = new byte[pixels.Length];
        using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var output = new DataReader(stream.GetInputStreamAt(0));
        await output.LoadAsync((uint)stream.Size);
        var png = new byte[stream.Size];
        output.ReadBytes(png);
        string path = Path.Combine(SelfTest.OutputDirectory, name);
        await File.WriteAllBytesAsync(path, png);
        Console.WriteLine($"Rendered: {path}");
    }
}
