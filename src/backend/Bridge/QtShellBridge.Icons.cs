// language: C# 12, file: QtShellBridge.Icons.cs, target: Windows 11 / .NET 8, icônes, palette, tâches et entrées
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    object EntryValue(Entry e)
        => new
        {
            id = e.Id,
            name = e.Name,
            target = e.Target,
            category = e.Category,
            board = Store.BoardFor(e),
            favorite = Store.State.Favorites.Contains(e.Id),
            recent = Store.State.Recent.IndexOf(e.Id),
            kind = Store.BoardFor(e) is "projects" or "documents" ? "folder" : "app"
        };

    void RunPalette(string kind, string id, string query)
    {
        if (kind == "systemRecent")
        {
            lock (DesktopRecents.Entries)
                if (DesktopRecents.Entries.TryGetValue(id, out var recent))
                    owner.Launch(recent);
            return;
        }
        if (kind == "app")
        {
            var entry = Store.Visible().FirstOrDefault(e => e.Id == id);
            if (entry != null)
                owner.Launch(entry);
            return;
        }
        if (kind == "quickTab")
        {
            var tab = Store.State.QuickTabs.FirstOrDefault(t => t.Id == id);
            if (tab != null)
                owner.RunQuickTab(tab);
            return;
        }
        if (kind == "website")
        {
            var site = Store.State.Websites.FirstOrDefault(w => w.Id == id);
            if (site != null)
                owner.LaunchTarget(site.Url);
            return;
        }
        if (kind == "web")
        {
            owner.WebSearch(query);
            return;
        }
        if (kind != "command")
            return;
        _ = dispatcher.BeginInvoke((Action)(() => WithDialog(() =>
        {
            switch (id)
            {
                case "capture":
                    owner.OpenCapture();
                    break;
                case "notes":
                    owner.OpenNotes();
                    break;
                case "focus":
                    owner.OpenFocus();
                    break;
                case "audio":
                    owner.OpenAudio();
                    break;
                case "profiles":
                    owner.OpenWorkspaces();
                    break;
                case "quicktabs":
                    owner.EditQuickTabs();
                    break;
                case "tools":
                    owner.OpenTools();
                    break;
                case "taskmgr":
                    SystemActions.TaskManager(owner);
                    break;
                case "calculator":
                    owner.LaunchTarget("calc.exe");
                    break;
            }
        })));
    }

    void LoadIcon(Entry entry)
    {
        if (!requested.Add(entry.Id))
            return;
        owner.Icons.Load(entry, source =>
        {
            if (disposed)
                return;
            BitmapSource bitmap;
            if (source is BitmapSource native)
                bitmap = native;
            else
            {
                var image = new System.Windows.Media.DrawingVisual();
                using (var dc = image.RenderOpen())
                    dc.DrawImage(source, new System.Windows.Rect(0, 0, 64, 64));
                var target = new RenderTargetBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                target.Render(image);
                target.Freeze();
                bitmap = target;
            }
            bool fallback = owner.Icons.IsPlaceholder(source);
            string file =
                Path.Combine(cache,
                             "icons",
                             Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Id + entry.IconPath + entry.Target)))[..24]
                                 + (fallback ? ".fb.png" : ".png"));
            _ = Task.Run(() =>
            {
                try
                {
                    SaveImage(bitmap, file);
                    dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (disposed)
                            return;
                        string previous = icons.GetValueOrDefault(entry.Id, "");
                        icons[entry.Id] = file;
                        iconRevision++;
                        if (fallback)
                            RetryIcon(entry);
                        else
                        {
                            placeholderIcons.Remove(entry.Id);
                            if (previous.EndsWith(".fb.png", StringComparison.Ordinal))
                            {
                                Safe.Try(() => File.Delete(previous));
                            }
                        }
                    }));
                }
                catch (Exception ex)
                {
                    dispatcher.BeginInvoke((Action)(() => requested.Remove(entry.Id)));
                    Store.Log("Icon Qt: " + ex.Message);
                }
            });
        }, "qt");
    }

    // Une vignette de remplacement vient d'un fichier .fb.png séparé, remplacé par le vrai PNG dès qu'un nouvel essai borné réussit.
    readonly HashSet<string> placeholderIcons = new();
    void RetryIcon(Entry entry)
    {
        placeholderIcons.Add(entry.Id);
        if (CatalogueWatchRules.IconRetryDelay(owner.Icons.FailureCount(entry)) is not TimeSpan delay)
            return;
        _ = Task.Delay(delay).ContinueWith(_ => dispatcher.BeginInvoke((Action)(() =>
        {
            if (disposed || !placeholderIcons.Contains(entry.Id))
                return;
            requested.Remove(entry.Id);
            LoadIcon(Store.Entries.FirstOrDefault(e => e.Id == entry.Id) ?? entry);
        })));
    }
    // Catalogue relu : les icônes en échec retrouvent une série d'essais.
    internal void CatalogueChanged()
    {
        dispatcher.BeginInvoke((Action)(() =>
        {
            if (disposed)
                return;
            owner.Icons.ResetFailures();
            foreach (string id in placeholderIcons.ToArray())
            {
                var entry = Store.Entries.FirstOrDefault(e => e.Id == id);
                if (entry == null || owner.Icons.FailureCount(entry) > 0)
                    continue;
                requested.Remove(id);
                LoadIcon(entry);
            }
        }));
    }

    static void SaveImage(BitmapSource bitmap, string file)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    void RefreshTasks()
    {
        if (!owner.TaskbarEnabled || (!active && !dockVisible) || DateTime.UtcNow < nextTasks)
            return;
        nextTasks = DateTime.UtcNow.AddSeconds(3);
        RefreshDockApps();
    }
}
