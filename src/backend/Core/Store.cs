// language: C# 12, file: Store.cs, target: Windows 11 / .NET 8, stockage local : chemins, état, journal et sérialisation
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Winlestia.Shared;
namespace PanneauxGlass;

public static class Store
{
    public static string Root = AppDomain.CurrentDomain.BaseDirectory;
    // Les données utilisateur (préférences, journal, presse-papiers, épingles) vivent hors du dossier d'installation
    // pour survivre à une désinstallation/réinstallation. WINLESTIA_DATA_DIR le remplace (tests).
    static string? dirEnv, dirCached;
    static bool dirInit;
    public static string DataDir
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");
            if (dirInit && dirEnv == env && dirCached != null)
                return dirCached;
            // Nom historique conservé pour compatibilité (dossier de données des installations existantes).
            string d = env is { Length: > 0 } o ? o : Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Panneaux");
            try
            {
                Directory.CreateDirectory(d);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Store.Log("Models.get : " + ex.GetType().Name + ": " + ex.Message);
            }
            dirEnv = env;
            dirCached = d;
            dirInit = true;
            return d;
        }
    }
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static Preferences State = new();
    public static List<Entry> Entries = new();
    public static string Fold(string? value)
    {
        var b = new StringBuilder();
        foreach (char c in (value ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                b.Append(c);
        return b.ToString();
    }
    static readonly object logLock = new();
    public static void Log(string text)
    {
        try
        {
            lock (logLock)
            {
                string f = Path.Combine(DataDir, "glass.log"), line = DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine;
                try
                {
                    File.AppendAllText(f, line, Encoding.UTF8);
                }
                catch (DirectoryNotFoundException)
                {
                    Directory.CreateDirectory(DataDir);
                    File.AppendAllText(f, line, Encoding.UTF8);
                }
            }
        }
        catch (Exception)
        {
            // Le journal ne peut pas se journaliser lui-même.
        }
    }
    public static T Read<T>(string path) where T : new()
    {
        if (!File.Exists(path))
            return new T();
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Transitoire (verrou, antivirus...) : le fichier n'est pas connu comme corrompu, on ne touche jamais au disque ici.
            System.Threading.Thread.Sleep(50);
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
            {
                Log(Path.GetFileName(path) + ": lecture impossible: " + ex2.Message);
                return new T();
            }
        }
        catch (Exception ex) { return Recover<T>(path, ex); }
        try { return JsonSerializer.Deserialize<T>(text, Json) ?? new T(); }
        catch (Exception ex) { return Recover<T>(path, ex); }
    }
    static bool InDataDir(string path)
    {
        string root = Path.GetFullPath(DataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    static T Recover<T>(string path, Exception ex) where T : new()
    {
        Log(Path.GetFileName(path) + ": " + ex.Message);
        KeepCorrupt(path);
        string bak = path + ".bak";
        if (InDataDir(path) && File.Exists(bak))
        {
            try
            {
                var recovered = JsonSerializer.Deserialize<T>(File.ReadAllText(bak, Encoding.UTF8), Json);
                if (recovered != null)
                {
                    Log(Path.GetFileName(path) + ": restauré depuis .bak");
                    // La bonne copie redevient le principal : sinon le prochain Save écraserait le .bak sain par le principal corrompu.
                    try { File.Copy(bak, path, true); }
                    catch (Exception ex2) { Log(Path.GetFileName(path) + ": restauration du principal impossible: " + ex2.Message); }
                    return recovered;
                }
            }
            catch (Exception ex2) { Log(Path.GetFileName(path) + ".bak: " + ex2.Message); }
        }
        Log(Path.GetFileName(path) + ": valeurs par défaut utilisées");
        return new T();
    }
    // Un fichier utilisateur illisible va être remplacé par les défauts au prochain Save : on garde ses octets (une copie, à part du
    // .bak glissant).
    static void KeepCorrupt(string path)
    {
        try
        {
            if (!InDataDir(path))
                return;
            File.Copy(path, path + ".corrupt.bak", true);
            Log(Path.GetFileName(path) + ": copie du fichier illisible conservée (.corrupt.bak)");
        }
        catch (Exception ex) { Log(Path.GetFileName(path) + ": copie .corrupt.bak impossible: " + ex.Message); }
    }
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string path = Path.Combine(DataDir, "preferences-glass.json");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(State, Json), Encoding.UTF8, path + ".bak");
        }
        catch (Exception ex) { Log("Enregistrement: " + ex.Message); }
    }
    // Migration unique depuis le dossier d'installation (ancien emplacement) : copie des préférences (les anciens binaires
    // continuent de marcher), déplacement des gros dossiers, jamais d'écrasement, jamais d'exception.
    public static void MigrateLegacyData(string legacyRoot, string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            string prefs = Path.Combine(dataDir, "preferences-glass.json"), old = Path.Combine(legacyRoot, "preferences-glass.json");
            if (!File.Exists(prefs) && File.Exists(old))
            {
                try
                {
                    File.Copy(old, prefs, false);
                }
                catch (Exception ex) { Log("Migration preferences-glass.json: " + ex.Message); }
            }
            try
            {
                string bak = Path.Combine(legacyRoot, "preferences-glass.json.bak"), bakTo = Path.Combine(dataDir,
                    "preferences-glass.json.bak");
                if (File.Exists(bak) && !File.Exists(bakTo))
                    File.Copy(bak, bakTo, false);
            }
            catch (Exception ex) { Log("Migration preferences-glass.json.bak: " + ex.Message); }
            foreach (string folder in new[] { "clipboard", "pins" })
            {
                string from = Path.Combine(legacyRoot, folder), to = Path.Combine(dataDir, folder), tmp = to + ".migrating";
                if (Directory.Exists(tmp) && !Directory.Exists(to))
                {
                    try
                    {
                        Directory.Delete(tmp, true);
                    }
                    catch (Exception ex) { Log("Migration " + folder + " (reliquat): " + ex.Message); }
                }
                if (!Directory.Exists(from) || Directory.Exists(to))
                    continue;
                try
                {
                    Directory.Move(from, to);
                }
                catch (Exception ex)
                {
                    Log("Migration " + folder + " (déplacement): " + ex.Message);
                    try
                    {
                        if (Directory.Exists(tmp))
                            Directory.Delete(tmp, true);
                        CopyTree(from, tmp);
                        Directory.Move(tmp, to);
                    }
                    catch (Exception ex2) { Log("Migration " + folder + " (copie): " + ex2.Message); }
                }
            }
        }
        catch (Exception ex) { try { Log("Migration: " + ex.Message); } catch (
            Exception) { /* Le journal ne peut pas se journaliser lui-même. */ } }
    }
    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from))
        {
            string t = Path.Combine(to, Path.GetFileName(f));
            if (!File.Exists(t))
                File.Copy(f, t, false);
        }
        foreach (string d in Directory.GetDirectories(from))
            CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }
    public static void Load()
    {
        MigrateLegacyData(Root, DataDir);
        string prefsFile = Path.Combine(DataDir, "preferences-glass.json");
        State = Read<Preferences>(prefsFile);
        if (!File.Exists(prefsFile))
        {
            string old = Path.GetFullPath(Path.Combine(Root, "..", "Workspace-v4", "preferences.json"));
            if (File.Exists(old))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(old));
                    if (doc.RootElement.TryGetProperty("Favorites", out var f))
                        State.Favorites = f.Deserialize<List<string>>() ?? new();
                }
                catch (Exception err) when (err is IOException or UnauthorizedAccessException or JsonException)
                {
                    Store.Log("Models.Load : " + err.GetType().Name + ": " + err.Message);
                }
            }
        }
        State.Normalize();
        MigrateSettings(State);
        if (State.GlassOpacityRevision < 1)
        {
            if (Math.Abs(State.GlassOpacity - .12) < 1e-9)
                State.GlassOpacity = .95;
            State.GlassOpacityRevision = 1;
        }
        LoadCatalogue();
    }
    // Schéma 2 (refonte des réglages) : le dock était affiché exactement quand ReplaceTaskbar était actif ; les deux sont
    // désormais indépendants, l'ancien état est donc copié une fois. Toute autre clé reste intacte.
    public const int SettingsSchemaCurrent = 2;
    public static void MigrateSettings(Preferences p)
    {
        if (p.SettingsSchema < SettingsSchemaCurrent)
        {
            p.DockVisible = p.ReplaceTaskbar;
            p.SettingsSchema = SettingsSchemaCurrent;
        }
    }
    public static void LoadCatalogue()
    {
        Entries = Read<Catalogue>(Path.Combine(Root, "catalog.json")).Entries;
        foreach (var e in State.Custom)
            if (!Entries.Any(x => x.Id == e.Id))
                Entries.Add(e);
        bool healed = false;
        foreach (var e in Entries)
        {
            if (HealDockTarget(e))
                healed = true;
            RepairMovedTarget(e);
            if (State.Names.TryGetValue(e.Id, out var name))
                e.Name = name;
            if (State.Categories.TryGetValue(e.Id, out var cat))
                e.Category = cat;
            if (State.Icons.TryGetValue(e.Id, out var icon))
                e.IconPath = icon;
        }
        if (healed)
            Save();
    }
    // Auto-réparation des entrées « dock: » dont l'exe a disparu (les applis Squirrel passent de app-1.0.9265 à app-1.0.9266).
    // L'id est conservé (favoris et épingles le référencent) ; seuls Target/IconPath changent.
    // Renvoie true seulement si l'entrée a été réparée ET appartient à State.Custom (le stockage persistant) : les entrées
    // de catalog.json sont réparées en mémoire uniquement, pour ne pas réécrire les préférences à chaque démarrage.
    static bool HealDockTarget(Entry entry)
    {
        if (!entry.Id.StartsWith("dock:", StringComparison.Ordinal)
            || !SafeToProbe(entry.Target)
            || File.Exists(entry.Target)
            || Directory.Exists(entry.Target))
            return false;
        string? fixedPath = HealStaleExe(entry.Target);
        if (fixedPath == null)
            return false;
        if (string.IsNullOrEmpty(entry.IconPath)
            || entry.IconPath.Equals(entry.Target, StringComparison.OrdinalIgnoreCase)
            || (SafeToProbe(entry.IconPath) && !File.Exists(entry.IconPath)))
            entry.IconPath = fixedPath;
        entry.Target = fixedPath;
        return State.Custom.Any(c => ReferenceEquals(c, entry));
    }
    // Ne jamais toucher aux chemins UNC ni aux lecteurs absents ou non prêts (un File.Exists dessus peut figer le thread UI).
    static bool SafeToProbe(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathRooted(path))
                return false;
            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return false;
            var drive = new DriveInfo(root);
            return drive.IsReady && Directory.Exists(root);
        }
        catch { return false; }
    }
    // Squirrel seulement : racine/app-X/nom.exe -> frère le plus récent racine/app-Y/nom.exe. Ordre numérique des versions,
    // suffixe « -xxx » ignoré ; à égalité, la build sans suffixe l'emporte.
    public static string? HealStaleExe(string path)
    {
        try
        {
            if (!SafeToProbe(path))
                return null;
            string name = Path.GetFileName(path);
            string? parentDir = Path.GetDirectoryName(path);
            string? root = parentDir == null ? null : Path.GetDirectoryName(parentDir);
            if (string.IsNullOrEmpty(name)
                || root == null
                || !Path.GetFileName(parentDir)!.StartsWith("app-", StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(root))
                return null;
            string? best = null;
            Version? bestVersion = null;
            bool bestSuffix = false;
            foreach (string dir in Directory.GetDirectories(root, "app-*"))
            {
                if (!File.Exists(Path.Combine(dir, name)))
                    continue;
                string tail = Path.GetFileName(dir)[4..];
                int dash = tail.IndexOf('-');
                bool suffix = dash >= 0;
                Version.TryParse(suffix ? tail[..dash] : tail, out var version);
                int cmp = best == null ? 1 : version == null ? (bestVersion == null ? 0 : -1) : bestVersion == null ? 1
                    : version.CompareTo(bestVersion);
                if (cmp > 0 || (cmp == 0 && bestSuffix && !suffix))
                {
                    best = dir;
                    bestVersion = version;
                    bestSuffix = suffix;
                }
            }
            return best == null ? null : Path.Combine(best, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Store.Log("Models.HealStaleExe : " + ex.GetType().Name + ": " + ex.Message);
        }
        return null;
    }
    static void RepairMovedTarget(Entry entry)
    {
        if (File.Exists(entry.Target) || Directory.Exists(entry.Target))
            return;
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (string folder in new[] { "02_Logiciels_et_Outils", "04_Documents_Administratifs" })
        {
            string prefix = Path.Combine(documents, folder);
            if (!entry.Target.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && !entry.Target.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            string replacement = Path.Combine(desktop, folder) + entry.Target[prefix.Length..];
            if (!Directory.Exists(replacement) && !File.Exists(replacement))
                continue;
            if (entry.IconPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                entry.IconPath = Path.Combine(desktop, folder) + entry.IconPath[prefix.Length..];
            entry.Target = replacement;
        }
    }
    public static string BoardFor(Entry e)
    {
        if (State.BoardOverrides.TryGetValue(e.Id, out var board))
            return board;
        return e.Section switch
        {
            "Applications" => "apps",
            "Jeux" => "games",
            "Projets" => "projects",
            _ => "documents"
        };
    }
    // Ids dont la cible a disparu (désinstallée) : masqués mais pas oubliés, une réinstallation ramène donc les favoris.
    internal static HashSet<string> Missing = new();
    internal static IEnumerable<Entry> Visible(string scope = "Tous", string query = "")
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries.Where(e =>
            !State.Hidden.Contains(e.Id)
                && !Missing.Contains(e.Id)
                && (scope == "Tous" || BoardFor(e) == scope)
                && words.All(w => Fold(e.Name + " " + e.Category + " " + e.Target).Contains(w)));
    }
    internal static IEnumerable<Entry> HomeFavorites(string scope = "Tous",
                                                     string query = "") => Visible(scope, query).Where(e =>
                                                         State.Favorites.Contains(e.Id)).OrderBy(e =>
                                                         {
                                                             int i = State.Recent.IndexOf(e.Id);
                                                             return i < 0 ? int.MaxValue : i;
                                                         }).ThenBy(e =>
                                                         {
                                                             int i = State.Favorites.IndexOf(e.Id);
                                                             return i < 0 ? int.MaxValue : i;
                                                         });
    internal static IEnumerable<Entry> RecentEntries(string scope = "Tous", string query = "")
        => Visible(scope, query).Where(e => State.Recent.Contains(e.Id)).OrderBy(e => State.Recent.IndexOf(e.Id));
    internal static void RecordLaunch(string id)
    {
        State.Recent.RemoveAll(x => x == id);
        State.Recent.Insert(0, id);
        State.Recent = State.Recent.Take(48).ToList();
        State.LaunchCounts.TryGetValue(id, out int count);
        State.LaunchCounts[id] = count == int.MaxValue ? count : count + 1;
    }
    public static IEnumerable<Entry> Sorted(IEnumerable<Entry> source) => source.OrderBy(e =>
    {
        int n = State.Order.IndexOf(e.Id);
        return n < 0 ? 100000 : n;
    }).ThenByDescending(e => State.Favorites.Contains(e.Id)).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
}
