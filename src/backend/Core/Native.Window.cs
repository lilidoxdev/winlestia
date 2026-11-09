// language: C# 12, file: Native.Window.cs, target: Windows 11 / .NET 8, verre natif et icônes du shell
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal static partial class Native
{
    // schtasks peut durer jusqu'à 15 s : exécuté en fond, l'état demandé reste visible via Autostart pendant ce temps.
    internal static void SetAutostart(bool value)
    {
        // Nom historique conservé pour compatibilité (valeur "Panneaux" de la clé Run des installations existantes).
        System.Threading.Volatile.Write(ref autostartDesired, value ? 1 : 0);
        System.Threading.Tasks.Task.Run(() =>
        {
            lock (autostartGate)
            {
                try
                {
                    if (value)
                    {
                        if (AutostartTask.Create())
                            DropRun(
            );
                        else
                        {
                            Store.Log(
            "Démarrage: tâche indisponible, repli sur la clé Run.");
                            using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run");
                            key.SetValue("Panneaux",
            "\"" + Environment.ProcessPath + "\"");
                        }
                    }
                    else
                    {
                        AutostartTask.Delete();
                        DropRun();
                    }
                }
                catch (Exception ex)
                {
                    Store.Log(
            "Démarrage: " + ex.Message);
                }
                finally { System.Threading.Volatile.Write(ref autostartDesired, -1); }
            }
        });
    }
    internal static bool DesktopHidden()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return Convert.ToInt32(key?.GetValue("HideIcons", 0) ?? 0) != 0;
    }
    internal static void Desktop(bool hidden)
    {
        string helper = Path.Combine(Store.Root, "SECOURS_BUREAU.exe");
        if (!File.Exists(helper))
            throw new FileNotFoundException("Le composant natif du Bureau est absent.");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(helper, hidden ? "--hide --silent"
            : "--show-only --silent")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process == null || !process.WaitForExit(3000) || process.ExitCode != 0)
            throw new InvalidOperationException("Le Bureau Windows n’a pas appliqué le réglage.");
        if (DesktopHidden() != hidden || !DesktopView.Enforce(hidden))
            throw new InvalidOperationException("Le Bureau Windows n’a pas retenu le réglage.");
    }
}
internal sealed class IconLoader : IIconSource, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Size
    {
        public int Width, Height;
    }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path,
        IntPtr bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IImageFactory factory);
    readonly BlockingCollection<(Entry entry, Action<ImageSource> callback)> jobs = new(); readonly ConcurrentDictionary<string,
        ImageSource> images = new();
    sealed record Request(Action<ImageSource> Callback, string Scope);
    readonly object pendingLock = new(); readonly Dictionary<string, List<Request>> pending = new(); bool disposed;
    readonly Thread worker; internal int LoadedCount => images.Count; internal int Failures;
    internal IconLoader()
    {
        // Nom historique conservé pour compatibilité (nom de thread vu dans les journaux et débogueurs).
        worker = new Thread(Work) { IsBackground = true, Name = "Panneaux shell icons" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }
    public void Load(Entry entry, Action<ImageSource> callback, string scope = "")
    {
        string key = entry.Id + "|" + entry.IconPath;
        if (images.TryGetValue(key, out var value))
        {
            callback(value);
            return;
        }
        lock (pendingLock)
        {
            if (disposed)
                return;
            if (images.TryGetValue(key, out value))
            {
                callback(value);
                return;
            }
            if (pending.TryGetValue(key, out var listeners))
            {
                listeners.Add(new(callback, scope));
                return;
            }
            pending[key] = new() { new(callback, scope) };
            jobs.Add((entry, callback));
        }
    }
    internal void CancelScope(string scope)
    {
        lock (pendingLock)
        {
            foreach (var key in pending.Keys.ToArray())
            {
                pending[key].RemoveAll(r => r.Scope == scope);
                if (pending[key].Count == 0)
                    pending.Remove(key);
            }
        }
    }
    void Work()
    {
        foreach (var job in jobs.GetConsumingEnumerable())
        {
            string key = job.entry.Id + "|" + job.entry.IconPath;
            lock (pendingLock)
            {
                if (disposed)
                    break;
                if (!pending.ContainsKey(key))
                    continue;
            }
            if (!images.TryGetValue(key, out var image))
            {
                image = ExtractOrFallback(key, job.entry);
            }
            Request[] listeners;
            lock (pendingLock)
            {
                listeners = pending.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<Request>();
                pending.Remove(key);
            }
            var result = image;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
            {
                if (
                !disposed)
                    foreach (var listener in listeners)
                        listener.Callback(result);
            }));
        }
    }
    // Une tuile lettre ou dossier remplace une extraction échouée mais n'est jamais retenue comme icône : le chargement
    // suivant, après le délai de nouvel essai (ou un rechargement du catalogue), extrait de nouveau.
    readonly ConcurrentDictionary<string, (DateTime At, int Count, ImageSource Plate, Entry Entry, DateTime Stamp)> failed = new(
        ); readonly ConcurrentDictionary<string, byte> logged = new(
        ); readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, object> placeholders = new();
    ImageSource ExtractOrFallback(string key, Entry entry)
    {
        int count = 0;
        if (failed.TryGetValue(key, out var previous))
        {
            count = previous.Count;
            if (!CatalogueWatchRules.IconRetryDue(count, previous.At, DateTime.UtcNow))
                return previous.Plate;
        }
        var real = Extract(entry);
        if (real != null)
        {
            images[key] = real;
            failed.TryRemove(key, out _);
            return real;
        }
        if (Directory.Exists(entry.Target))
        {
            var folder = FolderFallback();
            images[key] = folder;
            return folder;
        }
        Failures++;
        var plate = InitialPlate(entry);
        placeholders.Add(plate, new object());
        failed[key] = (DateTime.UtcNow, count + 1, plate, entry, SourceStamp(entry));
        if (logged.TryAdd(key, 0))
            Store.Log("Icône de secours: " + entry.Name);
        return plate;
    }
    public bool IsPlaceholder(ImageSource image) => placeholders.TryGetValue(image, out _);
    public int FailureCount(Entry entry) => failed.TryGetValue(entry.Id + "|" + entry.IconPath, out var f) ? f.Count : 0;
    // Date d'écriture la plus récente de la source d'icône et de la cible (fichiers locaux seulement) ; une source qui
    // apparaît ou change donne à son icône une nouvelle série d'essais.
    static DateTime SourceStamp(Entry entry)
    {
        DateTime stamp = DateTime.MinValue;
        foreach (string path in new[] { entry.IconPath, entry.Target })
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathRooted(path))
                    continue;
                DateTime at = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(
                    path) : DateTime.MinValue;
                if (at > stamp)
                    stamp = at;
            }
            catch (Exception)
            {
                // Entrée récente illisible : ignorée.
            }
        }
        return stamp;
    }
    public void ResetFailures()
    {
        foreach (var item in failed)
            if (SourceStamp(item.Value.Entry) != item.Value.Stamp)
                failed.TryRemove(item.Key, out _);
    }
    static ImageSource? Extract(Entry entry)
    {
        var paths = CatalogueWatchRules.PreferTargetForIcon(entry.IconPath) ? new[] { entry.Target, entry.IconPath }
            : new[] { entry.IconPath, entry.Target };
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            try
            {
                if (File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".ico")
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(path, UriKind.Absolute);
                    bi.DecodePixelWidth = 96;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                Guid iid = typeof(IImageFactory).GUID;
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                if (hr != 0)
                    continue;
                try
                {
                    if (factory.GetImage(new Size { Width = 96, Height = 96 }, 0x100 | 0x4, out var bitmap) == 0 && bitmap != IntPtr.Zero)
                    {
                        try
                        {
                            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            source.Freeze();
                            return source;
                        }
                        finally { Native.DeleteObject(bitmap); }
                    }
                }
                finally { Marshal.ReleaseComObject(factory); }
            }
            catch (Exception ex) { Store.Log("Icône " + path + ": " + ex.Message); }
        }
        return null;
    }
    internal static ImageSource? Sample(Entry entry) => Extract(entry);
    static ImageSource FolderFallback()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(248, 194, 78)), null, new Rect(5, 23, 80, 58), 9, 9);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 215, 118)), null, new Rect(5, 16, 38, 20), 6, 6);
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
    // Un logo d'application manquant devient une tuile lettre, jamais un dossier jaune : une ligne se lit toujours
    // comme une appli précise, et la palette n'affiche jamais d'emplacement vide.
    internal static ImageSource InitialPlate(Entry entry)
    {
        var colors = new[] { Color.FromRgb(0x3D, 0x7E, 0xA6), Color.FromRgb(0xC2, 0x5B, 0x35), Color.FromRgb(0x4C, 0x8F, 0x6D),
            Color.FromRgb(0x7A, 0x5C, 0xA8), Color.FromRgb(0xC0, 0x8A, 0x2E), Color.FromRgb(0xB0, 0x4A, 0x6A) };
        string name = string.IsNullOrWhiteSpace(entry.Name) ? "?" : entry.Name.Trim();
        Color fill = colors[Math.Abs(name.ToUpperInvariant()[0] * 31 + name.Length * 17) % colors.Length];
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(fill), null, new Rect(3, 3, 90, 90), 24, 24);
            string letter = name[..1].ToUpperInvariant();
            var text = new FormattedText(letter, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 50, Brushes.White, 1.0);
            dc.DrawText(text, new Point((96 - text.Width) / 2, (96 - text.Height) / 2));
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
    public void Dispose()
    {
        lock (pendingLock)
        {
            disposed = true;
            pending.Clear();
            jobs.CompleteAdding();
        }
    }
}
