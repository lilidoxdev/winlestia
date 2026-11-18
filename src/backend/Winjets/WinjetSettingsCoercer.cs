// language: C# 12, file: WinjetSettingsCoercer.cs, target: Windows 11 / .NET 8, réglages et état d'une instance validés contre widget.json
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

internal static class WinjetSettingsCoercer
{
    // JsonElement -> bool, double ou string ; tout le reste (objet, tableau, null) devient null.
    internal static object? Plain(object? value)
    {
        if (value is not JsonElement e)
            return value is bool or double or string ? value : value is int or long or float
                ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : null;
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetDouble(out double d) && double.IsFinite(d) ? d : null,
            JsonValueKind.String => e.GetString(),
            _ => null
        };
    }

    internal static Dictionary<string, object?> Defaults(WinjetDefinition def)
        => def.Settings.ToDictionary(s => s.Id, s => (object?)s.Default, StringComparer.Ordinal);

    // Valeur coercée selon le réglage, ou `fallback` si elle est inutilisable.
    internal static object Coerce(WinjetSetting setting, object? raw, object fallback)
    {
        object? v = Plain(raw);
        switch (setting.Type)
        {
            case "toggle":
                if (v is bool b)
                    return b;
                return v is string t && (t == "true" || t == "false") ? t == "true" : fallback;
            case "number":
                {
                    double? n = v is double d ? d : v is string s
                        && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : null;
                    return n is { } x && double.IsFinite(x) ? Math.Clamp(x, setting.Min, setting.Max) : fallback;
                }
            case "text":
                {
                    string? s = v as string ?? (v is double n2 ? n2.ToString(CultureInfo.InvariantCulture) : null);
                    if (s == null)
                        return fallback;
                    var sb = new StringBuilder();
                    foreach (char c in s)
                        if (!char.IsControl(c))
                            sb.Append(c);
                    return sb.Length > setting.MaxLength ? sb.ToString(0, setting.MaxLength) : sb.ToString();
                }
            case "choice":
                return v is string choice && setting.Options.Contains(choice) ? choice : fallback;
            default:
                return fallback;
        }
    }

    // Réglages d'entrée (objet JSON) -> dictionnaire complet : clés inconnues écartées, valeurs invalides = valeur actuelle.
    internal static Dictionary<string, object?> Settings(WinjetDefinition def, JsonElement input, IReadOnlyDictionary<string,
        object?>? current)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var s in def.Settings)
        {
            object fallback = current != null && current.TryGetValue(s.Id, out var cur) && cur != null ? Coerce(s, cur, s.Default)
                : s.Default;
            result[s.Id] = input.ValueKind == JsonValueKind.Object && input.TryGetProperty(s.Id, out var raw) ? Coerce(s, raw, fallback)
                : fallback;
        }
        return result;
    }

    // Même chose depuis un dictionnaire déjà chargé (relecture du fichier, mise à jour d'un paquet).
    internal static Dictionary<string, object?> Settings(WinjetDefinition def, IReadOnlyDictionary<string, object?> input)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var s in def.Settings)
            result[s.Id] = input.TryGetValue(s.Id, out var raw) ? Coerce(s, raw, s.Default) : s.Default;
        return result;
    }

    // État : seules les clés déclarées survivent ; le type est celui de la valeur initiale.
    internal static Dictionary<string, object?> State(WinjetDefinition def, IReadOnlyDictionary<string, object?> input)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, initial) in def.State)
            result[key] = input.TryGetValue(key, out var raw) && StateValue(initial, raw) is { } ok ? ok : initial;
        return result;
    }

    // Valeur d'état du même type que la valeur initiale, ou null si elle ne convient pas.
    internal static object? StateValue(object initial, object? raw)
    {
        object? v = Plain(raw);
        return initial switch
        {
            bool => v as bool?,
            double => v as double?,
            string => v is string s && s.Length <= WinjetLimits.MaxStateTextLength
                && !s.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ? s : null,
            _ => null
        };
    }
}
