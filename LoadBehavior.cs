namespace LoadCursor;

enum LoadLevel { Normal, Busy, Hot, Critical }
enum LoadSource { Highest, Cpu, Gpu, Ram }

sealed class LoadState
{
    long? criticalSince;

    internal LoadLevel Update(double? load, long milliseconds, AppSettings? settings = null)
    {
        if (load is null)
        {
            criticalSince = null;
            return LoadLevel.Normal;
        }
        if (!double.IsFinite(load.Value) || load is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(load));
        var (busy, hot, critical) = settings?.Thresholds ?? (60, 80, 95);
        if (load < critical)
            criticalSince = null;
        else
            criticalSince ??= milliseconds;
        if (criticalSince.HasValue && milliseconds - criticalSince.Value >= 10_000)
            return LoadLevel.Critical;
        return load >= hot ? LoadLevel.Hot : load >= busy ? LoadLevel.Busy : LoadLevel.Normal;
    }

    internal static string TextFor(LoadLevel level) => level switch
    {
        LoadLevel.Busy => "Busy",
        LoadLevel.Hot => "High load",
        LoadLevel.Critical => "Critical",
        _ => "Normal"
    };
}

sealed class HoldGesture
{
    long? pressedAt;
    bool dismissed;

    internal void Dismiss() => dismissed = true;

    internal bool Update(bool shift, bool escape, bool hovering, long milliseconds)
    {
        if (!shift)
        {
            pressedAt = null;
            dismissed = false;
        }
        if (escape)
            dismissed = true;
        if (dismissed)
            return false;
        if (!shift)
            return hovering;
        pressedAt ??= milliseconds;
        return milliseconds - pressedAt.Value >= 600;
    }
}
