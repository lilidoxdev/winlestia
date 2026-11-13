// language: C# 12, file: WinjetsService.cs, target: Windows 11 / .NET 8, façade des Winjets : bibliothèque, instances, données et état publié
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace PanneauxGlass.Winjets;

// Point d'entrée unique du pont : chaque action renvoie un message d'erreur lisible, ou null si tout s'est bien passé.
// Une seule serrure protège les écritures ; l'état publié est reconstruit seulement quand quelque chose a changé.
internal sealed class WinjetsService : IDisposable
{
    readonly object gate = new();
    readonly WinjetLibrary library;
    readonly WinjetInstances instances;
    readonly WinjetDataService data;
    bool editMode, disposed;
    object lastImport = new { ok = true, packageId = "", errors = Array.Empty<string>() };
    int revision, builtRevision = -1, explorerEpoch;
    object? built;

    internal WinjetsService(WinjetLibrary library, WinjetInstances instances, WinjetDataService data)
    {
        this.library = library;
        this.instances = instances;
        this.data = data;
        instances.Changed += Touch;
        data.Changed += Touch;
        lock (gate)
            SyncTargets();
    }

    // Construction de production : dossier de données comme Store, paquets inclus selon WINLESTIA_WINJETS_BUILTIN / installation.
    internal static WinjetsService CreateDefault()
    {
        string root = WinjetLibrary.DefaultRoot();
        Directory.CreateDirectory(root);
        return new WinjetsService(new WinjetLibrary(root, WinjetLibrary.ResolveBuiltinDir()),
            new WinjetInstances(Path.Combine(root, "instances.json")), new WinjetDataService(Path.Combine(root, "cache")));
    }

    internal WinjetLibrary Library => library;
    internal WinjetInstances Instances => instances;
    internal WinjetDataService Data => data;
    internal int ExplorerEpoch => Volatile.Read(ref explorerEpoch);
    internal bool EditMode
    {
        get
        {
            lock (gate)
                return editMode;
        }
    }

    void Touch() => Interlocked.Increment(ref revision);

    // Explorer a redémarré : le compteur change, le frontend replace ses fenêtres.
    internal void NotifyExplorerRestarted()
    {
        Interlocked.Increment(ref explorerEpoch);
        Touch();
    }

    WinjetPollTarget? TargetOf(WinjetInstance i)
        => library.Find(i.PackageId) is { } p
            ? new WinjetPollTarget(i.Id, p, new Dictionary<string, object?>(i.Settings), new Dictionary<string, object?>(i.State)) : null;

    // Appelé sous la serrure : les instances visibles dont le paquet existe sont les seules à être tenues à jour.
    void SyncTargets()
    {
        var targets = instances.All.Where(i => !i.Hidden).Select(TargetOf).OfType<WinjetPollTarget>().ToList();
        data.SetTargets(targets);
    }

    void Mutated()
    {
        SyncTargets();
        Touch();
    }

    // ---- Actions (docs/WINJETS-DESIGN.md §4) ----

    internal string? Inspect(string path) => Record(library.Inspect(path), true);

    internal string? Import(string path)
    {
        lock (gate)
        {
            var result = library.Import(path);
            if (result.Ok && result.Package != null)
            {
                foreach (var i in instances.All.Where(x => x.PackageId == result.PackageId))
                    data.Forget(i.Id);
                instances.Reconcile(result.Package);
            }
            string? error = Record(result, false);
            Mutated();
            return error;
        }
    }

    string? Record(WinjetImportResult r, bool inspect)
    {
        lock (gate)
        {
            lastImport = new
            {
                ok = r.Ok,
                packageId = r.PackageId,
                errors = r.Errors.ToArray(),
                inspect,
                updated = r.Updated,
                package = r.Package == null ? null : PackageView(r.Package)
            };
            Touch();
        }
        return r.Ok ? null : r.Errors.FirstOrDefault() ?? "Import impossible.";
    }

    internal string? Uninstall(string packageId)
    {
        lock (gate)
        {
            if (library.IsBuiltin(packageId))
                return "Un Winjet inclus ne peut pas être désinstallé.";
            var ids = instances.All.Where(i => i.PackageId == packageId).Select(i => i.Id).ToList();
            string? error = library.Uninstall(packageId);
            if (error != null)
                return error;
            instances.RemoveForPackage(packageId);
            foreach (string id in ids)
                data.Forget(id);
            Mutated();
            return null;
        }
    }

    internal string? Add(string packageId, string screen, double? x, double? y)
    {
        lock (gate)
        {
            var package = library.Find(packageId);
            if (package == null)
                return "Winjet inconnu : " + packageId;
            if (instances.Add(package, screen, x, y) == null)
                return "Trop de Winjets posés.";
            Mutated();
            return null;
        }
    }

    internal string? Remove(string instanceId)
    {
        lock (gate)
        {
            if (!instances.Remove(instanceId))
                return "Instance inconnue : " + instanceId;
            data.Forget(instanceId);
            Mutated();
            return null;
        }
    }

    internal string? SetHidden(string instanceId, bool hidden)
    {
        lock (gate)
        {
            if (!instances.SetHidden(instanceId, hidden))
                return "Instance inconnue : " + instanceId;
            Mutated();
            return null;
        }
    }

    internal string? SetOpacity(string instanceId, JsonElement value)
    {
        lock (gate)
        {
            if (!instances.SetOpacity(instanceId, value))
                return "Transparence invalide ou instance inconnue.";
            Mutated();
            return null;
        }
    }

    // Package de l'instance, ou message d'erreur.
    string? Resolve(string instanceId, out WinjetPackage package)
    {
        package = null!;
        var i = instances.Find(instanceId);
        if (i == null)
            return "Instance inconnue : " + instanceId;
        var p = library.Find(i.PackageId);
        if (p == null)
            return "Le Winjet de cette instance n'est plus installé.";
        package = p;
        return null;
    }

    internal string? SetGeometry(string instanceId, JsonElement geometry)
    {
        lock (gate)
        {
            string? error = Resolve(instanceId, out var package);
            if (error != null)
                return error;
            if (!instances.SetGeometry(instanceId, geometry, package.Definition))
                return "Géométrie invalide.";
            Touch();
            return null;
        }
    }

    internal string? SetSettings(string instanceId, JsonElement settings)
    {
        lock (gate)
        {
            string? error = Resolve(instanceId, out var package);
            if (error != null)
                return error;
            instances.SetSettings(instanceId, settings, package.Definition);
            Mutated();
            RefreshSoon(instanceId, false);
            return null;
        }
    }

    internal string? SetState(string instanceId, string key, JsonElement value)
    {
        lock (gate)
        {
            string? error = Resolve(instanceId, out var package);
            if (error != null)
                return error;
            error = instances.SetState(instanceId, key, value, package.Definition);
            if (error == null)
                Mutated();
            return error;
        }
    }

    internal string? SetEditMode(bool enabled)
    {
        lock (gate)
        {
            editMode = enabled;
            Touch();
            return null;
        }
    }

    internal string? Refresh(string instanceId)
    {
        lock (gate)
        {
            string? error = Resolve(instanceId, out _);
            if (error != null)
                return error;
            RefreshSoon(instanceId, true);
            return null;
        }
    }

    internal string? RecoverAll()
    {
        lock (gate)
        {
            instances.RecoverAll();
            Mutated();
            return null;
        }
    }

    // Lance le rafraîchissement sans bloquer le pont ; les échecs deviennent l'erreur de la source, jamais une exception.
    void RefreshSoon(string instanceId, bool force)
    {
        if (instances.Find(instanceId) is not { } i || TargetOf(i) is not { } target)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await data.RefreshAsync(target, force);
            }
            catch (Exception ex) { Store.Log("Winjets : rafraîchissement : " + ex.GetType().Name + ": " + ex.Message); }
        });
    }

    // ---- État publié ----

    static object PackageView(WinjetPackage p) => new
    {
        id = p.Id,
        version = p.Manifest.Version,
        name = p.Manifest.Name,
        author = p.Manifest.Author,
        description = p.Manifest.Description,
        builtin = p.Builtin,
        dir = p.Dir,
        preview = p.Preview,
        network = p.Manifest.Network,
        definition = p.Definition.Raw,
        manifest = p.Manifest.Raw
    };

    // Objet `state.winjets`, reconstruit seulement si la révision a changé depuis la dernière publication.
    internal object Snapshot()
    {
        lock (gate)
        {
            int rev = Volatile.Read(ref revision);
            if (built != null && builtRevision == rev)
                return built;
            var known = library.Packages;
            var views = instances.All
                .Where(i => known.Any(p => p.Id == i.PackageId))
                .Select(i => new { id = i.Id, packageId = i.PackageId, hidden = i.Hidden, opacity = i.Opacity, geometry = i.Geometry, settings = i.Settings, state = i.State })
                .ToArray();
            var values = data.Snapshot().ToDictionary(p => p.Key, p => p.Value.ToDictionary(q => q.Key, q => (object)new
            {
                value = q.Value.Value,
                updated = q.Value.Updated,
                error = q.Value.Error,
                stale = q.Value.Stale
            }));
            built = new
            {
                editMode,
                packages = known.Select(PackageView).ToArray(),
                instances = views,
                data = values,
                lastImport
            };
            builtRevision = rev;
            return built;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
        }
        instances.Changed -= Touch;
        data.Changed -= Touch;
        data.Dispose();
    }
}
