// language: C# 12, file: DesktopIconsService.cs, target: Windows 11 / .NET 8, masquage des icônes du Bureau et hook de réaffirmation
using System;
using System.Windows.Threading;

namespace PanneauxGlass;

internal interface IDesktopIconsService : IDisposable
{
    void Hide();
    void Restore();
    void OnExplorerCreated();
}

// Décisions pures du masquage : le service ne fait que les appliquer à des minuteurs.
internal static class DesktopIconsRules
{
    internal static readonly int[] Reasserts = { 3, 10 };
    // Le Bureau d'Explorer se publie après le démarrage : on retente brièvement puis on abandonne pour ne pas boucler à vie.
    internal static TimeSpan? RetryDelay(int failedAttempts)
        => failedAttempts is >= 1 and < 4 ? TimeSpan.FromSeconds(failedAttempts == 1 ? 1 : 2) : null;
    // Un hook ne s'installe ni en aperçu, ni après l'arrêt, ni si l'utilisateur a déjà repris la main sur le Bureau.
    internal static bool CanGuard(bool preview, bool disposed, bool suspended) => !(preview || disposed || suspended);
}

internal sealed class DesktopIconsService : IDesktopIconsService
{
    readonly bool preview;
    // Lus par le hook depuis un autre thread.
    volatile bool managed, suspended;
    volatile bool disposed;
    DesktopIconsGuard? guard;
    internal DesktopIconsService(bool preview) => this.preview = preview;
    // Le Bureau d'Explorer se publie après le démarrage : un appel unique arrivait trop tôt,
    // rendait la main sans erreur, et les icônes restaient visibles pour toute la session.
    public void Hide()
    {
        if (preview)
            return;
        suspended = false;
        int attempt = 0;
        void Apply()
        {
            try
            {
                Native.Desktop(true);
                managed = true;
                if (attempt == 0)
                    Store.Log("Icônes du Bureau masquées.");
            }
            catch (Exception ex)
            {
                attempt++;
                var delay = DesktopIconsRules.RetryDelay(attempt);
                if (delay != null)
                {
                    Store.Log("Masquage du Bureau, nouvel essai: " + ex.Message);
                    var retry = new DispatcherTimer { Interval = delay.Value };
                    retry.Tick += (_, _) =>
                    {
                        retry.Stop();
                        Apply();
                    };
                    retry.Start();
                }
                else
                    Store.Log("Masquage du Bureau impossible: " + ex.Message);
            }
        }
        Apply();
        StartGuard();
        foreach (int delay in DesktopIconsRules.Reasserts)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (disposed || preview)
                    return;
                Apply();
            };
            timer.Start();
        }
    }
    // Hook Explorer du Bureau : réaffirme FWF_NOICONS si un moteur de fond d'écran recrée la vue. Un seul hook, réinstallé après
    // TaskbarCreated.
    void StartGuard(bool reinstall = false)
    {
        if (!DesktopIconsRules.CanGuard(preview, disposed, suspended))
            return;
        guard ??= new DesktopIconsGuard(() => managed, () =>
        {
            Native.Desktop(true);
            managed = true;
        }, () =>
        {
            suspended = true;
            managed = false;
        });
        if (reinstall || !guard.Installed)
            guard.Install();
    }
    // TaskbarCreated : Explorer vient de (re)démarrer, le hook est réinstallé et le Bureau réaffirmé.
    public void OnExplorerCreated()
    {
        if (suspended)
            return;
        StartGuard(true);
        guard?.Request(true);
    }
    public void Restore()
    {
        suspended = true;
        guard?.Uninstall();
        if (!managed)
            return;
        try
        {
            Native.Desktop(false);
            managed = false;
            Store.Log("Icônes du Bureau rétablies.");
        }
        catch (Exception ex) { Store.Log("Rétablissement du Bureau: " + ex.Message); }
    }
    public void Dispose()
    {
        disposed = true;
        guard?.Dispose();
    }
}
