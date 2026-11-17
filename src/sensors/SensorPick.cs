// C# 12, fichier SensorPick.cs, cible Windows 11 / .NET 8, choix pur du capteur de température du processeur (testable sans matériel)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinlestiaSensors;

internal static class SensorPick
{
    // Par ordre de préférence ; à défaut, le premier capteur plausible.
    internal static readonly string[] Priority = { "CPU Package", "Core (Tdie)", "Core (Tctl/Tdie)", "Core (Tctl)", "Core Max",
        "Core Average" };

    internal static double? Plausible(double? v) => v is > 10 and <= 125 && double.IsFinite(v.Value) ? v : null;

    internal static double? Choose(IEnumerable<(string name, double? value)> sensors)
    {
        var list = new List<(string name, double? value)>(sensors);
        foreach (string wanted in Priority)
            foreach (var (name, value) in list)
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase) && Plausible(value) is { } v)
                    return v;
        foreach (var (_, value) in list)
            if (Plausible(value) is { } v)
                return v;
        return null;
    }

    // Réponse du tube : une décimale, culture invariante, ou « null ».
    internal static string Format(double? v) => v is { } t ? Math.Round(t, 1).ToString("0.0", CultureInfo.InvariantCulture) : "null";
}
