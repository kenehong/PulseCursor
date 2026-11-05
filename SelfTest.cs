namespace LoadCursor;

static class SelfTest
{
    internal static string OutputDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "PulseCursor", "self-test", Environment.ProcessId.ToString());

    internal static async Task RunAsync()
    {
        Directory.CreateDirectory(OutputDirectory);
        CheckSettings();
        var defaults = new AppSettings();
        foreach (var (load, expected) in new[]
        {
            (0d, LoadLevel.Normal), (59.9, LoadLevel.Normal),
            (60d, LoadLevel.Busy), (79.9, LoadLevel.Busy),
            (80d, LoadLevel.Hot), (94.9, LoadLevel.Hot),
            (95d, LoadLevel.Hot), (100d, LoadLevel.Hot)
        })
            Require(new LoadState().Update(load, 0) == expected, $"Threshold: {load}");
        var state = new LoadState();
        Require(state.Update(95, 0) == LoadLevel.Hot, "Critical delay starts");
        Require(state.Update(100, 9999) == LoadLevel.Hot, "Critical delay not complete");
        Require(state.Update(95, 10000) == LoadLevel.Critical, "Ten continuous seconds");
        Require(state.Update(94, 11000) == LoadLevel.Hot, "Critical resets below threshold");
        Require(state.Update(95, 12000) == LoadLevel.Hot, "Critical timer restarted");
        state.Update(null, 22000);
        Require(state.Update(95, 23000) == LoadLevel.Hot, "Missing sample clears critical timer");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 101 })
        {
            bool rejected = false;
            try { new LoadState().Update(invalid, 0); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid percentage accepted");
        }
        var samples = new[]
        {
            ("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 40d),
            ("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 35d),
            ("pid_1_luid_0x0_0x1_phys_0_eng_1_engtype_Copy", 50d),
            ("pid_1_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 60d)
        };
        var readings = Metrics.ParseGpuSamples(samples).ToArray();
        Require(Metrics.BusiestEngine(readings) == 75, "GPU engine grouping");
        Require(TopProcess.CpuPercent(200, 1000, 4) == 5 && TopProcess.CpuPercent(-5, 1000, 4) == 0,
            "CPU share uses elapsed time, logical processors and restart guard");
        Require(PeekModel.Bars(82) == "\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2591\u2591" &&
            PeekModel.Bars(null) == new string('\u2591', 10), "Segmented meters represent the sample");
        Require(Metrics.BusiestEngine([]) == null, "Missing GPU must not read as zero");
        Require(Metrics.BusiestEngine(Metrics.ParseGpuSamples([("bad", 10), (samples[0].Item1, double.NaN)])) == null, "Invalid GPU samples");
        Require(Metrics.BusiestEngine(Metrics.ParseGpuSamples([(samples[0].Item1, 75), (samples[1].Item1, 60)])) == 100, "GPU upper bound");
        var sample = new Sample(40, null, 80, "unsupported");
        Require(sample.Select(LoadSource.Highest) == 80 && sample.Select(LoadSource.Gpu) == null, "Optional GPU");
        Require(new Sample(40, 95, 80, null).Select(LoadSource.Highest) == 95, "GPU drives highest load");
        Require(sample.Resolve(LoadSource.Highest) == LoadSource.Ram, "Missing GPU cannot lead");
        Color tint = defaults.CursorColor(LoadLevel.Busy);
        Require(SystemCursor.TintPixel(Color.White, tint).ToArgb() == tint.ToArgb(), "White tint");
        Require(SystemCursor.TintPixel(Color.Black, tint).ToArgb() == Color.Black.ToArgb(), "Black outline");
        Require(SystemCursor.TintPixel(Color.FromArgb(0, 255, 255, 255), tint).A == 0, "Transparency");
        var hold = new HoldGesture();
        Require(!hold.Update(true, false, false, 0), "Shift tap stays hidden");
        Require(!hold.Update(true, false, false, 599), "Hold threshold");
        Require(hold.Update(true, false, false, 600), "Long Shift reveals card");
        Require(hold.Update(false, false, true, 700), "Hover keeps card open");
        Require(!hold.Update(false, false, false, 800), "Release outside closes card");
        hold.Update(true, false, false, 1000);
        Require(!hold.Update(true, true, false, 1700), "Escape dismisses card");
        Require(!hold.Update(true, false, false, 1800), "Held Shift cannot reopen dismissed card");
        var placement = PeekCard.Fit(new Point(1910, 1070), new Size(386, 294), new Rectangle(0, 0, 1920, 1080));
        Require(placement.X + 386 <= 1920 && placement.Y + 294 <= 1080, "Card stays on screen");
        using (var cursor = new SystemCursor())
        {
            using var normal = cursor.Image(Color.White);
            using var yellow = cursor.Image(defaults.CursorColor(LoadLevel.Busy));
            using var orange = cursor.Image(defaults.CursorColor(LoadLevel.Hot));
            using var red = cursor.Image(defaults.CursorColor(LoadLevel.Critical));
            using var ring = cursor.Image(Color.Red, 0.25);
            using var laterRing = cursor.Image(Color.Red, 0.5);
            Require(normal.Size == yellow.Size && normal.Size == red.Size, "Cursor size");
            Require(!SamePixels(normal, yellow) && !SamePixels(yellow, orange) && !SamePixels(orange, red), "Visible cursor colors");
            Require(ring.Width > red.Width && !SamePixels(ring, laterRing), "Optional critical pulse");
            int white = 0, colored = 0;
            for (int y = 0; y < normal.Height; y++)
                for (int x = 0; x < normal.Width; x++)
                {
                    Color before = normal.GetPixel(x, y), after = yellow.GetPixel(x, y);
                    Require(before.A == after.A, "Fill keeps cursor alpha");
                    if (before.R == 0 && before.G == 0 && before.B == 0)
                        Require(after.R == 0 && after.G == 0 && after.B == 0, "Fill keeps black outline");
                    if (before.A == 255 && before.R == 255 && before.G == 255 && before.B == 255)
                    {
                        if (before == after) white++; else colored++;
                    }
                }
            Require(white == 0 && colored > 0, "Plain color fills the entire light cursor body");
            Console.WriteLine($"Cursor images: {normal.Width}x{normal.Height}; colors distinct.");
        }
        await UiEvidence.RunAsync();
        using (var metrics = new Metrics())
        {
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(1000);
                var live = metrics.Read();
                Require(live.Cpu.HasValue && double.IsFinite(live.Cpu.Value) && live.Cpu is >= 0 and <= 100, "Live CPU");
                Require(live.Ram is >= 0 and <= 100, "Live RAM");
                Require(live.Gpu is null || double.IsFinite(live.Gpu.Value) && live.Gpu is >= 0 and <= 100, "Live GPU");
                Require(live.Gpu.HasValue != (live.GpuError != null), "GPU failure must be explicit");
                Console.WriteLine($"CPU {live.Cpu:F1}% | GPU {(live.Gpu.HasValue ? $"{live.Gpu:F1}%" : $"N/A: {live.GpuError}")} | RAM {live.Ram:F0}%");
            }
        }
        using (var memoryOnly = new Metrics(new AppSettings { MonitorCpu = false, MonitorGpu = false }))
        {
            var live = memoryOnly.Read();
            Require(live.Cpu == null && live.Gpu == null && live.Ram.HasValue, "Unselected counters are not sampled");
        }
        using (var tray = new MonitorController(new AppSettings
        {
            MonitorCpu = false,
            MonitorGpu = false,
            MonitorMemory = false
        }, () => { }))
            await Task.Delay(2200);
        Console.WriteLine("Self-test passed. No system cursor changes requested.");
    }

    internal static void Demo(SystemCursor cursor)
    {
        var defaults = new AppSettings();
        using var original = SystemCursor.CurrentImage();
        foreach (var level in new[] { LoadLevel.Busy, LoadLevel.Hot, LoadLevel.Critical, LoadLevel.Normal })
        {
            if (level == LoadLevel.Normal) cursor.Restore();
            else cursor.Apply(level, defaults.CursorColor(level), showPulse: true);
            using var current = SystemCursor.CurrentImage();
            if (level == LoadLevel.Normal)
                Require(SamePixels(current, original), $"Applied {level}");
            Console.WriteLine($"Cursor: {level}");
            for (int frame = 0; frame < 60; frame++)
            {
                cursor.Animate(1d / 30);
                Thread.Sleep(33);
            }
        }
        cursor.Apply(LoadLevel.Critical, Color.Red);
        cursor.Dispose();
        using var restored = SystemCursor.CurrentImage();
        Require(SamePixels(original, restored), "Restore original cursor on disposal");
        Console.WriteLine("Cursor restored.");
    }

    static bool SamePixels(Bitmap first, Bitmap second)
    {
        if (first.Size != second.Size) return false;
        for (int y = 0; y < first.Height; y++)
            for (int x = 0; x < first.Width; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
        return true;
    }

    static void CheckSettings()
    {
        var settings = new AppSettings();
        settings.Sensitivity = 0;
        Require(settings.Thresholds == (50, 70, 90), "Sensitive preset");
        settings.Sensitivity = 2;
        Require(settings.Thresholds == (70, 88, 97), "Relaxed preset");
        settings.SetThresholds(55, 75, 92);
        Require(settings.Sensitivity == 3 && new LoadState().Update(55, 0, settings) == LoadLevel.Busy, "Custom thresholds");
        bool rejected = false;
        try { settings.SetThresholds(90, 70, 95); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected && settings.Thresholds == (55, 75, 92), "Invalid thresholds cannot modify settings");
        rejected = false;
        try { settings.SetThresholds(double.NaN, 70, 95); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Blank NumberBox value is rejected");
        rejected = false;
        try { settings.BusyColor = "invalid"; }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid colors are rejected");
        settings.BusyColor = "#33AAFF";
        settings.Theme = 2;
        string path = Path.Combine(OutputDirectory, "self-test-settings.json");
        try
        {
            settings.Save(path);
            var restored = AppSettings.Load(path);
            Require(restored.Thresholds == settings.Thresholds && restored.BusyColor == "#33AAFF" && restored.Theme == 2, "Settings persistence");
        }
        finally { File.Delete(path); }
        settings.MonitorCpu = settings.MonitorGpu = settings.MonitorMemory = false;
        Require(RuntimeState.PauseReason(settings, false, false, false) != null, "All resources off pauses sampling");
        settings.MonitorCpu = true;
        Require(RuntimeState.PauseReason(settings, false, true, false) != null, "Full-screen pause");
        settings.PauseInFullscreen = false;
        Require(RuntimeState.PauseReason(settings, false, true, false) == null, "Full-screen opt out");
        settings.PauseInEnergySaver = true;
        Require(RuntimeState.PauseReason(settings, false, false, true) != null, "Energy saver stops sampling");
        Require(!RuntimeState.CoversMonitor(new Rectangle(0, 32, 1920, 1008), new Rectangle(0, 0, 1920, 1080)), "Maximized window is not full screen");
        Require(RuntimeState.CoversMonitor(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080)), "Borderless full screen");
        Console.WriteLine("Settings and runtime policy passed.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Self-test failed: {message}");
    }

}
