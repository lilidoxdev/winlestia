// C# 12 / BrightnessPolicy.cs / Règles pures de luminosité par écran : conversion de plage DDC/CI, limitation du débit d'écriture,
// budget d'échecs de lecture. Aucune E/S.
using System;

namespace PanneauxGlass;

internal static class BrightnessPolicy
{
    // écart minimal entre deux écritures DDC/CI sur un même écran ; la dernière valeur demandée l'emporte
    internal const int WriteIntervalMs = 120;
    // lectures échouées consécutives avant qu'un écran soit marqué indisponible jusqu'à la prochaine ouverture
    internal const int MaxFailures = 3;
    // période de relecture tant que le panneau est ouvert (les boutons physiques n'émettent aucun signal)
    internal const int RefreshSeconds = 30;

    // Pourcentage 0-100 (borné) vers la plage brute [min, max] propre au moniteur.
    internal static uint ToRaw(double percent, uint min, uint max)
    {
        if (max <= min)
            return min;
        double p = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
        return min + (uint)Math.Round(p * (max - min) / 100d);
    }

    // Valeur brute vers pourcentage 0-100 ; une plage dégénérée (max <= min) donne 0.
    internal static double ToPercent(uint raw, uint min, uint max)
    {
        if (max <= min)
            return 0;
        return Math.Clamp((raw - (double)min) * 100d / (max - min), 0, 100);
    }

    // Vrai quand la valeur brute relue est celle sous laquelle le pourcentage demandé a été écrit (plages grossières : on garde la
    // valeur demandée affichée).
    internal static bool Matches(double requested, uint raw, uint min, uint max) => raw == ToRaw(requested, min, max);

    // Millisecondes à attendre avant que la prochaine écriture puisse démarrer (0 = maintenant).
    internal static int RemainingDelay(long lastWriteMs, long nowMs) => (int)Math.Clamp(WriteIntervalMs - (nowMs - lastWriteMs), 0,
        WriteIntervalMs);

    internal static bool Unavailable(int failures) => failures >= MaxFailures;

    // Une lecture ne chevauche jamais une écriture sur le même écran.
    internal static bool CanRead(bool writing, bool writePending, bool unavailable) => !writing && !writePending && !unavailable;

    // Les identifiants d'écran viennent du nom GDI « \\.\DISPLAYn » ; 0 quand le nom n'a pas de numéro final.
    internal static int IdFromDevice(string? device)
    {
        if (string.IsNullOrEmpty(device))
            return 0;
        int end = device.Length, start = end;
        while (start > 0 && char.IsAsciiDigit(device[start - 1]))
            start--;
        return start < end && end - start <= 3 && int.TryParse(device.AsSpan(start), out int id) ? id : 0;
    }
}
