// C# 12 / WallpaperApply.cs / Applique le fond d'écran via IDesktopWallpaper (tous les écrans ou un seul) et liste les écrans. Piloté
// par événements : chaque appel construit son objet COM sur un STA éphémère et le libère avant de retourner.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PanneauxGlass;

internal sealed record WallpaperDisplay(int id, string device, string name, int width, int height, bool primary, string path);

internal static class WallpaperApply
{
    static readonly Guid Clsid = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SystemParametersInfo(uint action,
        uint param, string value, uint flags);

    // COM exige un thread STA et doit rester hors du thread UI : on utilise un thread dédié, abandonné après 5 s (un explorer figé ne
    // doit pas geler le shell).
    sealed class Abandon
    {
        public volatile bool Set;
    }
    static T OnSta<T>(Func<Abandon, T> work, T fallback, out bool expired)
    {
        T result = fallback;
        var abandon = new Abandon();
        var thread = new Thread(() => { try { result = work(abandon); } catch (Exception ex) { Store.Log("Fond d’écran: "
            + ex.Message); }
            }) { IsBackground = true, Name = "Wallpaper STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        expired = !thread.Join(5000);
        if (expired)
        {
            abandon.Set = true;
            return fallback;
        }
        return result;
    }

    // Principal d'abord, puis l'ordre rapporté par Windows. Un écran sans rectangle exploitable (débranché) est ignoré.
    static List<WallpaperDisplay> Enumerate(IDesktopWallpaper api)
    {
        var found = new List<(string device, Native.Rect rect, string path)>();
        if (api.GetMonitorDevicePathCount(out uint count) != 0)
            return new();
        for (uint i = 0; i < count; i++)
        {
            if (api.GetMonitorDevicePathAt(i, out string device) != 0 || string.IsNullOrEmpty(device) || api.GetMonitorRECT(device,
                out var rect) != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                continue;
            api.GetWallpaper(device, out string? path);
            found.Add((device, rect, path ?? ""));
        }
        var ordered = found.OrderBy(f => f.rect.Left == 0 && f.rect.Top == 0 ? 0 : 1).ToList();
        return ordered.Select((f, n) => new WallpaperDisplay(n + 1, f.device, WallpaperLogic.DisplayName(n + 1, f.rect.Right - f.rect.Left,
            f.rect.Bottom - f.rect.Top, n == 0), f.rect.Right - f.rect.Left, f.rect.Bottom - f.rect.Top, n == 0, f.path)).ToList();
    }

    static T WithApi<T>(Func<IDesktopWallpaper, Abandon, T> use, T fallback, out bool expired) => OnSta(abandon =>
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid)!);
            return use((IDesktopWallpaper)instance!, abandon);
        }
        finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance); }
    }, fallback, out expired);

    internal static List<WallpaperDisplay> List() => WithApi((api, _) => Enumerate(api), new List<WallpaperDisplay>(), out _);

    // Les noms de processus sont lus une fois par appel (ouverture du sélecteur, application, liste CLI) ; rien ne les surveille.
    internal static string[] Animated()
    {
        var names = new List<string>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    names.Add(p.ProcessName);
                }
                catch (Exception)
                {
                    // Processus fermé ou inaccessible : ignoré.
                }
                finally { p.Dispose(); }
            }
        }
        catch (Exception)
        {
            // Processus fermé ou inaccessible : ignoré.
        }
        return WallpaperLogic.Animated(names);
    }

    // display 0 = tous les écrans. Effets normaux : IDesktopWallpaper.SetWallpaper, l'appel que Windows fait lui-même pour son fondu ;
    // effets légers : SPI_SETDESKWALLPAPER, sans transition supplémentaire demandée.
    // Un écran débranché ou inconnu est un refus avec message, jamais une exception.
    internal static (int code, string text) Apply(string path, int display, bool light)
    {
        if (display <= 0 && light)
            return SystemParametersInfo(0x14, 0, path, 3) ? (0, "ok") : (1, "Windows refused the wallpaper.");
        int code = WithApi((api, abandon) =>
        {
            string? monitor = null;
            if (display > 0)
            {
                var target = Enumerate(api).FirstOrDefault(d => d.id == display);
                if (target == null)
                    return 2;
                monitor = target.device;
            }
            if (abandon.Set)
                return 3;   // l'appelant a déjà abandonné : ne pas changer le fond d'écran après coup
            return api.SetWallpaper(monitor!, path) == 0 ? 0 : 1;
        }, -1, out bool expired);
        if (expired)
            return (1, "Windows n’a pas répondu.");
        if (code == 0)
            return (0, "ok");
        if (code == 2)
            return (2, "Display not found (see 'winlestia wallpaper list').");
        if (code == 3)
            return (1, "Windows n’a pas répondu.");
        if (display <= 0 && SystemParametersInfo(0x14, 0, path, 3))
            return (0, "ok");   // COM unavailable: keep the former behaviour
        return (1, "Windows refused the wallpaper.");
    }

    // Depuis le sélecteur : le chemin vient déjà de la liste scannée (tout format du sélecteur, webp compris), on ne vérifie donc
    // ici que la cohérence d'un chemin local.
    internal static (int code, string text) ApplyKnown(string path, int display, bool light)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 259 || path.StartsWith(@"\\") || !System.IO.Path.IsPathFullyQualified(path)
            || !System.IO.File.Exists(path))
            return (2, "Wallpaper file not found.");
        return Apply(path, display, light);
    }
}
