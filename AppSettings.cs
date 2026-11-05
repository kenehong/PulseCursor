using System.ComponentModel;
using System.Text.Json;

namespace LoadCursor;

public sealed class AppSettings : INotifyPropertyChanged
{
    Data data = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool MonitorCpu { get => data.MonitorCpu; set => Change(data with { MonitorCpu = value }, nameof(MonitorCpu)); }
    public bool MonitorGpu { get => data.MonitorGpu; set => Change(data with { MonitorGpu = value }, nameof(MonitorGpu)); }
    public bool MonitorMemory { get => data.MonitorMemory; set => Change(data with { MonitorMemory = value }, nameof(MonitorMemory)); }
    public bool PulseOnCritical { get => data.PulseOnCritical; set => Change(data with { PulseOnCritical = value }); }
    public bool ShiftDetails { get => data.ShiftDetails; set => Change(data with { ShiftDetails = value }); }
    public bool PauseInFullscreen { get => data.PauseInFullscreen; set => Change(data with { PauseInFullscreen = value }); }
    public bool PauseInEnergySaver { get => data.PauseInEnergySaver; set => Change(data with { PauseInEnergySaver = value }); }
    public int Theme { get => data.Theme; set => Change(data with { Theme = value }); }
    public int Sensitivity
    {
        get => data.Sensitivity;
        set
        {
            var (busy, hot, critical) = value switch
            {
                0 => (50d, 70d, 90d),
                1 => (60d, 80d, 95d),
                2 => (70d, 88d, 97d),
                3 => Thresholds,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };
            Change(data with { Sensitivity = value, BusyThreshold = busy, HotThreshold = hot, CriticalThreshold = critical });
        }
    }
    public double BusyThreshold => data.BusyThreshold;
    public double HotThreshold => data.HotThreshold;
    public double CriticalThreshold => data.CriticalThreshold;
    public string NormalColor { get => data.NormalColor; set => Change(data with { NormalColor = value }); }
    public string BusyColor { get => data.BusyColor; set => Change(data with { BusyColor = value }); }
    public string HotColor { get => data.HotColor; set => Change(data with { HotColor = value }); }
    public string CriticalColor { get => data.CriticalColor; set => Change(data with { CriticalColor = value }); }
    public Windows.UI.Color NormalColorValue { get => ParseColor(NormalColor); set => NormalColor = Hex(value); }
    public Windows.UI.Color BusyColorValue { get => ParseColor(BusyColor); set => BusyColor = Hex(value); }
    public Windows.UI.Color HotColorValue { get => ParseColor(HotColor); set => HotColor = Hex(value); }
    public Windows.UI.Color CriticalColorValue { get => ParseColor(CriticalColor); set => CriticalColor = Hex(value); }
    public bool NoResourcesSelected => !MonitorCpu && !MonitorGpu && !MonitorMemory;
    public string CriticalDescription => $"Pulse after usage stays at {CriticalThreshold:0.#}% or higher for 10 seconds.";
    internal (double Busy, double Hot, double Critical) Thresholds => (BusyThreshold, HotThreshold, CriticalThreshold);

    internal void SetThresholds(double busy, double hot, double critical) => Change(data with
    {
        Sensitivity = 3,
        BusyThreshold = busy,
        HotThreshold = hot,
        CriticalThreshold = critical
    });

    internal System.Drawing.Color CursorColor(LoadLevel level) => ColorTranslator.FromHtml(level switch
    {
        LoadLevel.Busy => BusyColor,
        LoadLevel.Hot => HotColor,
        LoadLevel.Critical => CriticalColor,
        _ => NormalColor
    });

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoadCursor", "settings.json");

    internal static AppSettings Load(string path)
    {
        var result = new AppSettings();
        if (File.Exists(path))
        {
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? throw new JsonException($"Settings are empty: {path}");
            Validate(data);
            result.data = data;
        }
        return result;
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    void Change(Data next, string? property = null)
    {
        Validate(next);
        if (next == data)
            return;
        data = next;
        // A batch changes presets, thresholds, colors and their derived display properties together.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        if (property != null)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoResourcesSelected)));
    }

    static void Validate(Data value)
    {
        if (value.Sensitivity is < 0 or > 3 || value.Theme is < 0 or > 2)
            throw new ArgumentException("Choose a valid sensitivity and theme.");
        if (!double.IsFinite(value.BusyThreshold) || !double.IsFinite(value.HotThreshold) || !double.IsFinite(value.CriticalThreshold) ||
            value.BusyThreshold < 1 || value.BusyThreshold >= value.HotThreshold ||
            value.HotThreshold >= value.CriticalThreshold || value.CriticalThreshold > 100)
            throw new ArgumentException("Thresholds must increase: 1% <= Busy < High load < Critical <= 100%.");
        foreach (string color in new[] { value.NormalColor, value.BusyColor, value.HotColor, value.CriticalColor })
            ParseColor(color);
    }

    static Windows.UI.Color ParseColor(string hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#' ||
            !uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
            throw new ArgumentException("Use a color in #RRGGBB format.");
        return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public static string Hex(Windows.UI.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    sealed record Data
    {
        public bool MonitorCpu { get; init; } = true;
        public bool MonitorGpu { get; init; } = true;
        public bool MonitorMemory { get; init; } = true;
        public int Sensitivity { get; init; } = 1;
        public double BusyThreshold { get; init; } = 60;
        public double HotThreshold { get; init; } = 80;
        public double CriticalThreshold { get; init; } = 95;
        public string NormalColor { get; init; } = "#FFFFFF";
        public string BusyColor { get; init; } = "#F5B82E";
        public string HotColor { get; init; } = "#EE7A1F";
        public string CriticalColor { get; init; } = "#C42B1C";
        public bool PulseOnCritical { get; init; } = true;
        public bool ShiftDetails { get; init; } = true;
        public bool PauseInFullscreen { get; init; } = true;
        public bool PauseInEnergySaver { get; init; }
        public int Theme { get; init; }
    }
}
