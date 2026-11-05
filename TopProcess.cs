using System.ComponentModel;
using System.Diagnostics;

namespace LoadCursor;

readonly record struct AppUsage(string Name, string Usage, string Note, string? IconPath);

sealed class TopProcess
{
    Dictionary<int, double> previousCpu = new();
    int lastSkipped = -1;
    long lastRead;

    internal AppUsage Read(bool enabled)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = lastRead == 0 ? 0 : Stopwatch.GetElapsedTime(lastRead, now).TotalMilliseconds;
        var currentCpu = new Dictionary<int, double>();
        var cpuApps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var appIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        if (!enabled)
        {
            Reset();
            return new("Off", "", "", null);
        }
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == 0)
                    continue;
                try
                {
                    string name = process.ProcessName;
                    appIds[name] = process.Id;
                    double cpu = process.TotalProcessorTime.TotalMilliseconds;
                    currentCpu[process.Id] = cpu;
                    if (previousCpu.TryGetValue(process.Id, out double before) && elapsed > 0)
                        cpuApps[name] = cpuApps.GetValueOrDefault(name) + CpuPercent(cpu - before, elapsed, Environment.ProcessorCount);
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException)
                {
                    // Protected/exiting processes are explicitly excluded, not reported as idle.
                    skipped++;
                }
            }
        }
        bool warmingUp = previousCpu.Count == 0;
        previousCpu = currentCpu;
        lastRead = now;
        if (skipped != lastSkipped)
        {
            if (skipped > 0)
                Console.Error.WriteLine($"App activity: {skipped} protected/exiting processes excluded.");
            lastSkipped = skipped;
        }
        string note = $"Grouped by executable name. {skipped} process reads unavailable.";
        return warmingUp ?
            new AppUsage("Measuring...", "", note, null) : Leader(cpuApps, appIds, "Highest CPU usage. " + note);
    }

    internal static double CpuPercent(double milliseconds, double elapsed, int processors) =>
        Math.Clamp(milliseconds / elapsed / processors * 100, 0, 100);

    static AppUsage Leader(Dictionary<string, double> apps, Dictionary<string, int> appIds, string note)
    {
        if (apps.Count == 0)
            return new("Unavailable", "", note, null);
        var top = apps.MaxBy(pair => pair.Value);
        if (top.Value <= 0)
            return new("No activity", "", note, null);
        string? path = null;
        try
        {
            using var process = Process.GetProcessById(appIds[top.Key]);
            path = process.MainModule?.FileName;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException)
        {
            note += $" Icon unavailable: {error.Message}";
        }
        string detail = top.Value < 1 ? "<1%" : $"{Math.Clamp(top.Value, 0, 100):F0}%";
        return new(top.Key, detail, note, path);
    }

    internal void Reset()
    {
        previousCpu.Clear();
        lastRead = 0;
    }
}
