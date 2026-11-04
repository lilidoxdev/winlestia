// C# 12 / WallpaperApply.cs / Sets the wallpaper through IDesktopWallpaper (all displays or one) and lists displays. Event-driven: every call builds its COM object on a short-lived STA thread and releases it before returning.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PanneauxGlass.Desktop;

internal sealed record WallpaperDisplay(
    int id,
    string device,
    string name,
    int width,
    int height,
    bool primary,
    string path
);

internal static class WallpaperApply
{
    static readonly Guid Clsid = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);

    // COM needs an STA thread and must stay off the UI thread: run on a dedicated one, abandon it after 5 s (a hung explorer must not freeze the shell).
    sealed class Abandon
    {
        public volatile bool Set;
    }

    static T OnSta<T>(Func<Abandon, T> work, T fallback, out bool expired)
    {
        T result = fallback;
        var abandon = new Abandon();
        var thread = new Thread(() =>
        {
            try
            {
                result = work(abandon);
            }
            catch (Exception ex)
            {
                Store.Log("Fond d’écran: " + ex.Message);
            }
        })
        {
            IsBackground = true,
            Name = "Wallpaper STA",
        };
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

    // Primary first, then the order Windows reports. A display without a usable rectangle (unplugged) is skipped.
    static List<WallpaperDisplay> Enumerate(IDesktopWallpaper api)
    {
        var found = new List<(string device, Native.Rect rect, string path)>();
        if (api.GetMonitorDevicePathCount(out uint count) != 0)
            return new();
        for (uint i = 0; i < count; i++)
        {
            if (
                api.GetMonitorDevicePathAt(i, out string device) != 0
                || string.IsNullOrEmpty(device)
                || api.GetMonitorRECT(device, out var rect) != 0
                || rect.Right <= rect.Left
                || rect.Bottom <= rect.Top
            )
                continue;
            api.GetWallpaper(device, out string? path);
            found.Add((device, rect, path ?? ""));
        }
        var ordered = found.OrderBy(f => f.rect.Left == 0 && f.rect.Top == 0 ? 0 : 1).ToList();
        return ordered
            .Select(
                (f, n) =>
                    new WallpaperDisplay(
                        n + 1,
                        f.device,
                        WallpaperLogic.DisplayName(
                            n + 1,
                            f.rect.Right - f.rect.Left,
                            f.rect.Bottom - f.rect.Top,
                            n == 0
                        ),
                        f.rect.Right - f.rect.Left,
                        f.rect.Bottom - f.rect.Top,
                        n == 0,
                        f.path
                    )
            )
            .ToList();
    }

    static T WithApi<T>(Func<IDesktopWallpaper, Abandon, T> use, T fallback, out bool expired) =>
        OnSta(
            abandon =>
            {
                object? instance = null;
                try
                {
                    instance = Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid)!);
                    return use((IDesktopWallpaper)instance!, abandon);
                }
                finally
                {
                    if (instance != null && Marshal.IsComObject(instance))
                        Marshal.ReleaseComObject(instance);
                }
            },
            fallback,
            out expired
        );

    internal static List<WallpaperDisplay> List() =>
        WithApi((api, _) => Enumerate(api), new List<WallpaperDisplay>(), out _);

    // Process names are read once per call (picker opening, applying, CLI list); nothing watches them.
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
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
        return WallpaperLogic.Animated(names);
    }

    // display 0 = all displays. Normal effects: IDesktopWallpaper.SetWallpaper, which is the call Windows itself makes for its fade; light effects: SPI_SETDESKWALLPAPER, no extra transition requested.
    // An unplugged or unknown display is a refusal with a message, never an exception.
    internal static (int code, string text) Apply(string path, int display, bool light)
    {
        if (display <= 0 && light)
            return SystemParametersInfo(0x14, 0, path, 3) ? (0, "ok") : (1, "Windows refused the wallpaper.");
        int code = WithApi(
            (api, abandon) =>
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
                    return 3; // the caller already gave up: do not change the wallpaper after the fact
                return api.SetWallpaper(monitor!, path) == 0 ? 0 : 1;
            },
            -1,
            out bool expired
        );
        if (expired)
            return (1, "Windows n’a pas répondu.");
        if (code == 0)
            return (0, "ok");
        if (code == 2)
            return (2, "Display not found (see 'winlestia wallpaper list').");
        if (code == 3)
            return (1, "Windows n’a pas répondu.");
        if (display <= 0 && SystemParametersInfo(0x14, 0, path, 3))
            return (0, "ok"); // COM unavailable: keep the former behaviour
        return (1, "Windows refused the wallpaper.");
    }

    // From the picker: the path already comes from the scanned list (any picker format, webp included), so only local-path sanity is checked here.
    internal static (int code, string text) ApplyKnown(string path, int display, bool light)
    {
        if (
            string.IsNullOrWhiteSpace(path)
            || path.Length > 259
            || path.StartsWith(@"\\")
            || !System.IO.Path.IsPathFullyQualified(path)
            || !System.IO.File.Exists(path)
        )
            return (2, "Wallpaper file not found.");
        return Apply(path, display, light);
    }
}
