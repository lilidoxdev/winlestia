// C# 12 / BrightnessPolicy.cs / Pure rules for per-display brightness: DDC/CI range conversion, write rate limit, read failure budget. No I/O.
using System;

namespace PanneauxGlass;

internal static class BrightnessPolicy
{
    internal const int WriteIntervalMs = 120; // minimum spacing between two DDC/CI writes on one display; the latest requested value wins
    internal const int MaxFailures = 3; // consecutive failed reads before a display is marked unavailable until the next opening
    internal const int RefreshSeconds = 30; // re-read period while the panel is open (physical buttons raise no signal)

    // Percentage 0-100 (clamped) to the monitor's own raw range [min, max].
    internal static uint ToRaw(double percent, uint min, uint max)
    {
        if (max <= min)
            return min;
        double p = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
        return min + (uint)Math.Round(p * (max - min) / 100d);
    }

    // Raw value to percentage 0-100; a degenerate range (max <= min) gives 0.
    internal static double ToPercent(uint raw, uint min, uint max)
    {
        if (max <= min)
            return 0;
        return Math.Clamp((raw - (double)min) * 100d / (max - min), 0, 100);
    }

    // True when the raw value read back is the one the requested percentage was written as (coarse ranges: keep the requested value shown).
    internal static bool Matches(double requested, uint raw, uint min, uint max) => raw == ToRaw(requested, min, max);

    // Milliseconds still to wait before the next write may start (0 = now).
    internal static int RemainingDelay(long lastWriteMs, long nowMs) =>
        (int)Math.Clamp(WriteIntervalMs - (nowMs - lastWriteMs), 0, WriteIntervalMs);

    internal static bool Unavailable(int failures) => failures >= MaxFailures;

    // A read never overlaps a write on the same display.
    internal static bool CanRead(bool writing, bool writePending, bool unavailable) =>
        !writing && !writePending && !unavailable;

    // Display ids come from the GDI name "\\.\DISPLAYn"; 0 when the name has no trailing number.
    internal static int IdFromDevice(string? device)
    {
        if (string.IsNullOrEmpty(device))
            return 0;
        int end = device.Length,
            start = end;
        while (start > 0 && char.IsAsciiDigit(device[start - 1]))
            start--;
        return start < end && end - start <= 3 && int.TryParse(device.AsSpan(start), out int id) ? id : 0;
    }
}
