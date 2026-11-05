using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LoadCursor;

readonly record struct GpuUsage(int ProcessId, string Engine, double Percent);

readonly record struct Sample(double? Cpu, double? Gpu, double? Ram, string? GpuError)
{
    internal LoadSource Resolve(LoadSource source) => source != LoadSource.Highest ? source :
        Gpu is double gpu && gpu >= (Cpu ?? -1) && gpu >= (Ram ?? -1) ? LoadSource.Gpu :
        Cpu.HasValue && Cpu >= (Ram ?? -1) ? LoadSource.Cpu : LoadSource.Ram;

    internal double? Select(LoadSource source) => source switch
    {
        LoadSource.Cpu => Cpu,
        LoadSource.Gpu => Gpu,
        LoadSource.Ram => Ram,
        _ => new[] { Cpu, Gpu, Ram }.Max()
    };
}

sealed class Metrics : IDisposable
{
    const uint MoreData = 0x800007D2;
    const uint FormatDouble = 0x200;
    nint query;
    nint cpu;
    nint gpu;
    string? gpuSetupError;
    readonly bool monitorCpu, monitorGpu, monitorMemory;
    internal List<GpuUsage> GpuActivity { get; } = new();

    internal Metrics(AppSettings? settings = null)
    {
        settings ??= new AppSettings();
        monitorCpu = settings.MonitorCpu;
        monitorGpu = settings.MonitorGpu;
        monitorMemory = settings.MonitorMemory;
        if (settings.NoResourcesSelected)
            throw new InvalidOperationException("Select a resource before sampling.");
        Check(PdhOpenQueryW(null, 0, out query), "Open performance query");
        try
        {
            if (monitorCpu)
                Check(PdhAddEnglishCounterW(query, @"\Processor Information(_Total)\% Processor Time", 0, out cpu), "Add CPU counter");
            if (monitorGpu)
            {
                uint result = PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out gpu);
                if (result != 0)
                    gpuSetupError = $"Counter unavailable: 0x{result:X8}";
            }
            if (cpu != 0 || gpu != 0)
                Check(PdhCollectQueryData(query), "Prime performance query");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal Sample Read()
    {
        if (cpu != 0 || gpu != 0)
            Check(PdhCollectQueryData(query), "Collect performance data");
        double? cpuPercent = null;
        if (monitorCpu)
        {
            Check(PdhGetFormattedCounterValue(cpu, FormatDouble, out _, out var cpuValue), "Read CPU");
            CheckValue(cpuValue, "CPU");
            cpuPercent = Math.Clamp(cpuValue.Value, 0, 100);
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (monitorMemory && !GlobalMemoryStatusEx(ref memory))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read RAM");
        (double? gpuValue, string? gpuError) = monitorGpu ? ReadGpu() : (null, null);
        return new Sample(cpuPercent, gpuValue, monitorMemory ? memory.Load : null, gpuError);
    }

    (double? Value, string? Error) ReadGpu()
    {
        GpuActivity.Clear();
        if (gpuSetupError != null)
            return (null, gpuSetupError);
        // A fresh size probe is required on every retry; process instances come and go.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0;
            uint result = PdhGetFormattedCounterArrayW(gpu, FormatDouble, ref size, out _, 0);
            if (result != MoreData)
                return (null, $"No samples: 0x{result:X8}");
            nint buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                result = PdhGetFormattedCounterArrayW(gpu, FormatDouble, ref size, out uint count, buffer);
                if (result == MoreData)
                    continue;
                if (result != 0)
                    return (null, $"Read failed: 0x{result:X8}");
                var engines = new List<(string Name, double Value)>();
                int stride = Marshal.SizeOf<CounterItem>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(buffer + i * stride);
                    // New/exiting processes can lack the two samples needed for a rate.
                    if (item.Value.Status > 1 || !double.IsFinite(item.Value.Value))
                        continue;
                    string name = Marshal.PtrToStringUni(item.Name) ?? "";
                    engines.Add((name, item.Value.Value));
                }
                GpuActivity.AddRange(ParseGpuSamples(engines));
                double? busiest = BusiestEngine(GpuActivity);
                return busiest.HasValue ? (busiest, null) : (null, "Waiting for engine samples");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return (null, "Engine list changed; retrying next second");
    }

    internal static IEnumerable<GpuUsage> ParseGpuSamples(IEnumerable<(string Name, double Value)> samples)
    {
        foreach (var (name, value) in samples)
        {
            int separator = name.IndexOf("_luid_", StringComparison.Ordinal);
            if (separator <= 4 || !name.StartsWith("pid_", StringComparison.Ordinal) ||
                !double.IsFinite(value) || value < 0 ||
                !int.TryParse(name.AsSpan(4, separator - 4), out int pid) || pid < 0)
                continue;
            yield return new(pid, name[(separator + 1)..], value);
        }
    }

    internal static double? BusiestEngine(IEnumerable<GpuUsage> samples)
    {
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sample in samples)
            totals[sample.Engine] = totals.GetValueOrDefault(sample.Engine) + sample.Percent;
        if (totals.Count == 0)
            return null;
        var busiest = totals.MaxBy(pair => pair.Value);
        // Sum processes on the SAME adapter/engine, never independent GPU engines.
        return Math.Clamp(busiest.Value, 0, 100);
    }

    static void Check(uint status, string action)
    {
        if (status != 0)
            throw new InvalidOperationException($"{action}: PDH 0x{status:X8}");
    }

    static void CheckValue(CounterValue value, string name)
    {
        if (value.Status > 1 || !double.IsFinite(value.Value))
            throw new InvalidOperationException($"{name}: invalid sample 0x{value.Status:X8}");
    }

    public void Dispose()
    {
        if (query != 0)
        {
            uint status = PdhCloseQuery(query);
            query = 0;
            Check(status, "Close performance query");
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CounterItem
    {
        public nint Name;
        public CounterValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile;
        public ulong TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern uint PdhOpenQueryW(string? source, nuint user, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern uint PdhAddEnglishCounterW(nint query, string path, nuint user, out nint counter);
    [DllImport("pdh.dll")]
    static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")]
    static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint items);
    [DllImport("pdh.dll")]
    static extern uint PdhCloseQuery(nint query);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
