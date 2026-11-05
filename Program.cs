using System.ComponentModel;
using Microsoft.UI.Dispatching;

namespace LoadCursor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 0 && args is not ["--self-test"] && args is not ["--demo"] && args is not ["--restore"] &&
                args is not ["--settings"] && args is not ["--benchmark"])
                throw new ArgumentException("Usage: PulseCursor.exe [--settings | --self-test | --benchmark | --demo | --restore]");

            if (args is ["--self-test"])
            {
                App.Start(args, out int result);
                return result;
            }
            using var mutex = new Mutex(true, @"Local\LoadCursor", out bool created);
            if (!created)
                throw new InvalidOperationException("PulseCursor is already running. Exit it from the tray first.");
            if (args is ["--restore"])
            {
                SystemCursor.ReloadScheme();
                return 0;
            }
            if (args is ["--demo"])
            {
                using var cursor = new SystemCursor();
                SelfTest.Demo(cursor);
                return 0;
            }
            App.Start(args, out int exitCode);
            return exitCode;
        }
        catch (Exception error)
        {
            // Report at the application boundary, after cursor restoration/disposal.
            Console.Error.WriteLine(error);
            if (args.Length == 0)
                MessageBox.Show(error.Message, "PulseCursor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

}

sealed class MonitorController : IDisposable
{
    readonly SystemCursor cursor = new();
    Metrics? metrics;
    readonly NotifyIcon icon;
    readonly ContextMenuStrip menu = new();
    readonly DispatcherQueueTimer timer;
    readonly DispatcherQueueTimer frames;
    readonly DispatcherQueueTimer saveTimer;
    readonly PeekCard peek = new();
    readonly LoadState loadState = new();
    readonly AppSettings settings;
    readonly bool persistSettings;
    SettingsWindow? settingsWindow;
    readonly ToolStripMenuItem cpu = new("CPU");
    readonly ToolStripMenuItem gpu = new("GPU");
    readonly ToolStripMenuItem ram = new("RAM");
    readonly ToolStripMenuItem state = new("Measuring...");
    readonly ToolStripMenuItem pause = new("Pause") { CheckOnClick = true };
    LoadLevel level;
    Sample? latest;
    string? gpuError;
    long lastFrame = Environment.TickCount64;
    bool disposed;
    bool savePending;
    bool? benchmarkShift;
    string? suppression;
    (double Busy, double Hot, double Critical) thresholds;

    internal MonitorController(AppSettings settings, Action exit, bool persistSettings = true)
    {
        this.settings = settings;
        this.persistSettings = persistSettings;
        thresholds = settings.Thresholds;
        timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        frames = DispatcherQueue.GetForCurrentThread().CreateTimer();
        frames.Interval = TimeSpan.FromMilliseconds(33);
        saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        saveTimer.IsRepeating = false;
        saveTimer.Interval = TimeSpan.FromMilliseconds(300);
        saveTimer.Tick += (_, _) => SaveSettings();
        foreach (var item in new[] { state, cpu, gpu, ram })
        {
            item.Enabled = false;
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        pause.CheckedChanged += (_, _) =>
        {
            loadState.Update(null, Environment.TickCount64);
            UpdateSuppression();
            Render();
        };
        menu.Items.Add(pause);
        menu.Items.Add("Exit", null, (_, _) => exit());
        icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PulseCursor",
            ContextMenuStrip = menu,
            Visible = true
        };
        timer.Tick += (_, _) => { if (!disposed) Tick(); };
        frames.Tick += (_, _) =>
        {
            if (disposed || suppression != null)
                return;
            long now = Environment.TickCount64;
            cursor.Animate(Math.Max(0.001, (now - lastFrame) / 1000d));
            lastFrame = now;
            if (settings.ShiftDetails && latest is Sample sample && metrics != null)
                peek.Poll(sample, level, settings, benchmarkShift);
            else
                peek.HideAndReset();
        };
        settings.PropertyChanged += SettingsChanged;
        Tick();
        timer.Start();
        frames.Start();
    }

    internal void ShowSettings()
    {
        settingsWindow ??= new SettingsWindow(settings, cursor);
        settingsWindow.Activate();
    }

    internal async Task BenchmarkAsync()
    {
        benchmarkShift = false;
        await RuntimeBenchmark.MeasureAsync("background", () => latest.HasValue && !peek.IsVisible);
        settingsWindow = new SettingsWindow(settings, cursor);
        settingsWindow.Title = "PulseCursor settings - benchmark";
        settingsWindow.AppWindow.Show(false);
        await RuntimeBenchmark.MeasureAsync("settings", () => latest.HasValue && settingsWindow.AppWindow.IsVisible && !peek.IsVisible);
        settingsWindow.AppWindow.Hide();
        benchmarkShift = true;
        await RuntimeBenchmark.MeasureAsync("panel-after-settings", () => latest.HasValue && peek.IsVisible && !settingsWindow.AppWindow.IsVisible);
        benchmarkShift = null;
        peek.HideAndReset();
    }

    void SettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AppSettings.NoResourcesSelected) or nameof(AppSettings.CriticalDescription))
            return;
        if (persistSettings)
        {
            savePending = true;
            saveTimer.Stop();
            saveTimer.Start();
        }
        if (args.PropertyName is nameof(AppSettings.MonitorCpu) or nameof(AppSettings.MonitorGpu) or nameof(AppSettings.MonitorMemory))
        {
            metrics?.Dispose();
            metrics = null;
            latest = null;
            loadState.Update(null, Environment.TickCount64);
        }
        if (thresholds != settings.Thresholds)
        {
            thresholds = settings.Thresholds;
            loadState.Update(null, Environment.TickCount64);
        }
        if (!settings.ShiftDetails)
            peek.HideAndReset();
        peek.ApplyTheme(settings.Theme);
        UpdateSuppression();
        Render();
    }

    void SaveSettings()
    {
        savePending = false;
        try { settings.Save(AppSettings.FilePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error);
            MessageBox.Show($"Settings couldn't be saved. Changes will last until you exit.\n\n{error.Message}",
                "PulseCursor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void UpdateSuppression()
    {
        suppression = RuntimeState.PauseReason(settings, pause.Checked,
            settings.PauseInFullscreen && RuntimeState.IsFullscreen(),
            settings.PauseInEnergySaver && RuntimeState.IsEnergySaver());
        frames.Interval = TimeSpan.FromMilliseconds(suppression == null ? 33 : 250);
        if (suppression != null)
        {
            metrics?.Dispose();
            metrics = null;
            latest = null;
            peek.HideAndReset();
            loadState.Update(null, Environment.TickCount64);
            cursor.Restore();
            state.Text = suppression;
            icon.Text = $"PulseCursor - {suppression}";
        }
    }

    void Tick()
    {
        UpdateSuppression();
        if (suppression != null)
            return;
        if (metrics == null)
        {
            metrics = new Metrics(settings);
            state.Text = "Measuring...";
            return;
        }
        var sample = metrics.Read();
        bool first = latest == null;
        latest = sample;
        cpu.Text = $"CPU: {PeekModel.Format(sample.Cpu, settings.MonitorCpu)}";
        gpu.Text = $"GPU: {PeekModel.Format(sample.Gpu, settings.MonitorGpu)}";
        ram.Text = $"Memory: {PeekModel.Format(sample.Ram, settings.MonitorMemory)}";
        if (sample.GpuError != gpuError)
        {
            gpuError = sample.GpuError;
            if (gpuError != null)
            {
                Console.Error.WriteLine($"GPU unavailable: {gpuError}");
                icon.ShowBalloonTip(5000, "GPU usage unavailable", "Other selected resources are still monitored.", ToolTipIcon.Warning);
            }
        }
        Render();
        icon.Text = $"PulseCursor | CPU {PeekModel.Format(sample.Cpu, settings.MonitorCpu)} | GPU {PeekModel.Format(sample.Gpu, settings.MonitorGpu)} | RAM {PeekModel.Format(sample.Ram, settings.MonitorMemory)}";
        if (first)
            Console.WriteLine($"Ready. {icon.Text}");
        if (peek.IsVisible)
            peek.RefreshSample(sample, level, settings);
    }

    void Render()
    {
        double? selected = latest?.Select(LoadSource.Highest);
        level = loadState.Update(selected, Environment.TickCount64, settings);
        if (suppression != null)
        {
            cursor.Restore();
            state.Text = suppression;
            return;
        }
        if (SystemInformation.HighContrast || !selected.HasValue) cursor.Restore();
        else cursor.Apply(level, settings.CursorColor(level), settings.PulseOnCritical);
        state.Text = selected.HasValue ? LoadState.TextFor(level) : "Measuring...";
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Stop();
        frames.Stop();
        saveTimer.Stop();
        settings.PropertyChanged -= SettingsChanged;
        try
        {
            if (savePending) SaveSettings();
            settingsWindow?.Dispose();
            peek.Dispose();
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
            metrics?.Dispose();
        }
        finally { cursor.Dispose(); }
    }
}
