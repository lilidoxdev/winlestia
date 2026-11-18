// language: C# 12, file: PeripheralLogic.cs, target: Windows 11 / .NET 8, batterie des périphériques : logique pure (détection du genre,
// fusion, filtrage)
using System;
using System.Collections.Generic;
using System.Linq;
namespace PanneauxGlass;

// Un périphérique vu par un scan. Percent null = batterie inconnue ; DetectedKind ignore la préférence utilisateur.
internal sealed record PeripheralCandidate(string Id, string Name, string Source, string DetectedKind, bool Connected, int? Percent,
    bool? Charging = null, bool Approximate = false);

internal sealed record PeripheralDevice(string Id, string Name, string Kind, int Percent, bool? Charging, bool Approximate, bool Hidden,
    string Source);

internal sealed record PeripheralPaired(string Id, string Name, string Kind, string DetectedKind, bool Overridden, bool Connected,
    int? Percent);

internal sealed record PeripheralSnapshot(IReadOnlyList<PeripheralDevice> Devices, IReadOnlyList<PeripheralPaired> Paired)
{
    internal static readonly PeripheralSnapshot Empty = new(Array.Empty<PeripheralDevice>(), Array.Empty<PeripheralPaired>());
}

internal static class PeripheralLogic
{
    internal static readonly string[] Kinds = { "headset", "gamepad", "mouse", "keyboard", "other" };

    internal static bool IsKind(string? kind) => kind != null && Array.IndexOf(Kinds, kind) >= 0;

    // Classe de périphérique Bluetooth (CoD) : majeure 4 = audio, 5 = périphérique d'interface (HID).
    internal static string? KindFromClassOfDevice(uint cod)
    {
        uint major = (cod >> 8) & 0x1F, minor = (cod >> 2) & 0x3F;
        if (major == 4)
            return minor is 1 or 2 or 6 ? "headset" : null;
        if (major == 5)
        {
            return ((cod >> 6) & 3) switch
            {
                1 or 3 => "keyboard",
                2 => "mouse",
                _ => (minor & 0xF) is 1 or 2 ? "gamepad" : null
            };
        }
        return null;
    }

    // Apparence GATT : catégorie = valeur >> 6, sous-catégorie = 6 bits bas.
    internal static string? KindFromAppearance(ushort appearance) => appearance switch
    {
        0x03C1 => "keyboard",
        0x03C2 => "mouse",
        0x03C3 or 0x03C4 => "gamepad",
        _ => (appearance >> 6) == 0x25 ? "headset" : null   // 0x0941… : appareil audio portable (écouteurs, casque)
    };

    internal static string? KindFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string n = name.ToLowerInvariant();
        if (Has(n, "headset", "headphone", "earbud", "buds", "airpods", "casque", "écouteur", "ecouteur"))
            return "headset";
        if (Has(n, "controller", "gamepad", "xbox", "dualsense", "dualshock", "manette"))
            return "gamepad";
        if (Has(n, "mouse", "souris"))
            return "mouse";
        if (Has(n, "keyboard", "clavier"))
            return "keyboard";
        return null;
    }

    static bool Has(string text, params string[] words) => words.Any(text.Contains);

    // Détection hors préférence : CoD > apparence > nom > « other ».
    internal static string DetectKind(uint? cod, ushort? appearance, string? name)
        => (cod is uint c ? KindFromClassOfDevice(c) : null)
           ?? (appearance is ushort a ? KindFromAppearance(a) : null)
           ?? KindFromName(name)
           ?? "other";

    // Préférence utilisateur > détection.
    internal static string ResolveKind(string? userOverride, string detected) => IsKind(userOverride) ? userOverride! : detected;

    // XInput : types 0 déconnecté, 1 filaire, 0xFF inconnu → ignorés. Niveaux 0..3 → 5/25/60/100.
    internal static int? MapXInputLevel(byte batteryType, byte level)
    {
        if (batteryType is 0 or 1 or 0xFF)
            return null;
        return level switch { 0 => 5, 1 => 25, 2 => 60, 3 => 100, _ => null };
    }

    static bool LooksXbox(string name) => name.Contains("xbox", StringComparison.OrdinalIgnoreCase);

    // Un contrôleur Xbox déjà vu en XInput ne doit pas apparaître une seconde fois via Bluetooth : le Bluetooth gagne s'il porte
    // un pourcentage, sinon il reprend celui de XInput (seule source de batterie pour ces manettes) en gardant son identifiant.
    internal static List<PeripheralCandidate> Merge(IEnumerable<PeripheralCandidate> bluetooth, IEnumerable<PeripheralCandidate> xinput)
    {
        var bt = bluetooth.ToList();
        var pads = xinput.ToList();
        var result = new List<PeripheralCandidate>();
        var usedPads = new HashSet<string>();
        foreach (var b in bt)
        {
            bool xboxLike = b.DetectedKind == "gamepad" && LooksXbox(b.Name) && b.Connected;
            var pad = xboxLike ? pads.FirstOrDefault(p => !usedPads.Contains(p.Id)) : null;
            if (pad != null)
            {
                usedPads.Add(pad.Id);
                // L'identifiant bt: reste toujours : masquage et genre choisis par l'utilisateur doivent survivre.
                if (b.Percent == null)
                {
                    result.Add(b with { Percent = pad.Percent, Approximate = pad.Approximate });
                    continue;
                }
            }
            result.Add(b);
        }
        result.AddRange(pads.Where(p => !usedPads.Contains(p.Id)));
        return result;
    }

    // Batterie de l'ordinateur : percent < 0 = pas de batterie (bureau). Nom vide : l'interface le traduit. Jamais surchargeable.
    internal static PeripheralCandidate? SystemBattery(int percent, bool charging)
        => percent is >= 0 and <= 100 ? new PeripheralCandidate("system:battery", "", "system", "laptop", true, percent, charging) : null;

    static int Rank(string kind) => kind == "laptop" ? Kinds.Length : Math.Max(0, Array.IndexOf(Kinds, kind));

    // Filtrage : « devices » = connecté ET batterie connue (les masqués restent, marqués) ; « paired » = tout.
    internal static PeripheralSnapshot Build(IEnumerable<PeripheralCandidate> candidates, ISet<string>? hidden,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var list = candidates.GroupBy(c => c.Id).Select(g => g.First()).ToList();
        string KindOf(PeripheralCandidate c)
            => ResolveKind(c.DetectedKind != "laptop" && overrides != null && overrides.TryGetValue(c.Id, out var o) ? o
                : null, c.DetectedKind);
        var devices = list
            .Where(c => c.Connected && c.Percent is >= 0 and <= 100)
            .Select(c => new PeripheralDevice(c.Id, c.Name, KindOf(c), c.Percent!.Value, c.Charging, c.Approximate,
                hidden != null && hidden.Contains(c.Id), c.Source))
            .OrderBy(d => Rank(d.Kind)).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        var paired = list
            .Select(c => new PeripheralPaired(c.Id, c.Name, KindOf(c), c.DetectedKind,
                c.DetectedKind != "laptop" && overrides != null && overrides.TryGetValue(c.Id, out var o)
                    && IsKind(o), c.Connected, c.Percent))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
        return new PeripheralSnapshot(devices, paired);
    }
}
