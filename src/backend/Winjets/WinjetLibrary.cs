// language: C# 12, file: WinjetLibrary.cs, target: Windows 11 / .NET 8, paquets inclus (lecture seule) et installés (import, mise à jour, retrait)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PanneauxGlass.Winjets;

internal sealed record WinjetImportResult(bool Ok, string PackageId, IReadOnlyList<string> Errors, WinjetPackage? Package, bool Updated);

internal sealed class WinjetLibrary
{
    readonly object gate = new();
    // Sérialise les modifications du dossier des paquets (échange, désinstallation) ; l'extraction se fait hors verrou.
    readonly object mutate = new();
    readonly string? builtinDir;
    readonly string packagesDir, tempDir;
    readonly Version current;
    List<WinjetPackage> packages = new();
    HashSet<string> builtinIds = new(StringComparer.Ordinal);

    // `root` = <données>\winjets. `builtin` = dossier des paquets inclus (null = aucun).
    internal WinjetLibrary(string root, string? builtin, Version? current = null)
    {
        builtinDir = builtin;
        packagesDir = Path.Combine(root, "packages");
        tempDir = Path.Combine(root, "tmp");
        this.current = current ?? WinjetValidator.CurrentVersion();
        Directory.CreateDirectory(packagesDir);
        // Restes d'un arrêt brutal (préparations et anciennes versions) : sans valeur, on les supprime.
        RestoreInterruptedSwaps();
        TryDelete(tempDir);
        Directory.CreateDirectory(tempDir);
        Reload();
    }

    // Une coupure entre « ancienne version mise de côté » et « nouvelle version posée » laisse tmp\old-<id>~<guid> sans
    // packages\<id> : on remet l'ancienne version avant de purger tmp.
    void RestoreInterruptedSwaps()
    {
        try
        {
            if (!Directory.Exists(tempDir))
                return;
            foreach (string dir in Directory.EnumerateDirectories(tempDir, "old-*"))
            {
                string name = Path.GetFileName(dir);
                int cut = name.LastIndexOf('~');
                if (cut < 5)
                    continue;
                string target = Path.Combine(packagesDir, name.Substring(4, cut - 4));
                if (!Directory.Exists(target))
                    Directory.Move(dir, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Store.Log("Winjets : reprise d'un échange interrompu impossible : " + ex.Message);
        }
    }

    // Dossier de données résolu comme Store (WINLESTIA_DATA_DIR d'abord).
    internal static string DefaultRoot() => Path.Combine(Store.DataDir, "winjets");

    // WINLESTIA_WINJETS_BUILTIN, sinon <exe>\..\Winjets\builtin (installé), sinon remontée vers src\winjets\builtin (développement).
    internal static string? ResolveBuiltinDir()
    {
        string? env = Environment.GetEnvironmentVariable("WINLESTIA_WINJETS_BUILTIN");
        if (!string.IsNullOrWhiteSpace(env))
            return Directory.Exists(env) ? env : null;
        string exe = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string installed = Path.GetFullPath(Path.Combine(exe, "..", "Winjets", "builtin"));
        if (Directory.Exists(installed))
            return installed;
        for (var dir = new DirectoryInfo(exe); dir != null; dir = dir.Parent)
        {
            string dev = Path.Combine(dir.FullName, "src", "winjets", "builtin");
            if (Directory.Exists(dev))
                return dev;
        }
        return null;
    }

    internal string PackagesDir => packagesDir;

    internal IReadOnlyList<WinjetPackage> Packages
    {
        get
        {
            lock (gate)
                return packages.ToArray();
        }
    }

    internal WinjetPackage? Find(string id)
    {
        lock (gate)
            return packages.FirstOrDefault(p => p.Id == id);
    }

    internal bool IsBuiltin(string id)
    {
        lock (gate)
            return builtinIds.Contains(id);
    }

    internal void Reload()
    {
        var all = new List<WinjetPackage>();
        var builtin = LoadFolder(builtinDir, true, null);
        all.AddRange(builtin);
        var ids = builtin.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in LoadFolder(packagesDir, false, ids))
            if (ids.Add(p.Id))
                all.Add(p);
        lock (gate)
        {
            packages = all;
            builtinIds = builtin.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        }
    }

    // Charge chaque sous-dossier valide ; un paquet invalide est journalisé et ignoré, jamais bloquant.
    List<WinjetPackage> LoadFolder(string? folder, bool builtin, HashSet<string>? refused)
    {
        var result = new List<WinjetPackage>();
        if (folder == null || !Directory.Exists(folder))
            return result;
        foreach (string dir in Directory.EnumerateDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('.') || (dir.Length > 0 && new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)))
                continue;
            var package = WinjetPackage.Load(dir, builtin, current, out var errors);
            if (package == null)
                Store.Log("Winjets : paquet ignoré (" + name + ") : " + string.Join(" | ", errors.Take(3)));
            else if (!builtin && !string.Equals(package.Id, name, StringComparison.Ordinal))
                Store.Log("Winjets : paquet ignoré (" + name + ") : le dossier ne porte pas l'identifiant " + package.Id);
            else if (refused != null && refused.Contains(package.Id))
                Store.Log("Winjets : paquet installé ignoré, identifiant réservé à un paquet inclus : " + package.Id);
            else
                result.Add(package);
        }
        return result;
    }

    // Valide sans installer. Le paquet décompressé reste dans une zone temporaire pour l'aperçu (un seul aperçu à la fois).
    internal WinjetImportResult Inspect(string path)
    {
        using var v = WinjetValidator.Validate(path, current, tempDir);
        if (!v.Ok)
            return new WinjetImportResult(false, "", v.Errors, null, false);
        string dest = Path.Combine(tempDir, "inspect-" + Guid.NewGuid().ToString("N"));
        lock (mutate)
        {
            // On garde les aperçus récents (un autre appel peut encore en lire un) et on supprime les plus anciens.
            foreach (string old in Directory.EnumerateDirectories(tempDir, "inspect-*").OrderByDescending(Directory.GetCreationTimeUtc).Skip(3))
                TryDelete(old);
            Directory.Move(v.Release(), dest);
        }
        var package = new WinjetPackage(v.Package!.Manifest, v.Package.Definition, dest, false);
        return new WinjetImportResult(true, package.Id, Array.Empty<string>(), package, Find(package.Id) is { Builtin: false });
    }

    internal WinjetImportResult Import(string path)
    {
        using var v = WinjetValidator.Validate(path, current, tempDir);
        if (!v.Ok)
            return new WinjetImportResult(false, "", v.Errors, null, false);
        string id = v.Package!.Id;
        if (IsBuiltin(id))
            return Fail(id, "L'identifiant « " + id + " » appartient à un Winjet inclus : il ne peut pas être remplacé.");
        string final = Path.Combine(packagesDir, id), old = Path.Combine(tempDir, "old-" + id + "~" + Guid.NewGuid().ToString("N"));
        string staged = v.Release();
        bool updated;
        lock (mutate)
        try
        {
            updated = Directory.Exists(final);
            // Échange : l'ancienne version est mise de côté, la nouvelle prend sa place ; au moindre échec on remet l'ancienne.
            if (updated)
                Directory.Move(final, old);
            try
            {
                Directory.Move(staged, final);
            }
            catch
            {
                if (updated)
                    try
                    {
                        Directory.Move(old, final);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Store.Log("Winjets : retour arrière impossible, l'ancienne version reste dans " + old + " : " + ex.Message);
                    }
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(staged);
            return Fail(id, "Installation impossible : " + ex.Message);
        }
        lock (mutate)
        {
            TryDelete(old);
            Reload();
        }
        return new WinjetImportResult(true, id, Array.Empty<string>(), Find(id), updated);
    }

    static WinjetImportResult Fail(string id, string error) => new(false, id, new[] { error }, null, false);

    // Retire un paquet installé. Message d'erreur, ou null si c'est fait.
    internal string? Uninstall(string id)
    {
        if (IsBuiltin(id))
            return "Un Winjet inclus ne peut pas être désinstallé.";
        var package = Find(id);
        if (package == null)
            return "Winjet inconnu : " + id;
        lock (mutate)
        {
            try
            {
                Directory.Delete(package.Dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "Désinstallation impossible : " + ex.Message;
            }
            Reload();
        }
        return null;
    }

    static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("Winjets : nettoyage impossible : " + ex.Message); }
    }
}
