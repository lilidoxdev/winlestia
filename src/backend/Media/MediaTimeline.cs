// language: C# 12, file: MediaTimeline.cs, target: Windows 11 / .NET 8
// Pure position/duration maths for media sessions, kept free of WinRT types so it can be unit tested.
using System;

namespace PanneauxGlass.Media;

internal static class MediaTimeline
{
    /// <summary>A duration is known only when EndTime is strictly after StartTime.</summary>
    internal static bool IsDurationKnown(TimeSpan startTime, TimeSpan endTime)
    {
        return endTime > startTime;
    }

    /// <summary>
    /// Position in seconds now. While playing, the reported position is advanced by the time elapsed since
    /// LastUpdatedTime multiplied by the playback rate. The result is clamped to [0, duration] when the duration
    /// is known, and only floored at 0 otherwise.
    /// </summary>
    internal static double ExtrapolatePosition(
        double reportedPosition,
        DateTimeOffset lastUpdated,
        DateTimeOffset now,
        double rate,
        bool playing,
        double duration
    )
    {
        double position = reportedPosition;
        if (playing && lastUpdated != default)
        {
            double elapsed = Math.Max(0, (now - lastUpdated).TotalSeconds);
            position += elapsed * (rate > 0 ? rate : 1);
        }

        if (double.IsNaN(position) || position < 0)
        {
            return 0;
        }

        return duration > 0 ? Math.Min(position, duration) : position;
    }

    /// <summary>True when a session must be (re)subscribed: any change of object identity, not of AUMID.</summary>
    internal static bool NeedsResubscribe(object? current, object? next)
    {
        return !ReferenceEquals(current, next);
    }
}
