// Préférences persistantes de Files : <dossier de données>\settings.json, dossier de données = %LOCALAPPDATA%\Winlestia\Files sauf si
// WINLESTIA_FILES_DATA_DIR le redirige (tests).
// Écritures atomiques (.tmp puis remplacement). Un fichier illisible est renommé settings.<horodatage>.bak et les valeurs par défaut
// s'appliquent ; un fichier verrouillé donne des valeurs par défaut en lecture seule (pas de Save). Save fusionne les clés modifiées dans
// la copie disque sous un mutex nommé.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace WinlestiaFiles;

internal sealed class QuickEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label
    {
        get; set;
    }
}

internal sealed class SortData
{
    [JsonPropertyName("key")] public string Key { get; set; } = "modified";
    [JsonPropertyName("descending")] public bool Descending { get; set; } = true;
    [JsonPropertyName("foldersFirst")]
    public bool FoldersFirst
    {
        get; set;
    }
}

internal sealed class SettingsData
{
    [JsonPropertyName("quickAccess"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<QuickEntry>? QuickAccess
    {
        get; set;
    }
    [JsonPropertyName("sort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SortData? Sort
    {
        get; set;
    }
    [JsonPropertyName("view"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? View
    {
        get; set;
    }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra
    {
        get; set;
    }
}

internal static class FilesSettings
{
    static readonly object gate = new();
    static SettingsData? data;
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static string Dir => Environment.GetEnvironmentVariable("WINLESTIA_FILES_DATA_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia", "Files");
    internal static string FilePath => Path.Combine(Dir, "settings.json");
    // Vrai quand la dernière lecture a trouvé un fichier illisible et l'a mis de côté.
    internal static bool RecoveredFromCorrupt;

    internal static SettingsData Data
    {
        get
        {
            lock (gate)
                return data ??= Read();
        }
    }
    internal static void Reload()
    {
        lock (gate)
            data = null;
    }

    // Vrai quand le fichier n'a pas pu être lu (verrouillé...) : valeurs par défaut en mémoire, et Save() est désactivé pour ne jamais
    // écraser un fichier valide.
    static bool readOnly;
    internal static bool ReadOnly => readOnly;
    // Valeur sérialisée de chaque clé telle que lue/écrite en dernier par cette instance ; une clé dont la valeur actuelle diffère est
    // "modifiée" et l'emporte à la fusion.
    static Dictionary<string, string> baseline = new();
    static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    static Dictionary<string, string> Snapshot(SettingsData d) => new()
    {
        ["quickAccess"] = JsonSerializer.Serialize(d.QuickAccess),
        ["sort"] = JsonSerializer.Serialize(d.Sort),
        ["view"] = JsonSerializer.Serialize(d.View)
    };

    static SettingsData Parse(string text)
    {
        var parsed = JsonSerializer.Deserialize<SettingsData>(text, ReadOptions) ?? new SettingsData();
        parsed.QuickAccess = parsed.QuickAccess?.Where(q => q != null && !string.IsNullOrWhiteSpace(q.Path)).ToList();
        return parsed;
    }

    static SettingsData Read()
    {
        RecoveredFromCorrupt = false;
        readOnly = false;
        string file = FilePath;
        SettingsData result = new();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(file))
                    break;
                result = Parse(File.ReadAllText(file));
                break;
            }
            catch (JsonException ex)
            {
                RecoveredFromCorrupt = true;
                Perf.Log("settings.json unreadable (" + ex.GetType().Name + "), set aside as .bak");
                try
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss"), bak = Path.Combine(Dir, "settings." + stamp + ".bak");
                    for (int i = 1; File.Exists(bak); i++)
                        bak = Path.Combine(Dir, "settings." + stamp + "-" + i + ".bak");
                    File.Move(file, bak, false);
                }
                catch { readOnly = true; }
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < 2)
                {
                    Thread.Sleep(50);
                    continue;
                }
                Perf.Log("settings.json locked (" + ex.GetType().Name + "), read-only session");
                readOnly = true;
            }
            catch (Exception ex)
            {
                Perf.Log("settings.json read: " + ex.Message);
                readOnly = true;
                break;
            }
        }
        baseline = Snapshot(result);
        return result;
    }

    internal static void Save()
    {
        try
        {
            lock (gate)
            {
                if (readOnly)
                    return;
                Directory.CreateDirectory(Dir);
                string file = FilePath, tmp = file + "." + Environment.ProcessId + ".tmp";
                var mine = data ??= new SettingsData();
                using var mutex = new Mutex(false, "Winlestia.Files.Settings."
                    + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(
                    Dir).TrimEnd((char)92).ToLowerInvariant())))[..24]);
                bool held = false;
                try
                {
                    held = mutex.WaitOne(5000);
                }
                catch (AbandonedMutexException) { held = true; }
                try
                {
                    SettingsData merged = mine;
                    if (File.Exists(file))
                    {
                        try
                        {
                            var disk = Parse(File.ReadAllText(file));
                            var now = Snapshot(mine);
                            bool Dirty(string k) => now[k] != baseline.GetValueOrDefault(k);
                            if (Dirty("quickAccess"))
                                disk.QuickAccess = mine.QuickAccess;
                            if (Dirty("sort"))
                                disk.Sort = mine.Sort;
                            if (Dirty("view"))
                                disk.View = mine.View;
                            merged = disk;
                        }
                        catch (JsonException) { merged = mine; }
                    }
                    File.WriteAllText(tmp, JsonSerializer.Serialize(merged, Options));
                    if (File.Exists(file))
                        File.Replace(tmp, file, null);
                    else
                        File.Move(tmp, file);
                    data = merged;
                    baseline = Snapshot(merged);
                }
                finally { Safe.Try(() => { if (File.Exists(tmp)) File.Delete(tmp); }); if (held) mutex.ReleaseMutex(); }
            }
        }
        catch (Exception ex) { Perf.Log("settings save: " + ex.Message); }
    }

    // ---- sort ----------------------------------------------------------------------------------------------------

    static readonly string[] Keys = { "name", "modified", "type", "size" };

    // Pas de clé "sort" = profil neuf : date de modification, la plus récente d'abord, dossiers et fichiers mélangés.
    internal static SortSpec InitialSort()
    {
        var s = Data.Sort;
        if (s == null)
            return SortSpec.Default;
        int i = Array.IndexOf(Keys, (s.Key ?? "").ToLowerInvariant());
        return i < 0 ? SortSpec.Default : new SortSpec((SortColumn)i, !s.Descending, s.FoldersFirst);
    }

    internal static void SaveSort(SortSpec spec)
    {
        Data.Sort = new SortData { Key = Keys[(int)spec.Column], Descending = !spec.Ascending, FoldersFirst = spec.FoldersFirst };
        Save();
    }

    // ---- view (list / details / medium / large) -------------------------------------------------------------------

    internal static string InitialView() => Data.View is { Length: > 0 } v ? v : "details";
    internal static void SaveView(string view)
    {
        Data.View = view;
        Save();
    }

    // "Rétablir le tri par défaut": drops the key so a later launch starts from the default again.
    internal static void ClearSort()
    {
        Data.Sort = null;
        Save();
    }
}
