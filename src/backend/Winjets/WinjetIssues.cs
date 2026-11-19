// language: C# 12, file: WinjetIssues.cs, target: Windows 11 / .NET 8, collecte des erreurs de validation (messages français avec chemin
// JSON)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

// Un jeu d'erreurs partagé par toutes les étapes de la validation d'un paquet. Chaque lecteur travaille pour un fichier donné
// (manifest.json, widget.json…) : `Scope` donne le préfixe des messages.
internal sealed class WinjetIssues
{
    internal const int Cap = 40;
    readonly List<string> list;
    internal string Scope;

    internal WinjetIssues(List<string>? list = null, string scope = "")
    {
        this.list = list ?? new List<string>();
        Scope = scope;
    }

    internal IReadOnlyList<string> All => list;
    internal bool Any => list.Count > 0;
    internal int Count => list.Count;

    internal void Add(string path, string message)
    {
        if (list.Count == Cap)
            list.Add("Trop d'erreurs : la liste est tronquée.");
        if (list.Count >= Cap + 1)
            return;
        string where = Scope.Length > 0 ? (path.Length > 0 ? Scope + " " + path : Scope) : path;
        list.Add(where.Length > 0 ? where + " : " + message : message);
    }

    // Parse un fichier JSON avec les limites du format (taille, profondeur) ; null + erreur si illisible.
    internal JsonElement? ReadJson(string file, string name)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists)
            {
                Add("", name + " est obligatoire.");
                return null;
            }
            if (info.Length > WinjetLimits.MaxJsonBytes)
            {
                Add("", "fichier trop gros (" + info.Length / 1024 + " Kio, maximum " + WinjetLimits.MaxJsonBytes / 1024 + " Kio).");
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions
            {
                MaxDepth = WinjetLimits.MaxJsonDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            Add("", "JSON invalide ou trop profond (" + ex.Message.Split('.')[0] + ").");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Add("", "lecture impossible (" + ex.Message + ").");
            return null;
        }
    }

    // Format v1 : une propriété inconnue est une erreur.
    internal void Only(JsonElement obj, string path, params string[] allowed)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return;
        foreach (var p in obj.EnumerateObject())
            if (!allowed.Contains(p.Name, StringComparer.Ordinal))
                Add(path + "." + p.Name, "propriété inconnue (permises : " + string.Join(", ", allowed) + ").");
    }

    // Lecteurs typés : ils signalent l'erreur et renvoient null, l'appelant ne revérifie rien.
    internal string? Str(JsonElement obj, string name, string path, bool required, int max = 200, bool allowEmpty = false)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                Add(path + "." + name, "champ obligatoire.");
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            Add(path + "." + name, "doit être un texte.");
            return null;
        }
        string s = v.GetString() ?? "";
        if (s.Length == 0 && !allowEmpty)
        {
            Add(path + "." + name, "ne doit pas être vide.");
            return null;
        }
        if (s.Length > max)
        {
            Add(path + "." + name, "trop long (" + s.Length + " caractères, maximum " + max + ").");
            return null;
        }
        return s;
    }

    internal double? Num(JsonElement obj, string name, string path, bool required)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                Add(path + "." + name, "champ obligatoire.");
            return null;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || !double.IsFinite(d))
        {
            Add(path + "." + name, "doit être un nombre.");
            return null;
        }
        return d;
    }

    internal bool? Bool(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            Add(path + "." + name, "doit être vrai ou faux.");
            return null;
        }
        return v.GetBoolean();
    }

    internal JsonElement? Obj(JsonElement obj, string name, string path, bool required)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                Add(path + "." + name, "champ obligatoire.");
            return null;
        }
        if (v.ValueKind != JsonValueKind.Object)
        {
            Add(path + "." + name, "doit être un objet.");
            return null;
        }
        return v;
    }

    internal JsonElement? Arr(JsonElement obj, string name, string path, bool required, int max)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                Add(path + "." + name, "champ obligatoire.");
            return null;
        }
        if (v.ValueKind != JsonValueKind.Array)
        {
            Add(path + "." + name, "doit être un tableau.");
            return null;
        }
        if (v.GetArrayLength() > max)
        {
            Add(path + "." + name, "trop d'éléments (" + v.GetArrayLength() + ", maximum " + max + ").");
            return null;
        }
        return v;
    }
}
