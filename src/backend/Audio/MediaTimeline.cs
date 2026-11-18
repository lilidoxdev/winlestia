// language: C# 12, file: MediaTimeline.cs, target: Windows 11 / .NET 8
// Calculs purs de position/durée pour les sessions média, sans types WinRT afin de rester testables en unitaire.
using System;

namespace PanneauxGlass;

internal static class MediaTimeline
{
    /// <summary>Une durée n'est connue que lorsque EndTime est strictement postérieur à StartTime.</summary>
    internal static bool IsDurationKnown(TimeSpan startTime, TimeSpan endTime)
    {
        return endTime > startTime;
    }

    /// <summary>
    /// Position en secondes à l'instant présent. En lecture, la position rapportée est avancée du temps écoulé depuis
    /// LastUpdatedTime multiplié par la vitesse de lecture. Le résultat est borné à [0, durée] quand la durée
    /// est connue, et seulement minoré à 0 sinon.
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

    /// <summary>Vrai quand une session doit être (ré)abonnée : tout changement d'identité d'objet, pas d'AUMID.</summary>
    internal static bool NeedsResubscribe(object? current, object? next)
    {
        return !ReferenceEquals(current, next);
    }
}
