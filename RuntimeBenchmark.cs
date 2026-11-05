using System.Diagnostics;
using System.Text.Json;

namespace LoadCursor;

static class RuntimeBenchmark
{
    internal static async Task MeasureAsync(string stage, Func<bool> isReady)
    {
        await Task.Delay(5000);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        double beforeCpu = process.TotalProcessorTime.TotalSeconds;
        long start = Stopwatch.GetTimestamp();
        var workingSet = new List<double>();
        var privateBytes = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(1000);
            if (!isReady())
                throw new InvalidOperationException($"Benchmark state changed or is not ready: {stage}");
            process.Refresh();
            workingSet.Add(process.WorkingSet64 / 1048576d);
            privateBytes.Add(process.PrivateMemorySize64 / 1048576d);
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
        double cpu = (process.TotalProcessorTime.TotalSeconds - beforeCpu) / elapsed / Environment.ProcessorCount * 100;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Stage = stage,
            Seconds = Math.Round(elapsed, 2),
            LogicalProcessors = Environment.ProcessorCount,
            CpuPercent = Math.Round(cpu, 3),
            WorkingSetMiB = Math.Round(workingSet.Average(), 2),
            WorkingSetMinMiB = Math.Round(workingSet.Min(), 2),
            WorkingSetMaxMiB = Math.Round(workingSet.Max(), 2),
            PrivateBytesMiB = Math.Round(privateBytes.Average(), 2),
            PrivateBytesMinMiB = Math.Round(privateBytes.Min(), 2),
            PrivateBytesMaxMiB = Math.Round(privateBytes.Max(), 2)
        }));
    }
}
