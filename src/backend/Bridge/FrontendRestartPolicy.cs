// C# 12 / .NET 8. Politique de relance du frontend Qt après un arrêt inattendu : logique pure, sans processus ni horloge.
using System;
using System.Collections.Generic;

namespace PanneauxGlass;

// Chaque arrêt inattendu est enregistré ; la relance se fait après un délai croissant (1 s, 2 s, 4 s, 8 s).
// Au-delà de MaxRestarts arrêts dans la fenêtre glissante, on abandonne : le chemin de secours (barre + message) prend le relais.
internal sealed class FrontendRestartPolicy
{
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    internal const int MaxRestarts = 4;
    static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);

    readonly List<DateTime> crashes = new();

    // Vrai dès qu'un frontend s'est connecté au moins une fois : sans cela (modules Python absents, --qt-root erroné), relancer est vain.
    internal bool EverConnected
    {
        get; private set;
    }

    internal void MarkConnected() => EverConnected = true;

    // Enregistre un arrêt inattendu à `now`. Renvoie le délai avant relance, ou null s'il faut abandonner.
    internal TimeSpan? RecordCrash(DateTime now)
    {
        if (!EverConnected)
            return null;
        crashes.RemoveAll(t => now - t >= Window);
        crashes.Add(now);
        if (crashes.Count > MaxRestarts)
            return null;
        return TimeSpan.FromTicks(FirstDelay.Ticks << (crashes.Count - 1));
    }
}
