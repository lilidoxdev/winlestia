// Store: persistence, data directory, logging and catalogue lookups.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Winlestia.Shared;

namespace PanneauxGlass.Storage;

public static class Store
{
    public static string Root = AppDomain.CurrentDomain.BaseDirectory;

    // User data (preferences, log, clipboard, pins) lives outside the install folder so it survives
    // uninstall/reinstall. WINLESTIA_DATA_DIR overrides it (tests).
    static string? dirEnv,
        dirCached;
    static bool dirInit;
    public static string DataDir
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");
            if (dirInit && dirEnv == env && dirCached != null)
                return dirCached;
            string d = !string.IsNullOrEmpty(env)
                ? env
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Panneaux");
            try
            {
                Directory.CreateDirectory(d);
            }
            catch { }
            dirEnv = env;
            dirCached = d;
            dirInit = true;
            return d;
        }
    }
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
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
                string f = Path.Combine(DataDir, "glass.log"),
                    line = DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine;
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
        catch { }
    }

    static bool InDataDir(string path)
    {
        string root =
            Path.GetFullPath(DataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static T Read<T>(string path)
        where T : new()
    {
        if (!File.Exists(path))
            return new T();
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Transient (locked, AV scan...): the file is not known to be corrupt, so never touch disk here.
            System.Threading.Thread.Sleep(50);
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
            {
                Log(Path.GetFileName(path) + ": read failed: " + ex2.Message);
                return new T();
            }
        }
        catch (DecoderFallbackException ex)
        {
            return Recover<T>(path, ex);
        }
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json) ?? new T();
        }
        catch (JsonException ex)
        {
            return Recover<T>(path, ex);
        }
    }

    static T Recover<T>(string path, Exception ex)
        where T : new()
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
                    Log(Path.GetFileName(path) + ": restored from .bak");
                    // Put the good copy back as main: the next Save would otherwise roll the corrupt main over the good .bak.
                    try
                    {
                        File.Copy(bak, path, true);
                    }
                    catch (Exception ex2)
                    {
                        Log(Path.GetFileName(path) + ": could not restore the main file: " + ex2.Message);
                    }
                    return recovered;
                }
            }
            catch (Exception ex2)
            {
                Log(Path.GetFileName(path) + ".bak: " + ex2.Message);
            }
        }
        Log(Path.GetFileName(path) + ": default values used");
        return new T();
    }

    // A corrupt user file is about to be replaced by defaults on the next Save: keep the bytes (one copy, kept apart from the rolling .bak).
    static void KeepCorrupt(string path)
    {
        try
        {
            if (!InDataDir(path))
                return;
            File.Copy(path, path + ".corrupt.bak", true);
            Log(Path.GetFileName(path) + ": unreadable file copy kept (.corrupt.bak)");
        }
        catch (Exception ex)
        {
            Log(Path.GetFileName(path) + ": could not copy .corrupt.bak: " + ex.Message);
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string path = Path.Combine(DataDir, "preferences-glass.json");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(State, Json), Encoding.UTF8, path + ".bak");
        }
        catch (Exception ex)
        {
            Log("Enregistrement: " + ex.Message);
        }
    }

    // One-shot migration from the install folder (legacy location). Copy prefs (old binaries keep working), move big
    // folders, never overwrite, never throw.
    public static void MigrateLegacyData(string legacyRoot, string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            string prefs = Path.Combine(dataDir, "preferences-glass.json"),
                old = Path.Combine(legacyRoot, "preferences-glass.json");
            if (!File.Exists(prefs) && File.Exists(old))
            {
                try
                {
                    File.Copy(old, prefs, false);
                }
                catch (Exception ex)
                {
                    Log("Migration preferences-glass.json: " + ex.Message);
                }
            }
            try
            {
                string bak = Path.Combine(legacyRoot, "preferences-glass.json.bak"),
                    bakTo = Path.Combine(dataDir, "preferences-glass.json.bak");
                if (File.Exists(bak) && !File.Exists(bakTo))
                    File.Copy(bak, bakTo, false);
            }
            catch (Exception ex)
            {
                Log("Migration preferences-glass.json.bak: " + ex.Message);
            }
            foreach (string folder in new[] { "clipboard", "pins" })
            {
                string from = Path.Combine(legacyRoot, folder),
                    to = Path.Combine(dataDir, folder),
                    tmp = to + ".migrating";
                if (Directory.Exists(tmp) && !Directory.Exists(to))
                {
                    try
                    {
                        Directory.Delete(tmp, true);
                    }
                    catch (Exception ex)
                    {
                        Log("Migration " + folder + " (reliquat): " + ex.Message);
                    }
                }
                if (!Directory.Exists(from) || Directory.Exists(to))
                    continue;
                try
                {
                    Directory.Move(from, to);
                }
                catch (Exception ex)
                {
                    Log("Migration " + folder + " (move): " + ex.Message);
                    try
                    {
                        if (Directory.Exists(tmp))
                            Directory.Delete(tmp, true);
                        CopyTree(from, tmp);
                        Directory.Move(tmp, to);
                    }
                    catch (Exception ex2)
                    {
                        Log("Migration " + folder + " (copy): " + ex2.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                Log("Migration: " + ex.Message);
            }
            catch { }
        }
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
                catch { }
            }
        }
        State.Normalize();
        if (State.GlassOpacityRevision < 1)
        {
            if (Math.Abs(State.GlassOpacity - .12) < 1e-9)
                State.GlassOpacity = .95;
            State.GlassOpacityRevision = 1;
        }
        LoadCatalogue();
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

    // Self-heal for "dock:" entries whose exe vanished (Squirrel apps move app-1.0.9265 -> app-1.0.9266). The id is
    // kept (favourites/pins reference it); only Target/IconPath change.
    // Returns true only when the entry was healed AND belongs to State.Custom (the persisted store): catalog.json
    // entries are healed in memory only, so no prefs rewrite per start.
    static bool HealDockTarget(Entry entry)
    {
        if (
            !entry.Id.StartsWith("dock:", StringComparison.Ordinal)
            || !SafeToProbe(entry.Target)
            || File.Exists(entry.Target)
            || Directory.Exists(entry.Target)
        )
            return false;
        string? fixedPath = HealStaleExe(entry.Target);
        if (fixedPath == null)
            return false;
        if (
            string.IsNullOrEmpty(entry.IconPath)
            || entry.IconPath.Equals(entry.Target, StringComparison.OrdinalIgnoreCase)
            || (SafeToProbe(entry.IconPath) && !File.Exists(entry.IconPath))
        )
            entry.IconPath = fixedPath;
        entry.Target = fixedPath;
        return State.Custom.Any(c => ReferenceEquals(c, entry));
    }

    // Never touch UNC paths or drives that are missing / not ready (a File.Exists on those can stall the UI thread).
    static bool SafeToProbe(string? path)
    {
        try
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal)
                || !Path.IsPathRooted(path)
            )
                return false;
            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return false;
            var drive = new DriveInfo(root);
            return drive.IsReady && Directory.Exists(root);
        }
        catch
        {
            return false;
        }
    }

    // Squirrel only: root/app-X/name.exe -> newest sibling root/app-Y/name.exe. Numeric version order, "-suffix"
    // stripped; on a tie the build without suffix wins.
    public static string? HealStaleExe(string path)
    {
        try
        {
            if (!SafeToProbe(path))
                return null;
            string name = Path.GetFileName(path);
            string? parentDir = Path.GetDirectoryName(path);
            string? root = parentDir == null ? null : Path.GetDirectoryName(parentDir);
            if (
                string.IsNullOrEmpty(name)
                || root == null
                || !Path.GetFileName(parentDir)!.StartsWith("app-", StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(root)
            )
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
                int cmp =
                    best == null ? 1
                    : version == null ? (bestVersion == null ? 0 : -1)
                    : bestVersion == null ? 1
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
        catch { }
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
            if (
                !entry.Target.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && !entry.Target.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            )
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
            _ => "documents",
        };
    }

    internal static IEnumerable<Entry> Visible(string scope = "Tous", string query = "")
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries.Where(e =>
            !State.Hidden.Contains(e.Id)
            && (scope == "Tous" || BoardFor(e) == scope)
            && words.All(w => Fold(e.Name + " " + e.Category + " " + e.Target).Contains(w))
        );
    }

    internal static IEnumerable<Entry> HomeFavorites(string scope = "Tous", string query = "") =>
        Visible(scope, query)
            .Where(e => State.Favorites.Contains(e.Id))
            .OrderBy(e =>
            {
                int i = State.Recent.IndexOf(e.Id);
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(e =>
            {
                int i = State.Favorites.IndexOf(e.Id);
                return i < 0 ? int.MaxValue : i;
            });

    internal static IEnumerable<Entry> RecentEntries(string scope = "Tous", string query = "") =>
        Visible(scope, query).Where(e => State.Recent.Contains(e.Id)).OrderBy(e => State.Recent.IndexOf(e.Id));

    internal static void RecordLaunch(string id)
    {
        State.Recent.RemoveAll(x => x == id);
        State.Recent.Insert(0, id);
        State.Recent = State.Recent.Take(48).ToList();
        State.LaunchCounts.TryGetValue(id, out int count);
        State.LaunchCounts[id] = count == int.MaxValue ? count : count + 1;
    }

    public static IEnumerable<Entry> Sorted(IEnumerable<Entry> source) =>
        source
            .OrderBy(e =>
            {
                int n = State.Order.IndexOf(e.Id);
                return n < 0 ? 100000 : n;
            })
            .ThenByDescending(e => State.Favorites.Contains(e.Id))
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
}
