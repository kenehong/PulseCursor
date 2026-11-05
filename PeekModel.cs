using System.ComponentModel;

namespace LoadCursor;

public sealed class PeekModel : INotifyPropertyChanged
{
    readonly TopProcess processes = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Header { get; private set; } = "Measuring...";
    public string CpuText { get; private set; } = "N/A";
    public string GpuText { get; private set; } = "N/A";
    public string MemoryText { get; private set; } = "N/A";
    public double CpuValue { get; private set; }
    public double GpuValue { get; private set; }
    public double MemoryValue { get; private set; }
    public bool CpuEnabled { get; private set; } = true;
    public bool GpuEnabled { get; private set; } = true;
    public bool MemoryEnabled { get; private set; } = true;
    public string HeaderState { get; private set; } = "NORMAL";
    public string CpuBars { get; private set; } = "";
    public string GpuBars { get; private set; } = "";
    public string MemoryBars { get; private set; } = "";
    public string CpuStatus { get; private set; } = "Normal";
    public string GpuStatus { get; private set; } = "Normal";
    public string MemoryStatus { get; private set; } = "Normal";
    public AppActivity CpuApp { get; } = new();
    public string GpuNote { get; private set; } = "";

    internal static string Format(double? value, bool enabled) => !enabled ? "Off" : value is double number ? $"{number:F0}%" : "N/A";

    internal void Update(Sample sample, LoadLevel level, AppSettings settings)
    {
        var source = sample.Resolve(LoadSource.Highest);
        if (sample.Select(LoadSource.Highest) == null)
            source = settings.MonitorCpu ? LoadSource.Cpu : settings.MonitorGpu ? LoadSource.Gpu : LoadSource.Ram;
        string name = source == LoadSource.Ram ? "Memory" : source.ToString().ToUpperInvariant();
        Header = sample.Select(source) == null ? $"{name} usage unavailable" :
            $"{LoadState.TextFor(level)} · {name} {Format(sample.Select(source), true)}";
        CpuEnabled = settings.MonitorCpu;
        GpuEnabled = settings.MonitorGpu;
        MemoryEnabled = settings.MonitorMemory;
        CpuText = Format(sample.Cpu, CpuEnabled);
        GpuText = Format(sample.Gpu, GpuEnabled);
        MemoryText = Format(sample.Ram, MemoryEnabled);
        CpuValue = sample.Cpu ?? 0;
        GpuValue = sample.Gpu ?? 0;
        MemoryValue = sample.Ram ?? 0;
        HeaderState = level.ToString().ToUpperInvariant();
        CpuBars = Bars(sample.Cpu);
        GpuBars = Bars(sample.Gpu);
        MemoryBars = Bars(sample.Ram);
        CpuStatus = Status(sample.Cpu, level, settings);
        GpuStatus = Status(sample.Gpu, level, settings);
        MemoryStatus = Status(sample.Ram, level, settings);
        GpuNote = sample.GpuError ?? "Busiest GPU engine";
        CpuApp.Update(processes.Read(CpuEnabled));
        Notify();
    }

    internal static string Bars(double? value)
    {
        int filled = value.HasValue ? (int)Math.Round(Math.Clamp(value.Value, 0, 100) / 10) : 0;
        return new string('\u2588', filled) + new string('\u2591', 10 - filled);
    }

    static string Status(double? value, LoadLevel level, AppSettings settings) =>
        value >= settings.CriticalThreshold && level == LoadLevel.Critical ? "Critical" :
        value >= settings.HotThreshold ? "Hot" : value >= settings.BusyThreshold ? "Busy" : "Normal";

    internal void Reset() => processes.Reset();
    void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
