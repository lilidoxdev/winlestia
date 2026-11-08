// C# 12 / ExplorerAppearance.StableIcons.cs / Windows 11 : chemins d'icônes stables (<kind>-current.ico), régénération avec
// anti-rebond, migration en arrière-plan des anciens ini <hex>-<kind>-v2.ico.
// Un changement de palette réécrit UN petit jeu de fichiers .ico (temporaire atomique + remplacement) au lieu du desktop.ini de chaque
// dossier.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace PanneauxGlass;

internal sealed partial class ExplorerAppearance
{
    internal static readonly string[] IconKinds = { "folder", "desktop", "download", "document", "picture", "music", "video" };
    const string Scheme = "current-v3";
    sealed class IconState
    {
        public string Accent { get; set; } = ""; public bool Migrated
        {
            get; set;
        }
    }
    readonly object iconGate = new(); IconState? iconState; Timer? iconTimer; int shellGen, iconRetry; bool migrationStarted;
    internal Action<uint, uint, string?>? NotifySink; // tests : capturer les notifications du shell au lieu de les diffuser
    string IconsDir => Path.Combine(root, "icons");
    string StatePath => Path.Combine(IconsDir, "state.json");
    internal string StableIcon(string kind) => Path.Combine(IconsDir, (IconKinds.Contains(kind) ? kind : "folder") + "-current.ico");
    void Shell(uint kind, uint flags, string? path)
    {
        if (NotifySink != null)
            NotifySink(kind, flags, path);
        else
            SHChangeNotify(kind, flags, path, IntPtr.Zero);
    }
    IconState LoadState()
    {
        lock (iconGate)
        {
            if (iconState != null)
                return iconState;
            try
            {
                iconState = File.Exists(StatePath) ? JsonSerializer.Deserialize<IconState>(File.ReadAllText(StatePath)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Store.Log("ExplorerAppearance.StableIcons.LoadState : " + ex.GetType().Name + ": " + ex.Message);
            }
            return iconState ??= new();
        }
    }
    void SaveState()
    {
        lock (iconGate)
        {
            Directory.CreateDirectory(IconsDir);
            string temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(iconState ?? new()));
            File.Move(temp, StatePath, true);
        }
    }
    // Dessine dans un fichier temporaire puis l'échange : les lecteurs (Explorateur) ne voient jamais un .ico à moitié écrit.
    static void DrawAtomic(string path, string hex, string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            DrawIcon(temp, hex, kind);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path, true);
        }
        finally { Safe.Try(() => File.Delete(temp)); }
    }
    // Chemin d'icône stable pour un type ; dessiné à la demande (accent courant) s'il n'existe pas encore.
    internal string EnsureIcon(string kind)
    {
        lock (iconGate)
        {
            string path = StableIcon(kind);
            if (!File.Exists(path))
                DrawAtomic(path, accent, IconKinds.Contains(kind) ? kind : "folder");
            return path;
        }
    }
    // Régénère toutes les icônes stables quand l'accent diffère de celui stocké (ou qu'un fichier manque). Renvoie true quand du
    // contenu d'icône existant a été remplacé (les caches du shell doivent être rafraîchis).
    internal bool RegenerateIcons(string hex)
    {
        lock (iconGate)
        {
            var state = LoadState();
            bool changed = !state.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase);
            bool replaced = changed && state.Accent.Length > 0;
            foreach (string kind in IconKinds)
            {
                string path = StableIcon(kind);
                if (changed || !File.Exists(path))
                    DrawAtomic(path, hex, kind);
            }
            if (changed)
            {
                state.Accent = hex;
                SaveState();
            }
            return replaced;
        }
    }
    // Appelé par Sync (déjà hors du thread UI) : arme UNE régénération avec anti-rebond de 1 s si nécessaire et lance la migration
    // ponctuelle.
    void StartIconMaintenance()
    {
        if (disposed)
            return;
        try
        {
            lock (iconGate)
            {
                var state = LoadState();
                if (!state.Accent.Equals(accent, StringComparison.OrdinalIgnoreCase) || !IconKinds.All(k => File.Exists(StableIcon(k))))
                {
                    iconRetry = 0;
                    (iconTimer ??= new Timer(_ => IconTick(), null, Timeout.Infinite, Timeout.Infinite)).Change(1000, Timeout.Infinite);
                }
                if (!migrationStarted && !state.Migrated)
                {
                    migrationStarted = true;
                    _ = Task.Run(async () => { try { await MigrateAsync(); } catch (Exception ex) { Store.Log("Migration icônes: "
                        + ex.Message); } });
                }
            }
        }
        catch (Exception ex) { Store.Log("Icônes dossier: " + ex.Message); }
    }
    void IconTick()
    {
        if (disposed || !Store.State.ExplorerTheme)
            return;
        try
        {
            if (RegenerateIcons(accent))
            {
                int gen = Interlocked.Increment(ref shellGen);
                _ = Task.Run(() => RefreshShellAsync(gen));
            }
        }
        catch (Exception ex) { Store.Log("Icônes dossier régénération: " + ex.Message); if (!disposed
            && ++iconRetry <= 3) lock (iconGate) iconTimer?.Change(5000, Timeout.Infinite); }
    }
    // Rafraîchissement du shell après modification des fichiers d'icônes : un ASSOCCHANGED (flush nowait), puis SHCNE_UPDATEITEM par
    // dossier du manifeste en lots bornés (64 par 250 ms ; 32 par 500 ms avec effets réduits). Ne touche jamais à ie4uinit /
    // explorer.exe.
    internal async Task RefreshShellAsync(int gen, int batch = 64, int pauseMs = 250)
    {
        Shell(0x08000000, 0x2000, null);
        if (M3.ReducedEffects)
        {
            batch = Math.Min(batch, 32);
            pauseMs = Math.Max(pauseMs, 500);
        }
        List<Backup> data;
        try
        {
            data = Read();
        }
        catch { return; }
        for (int i = 0; i < data.Count; i += batch)
        {
            if (disposed || gen != Volatile.Read(ref shellGen))
                return;
            foreach (var saved in data.Skip(i).Take(batch))
                Shell(0x2000, 5 | 0x2000, saved.Folder);
            await Task.Delay(pauseMs);
        }
    }
    Regex OldIconPattern() => new(Regex.Escape(IconsDir) + @"[\\/][0-9a-f]{6}-(?<kind>[a-z]+)-v2\.ico", RegexOptions.IgnoreCase);
    // Migration ponctuelle de chaque entrée du manifeste dont le desktop.ini pointe encore vers un ancien <hex>-<kind>-v2.ico. Lots
    // bornés, étalés, chaque lot relit le manifeste sous le gate (aucune mise à jour perdue avec le thématage des dossiers visibles).
    // Un desktop.ini dont le hash diffère d'AppliedHash a été modifié par l'utilisateur ou un autre outil : ignoré, jamais écrasé.
    // Ensuite
    // les anciennes icônes que personne ne référence sont supprimées.
    internal async Task<(int migrated, int edited, int failed, int removed)> MigrateAsync(int batch = 64, int pauseMs = 250)
    {
        List<string> folders;
        lock (gate)
        {
            folders = Read().Where(p => p.AppliedHash != null).Select(p => p.Folder).ToList();
        }
        int migrated = 0, edited = 0, failed = 0;
        var pattern = OldIconPattern();
        for (int i = 0; i < folders.Count; i += batch)
        {
            lock (gate)
            {
                if (disposed)
                    return (migrated, edited, failed, 0);
                var data = Read();
                bool dirty = false;
                foreach (string folder in folders.Skip(i).Take(batch))
                {
                    try
                    {
                        var saved = data.FirstOrDefault(p => p.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
                        if (saved?.AppliedHash == null || !Directory.Exists(folder) || Protected(folder))
                            continue;
                        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                            continue;
                        string ini = Path.Combine(folder, "desktop.ini");
                        if (!File.Exists(ini))
                            continue;
                        var match = pattern.Match(File.ReadAllText(ini));
                        if (!match.Success)
                            continue;
                        if (Hash(ini) != saved.AppliedHash)
                        {
                            edited++;
                            continue;
                        }
                        WriteFolder(folder, EnsureIcon(match.Groups["kind"].Value.ToLowerInvariant()), saved);
                        migrated++;
                        dirty = true;
                    }
                    catch (Exception ex) { failed++; Store.Log("Migration icône ignorée: " + folder + ": " + ex.Message); }
                }
                if (dirty)
                    Save(data);
            }
            if (i + batch < folders.Count)
                await Task.Delay(pauseMs);
        }
        int removed = 0;
        lock (gate)
        {
            if (disposed)
                return (migrated, edited, failed, 0);
            removed = CleanupLegacyIcons();
        }
        lock (iconGate)
        {
            LoadState().Migrated = true;
            try
            {
                SaveState();
            }
            catch (Exception err)
            {
                Store.Log("ExplorerAppearance.StableIcons.MigrateAsync : " + err.GetType().Name + ": " + err.Message);
            }
        }
        return (migrated, edited, failed, removed);
    }
    // Supprime les anciens fichiers <hex>-<kind>-v2.ico qu'aucun desktop.ini du manifeste ne référence plus (les référencés, p. ex.
    // ini
    // modifiés par l'utilisateur, sont conservés).
    internal int CleanupLegacyIcons()
    {
        int removed = 0;
        try
        {
            if (!Directory.Exists(IconsDir))
                return 0;
            var pattern = OldIconPattern();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var saved in Read())
            {
                try
                {
                    string ini = Path.Combine(saved.Folder, "desktop.ini");
                    if (!File.Exists(ini) || new FileInfo(ini).Length > 65536)
                        continue;
                    foreach (Match m in pattern.Matches(File.ReadAllText(ini)))
                        used.Add(Path.GetFileName(m.Value));
                }
                catch (Exception)
                {
                    // desktop.ini illisible : ce dossier est laissé tel quel.
                }
            }
            var legacy = new Regex(@"^[0-9a-f]{6}-[a-z]+-v2\.ico$", RegexOptions.IgnoreCase);
            foreach (string file in Directory.EnumerateFiles(IconsDir, "*-v2.ico"))
            {
                string name = Path.GetFileName(file);
                if (!legacy.IsMatch(name) || used.Contains(name))
                    continue;
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception err) when (err is IOException or UnauthorizedAccessException)
                {
                    Store.Log("ExplorerAppearance.StableIcons.CleanupLegacyIcons : " + err.GetType().Name + ": " + err.Message);
                }
            }
        }
        catch (Exception ex) { Store.Log("Nettoyage icônes: " + ex.Message); }
        return removed;
    }
    // Entrée publique pour l'application Fichiers (navigation) : utilisable depuis n'importe quel thread. Anti-rebond de 3 s par dossier
    // + budget global de 12 appels / 10 s ; le gros du travail reste borné par ColourVisibleTree (256 thémés / 1024 examinés).
    readonly Dictionary<string, DateTime> visitedAt = new(StringComparer.OrdinalIgnoreCase); DateTime budgetStart; int budgetUsed;
    internal void ColourVisibleFolder(string parent)
    {
        if (disposed || string.IsNullOrWhiteSpace(parent))
            return;
        var now = DateTime.UtcNow;
        lock (visitedAt)
        {
            if (visitedAt.TryGetValue(parent, out var last) && (now - last).TotalSeconds < 3)
                return;
            if ((now - budgetStart).TotalSeconds >= 10)
            {
                budgetStart = now;
                budgetUsed = 0;
            }
            if (budgetUsed >= 12)
                return;
            budgetUsed++;
            if (visitedAt.Count >= 256)
                visitedAt.Clear();
            visitedAt[parent] = now;
        }
        try
        {
            ColourVisibleTree(parent, new[] { 1024 });
        }
        catch (Exception ex) { Store.Log("Dossier affiché: " + ex.Message); }
    }
    void DisposeIcons()
    {
        Safe.Try(() =>
        {
            lock (iconGate)
            {
                iconTimer?.Dispose();
                iconTimer = null;
            }

        });
    }
}
