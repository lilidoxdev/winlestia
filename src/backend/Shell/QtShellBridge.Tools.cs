// Actions behind the dispatch: tools, capture export, palette runs and dialogs.

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

namespace PanneauxGlass.Shell;

internal sealed partial class QtShellBridge
{
    void RunTool(string id)
    {
        switch (id)
        {
            case "capture":
                Send("capture");
                return;
            case "ocr":
                Send("capture-ocr");
                return;
            case "audio":
                Send("audio");
                return;
            case "clipboard":
                Send("clipboard");
                return;
            case "notifications":
                owner.ToggleNotifications();
                return;
        }
        Send("close");
        _ = dispatcher.BeginInvoke(
            (Action)(
                () =>
                    WithDialog(() =>
                    {
                        switch (id)
                        {
                            case "pipette":
                                owner.OpenColorPicker();
                                break;
                            case "ruler":
                                owner.OpenRuler();
                                break;
                            case "pin":
                                owner.ToggleAlwaysOnTop();
                                break;
                            case "largefiles":
                                owner.OpenLargeFiles();
                                break;
                            case "profiles":
                                owner.OpenWorkspaces();
                                break;
                            case "memo":
                                owner.OpenMemo();
                                break;
                            case "help":
                                owner.OpenHelp();
                                break;
                        }
                    })
            )
        );
    }

    async Task<string> CaptureExport(JsonElement args)
    {
        string path = Path.GetFullPath(Text(args, "path")),
            directory = Path.GetFullPath(Path.Combine(cache, "captures")) + Path.DirectorySeparatorChar;
        if (
            !path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path) != ".png"
            || !File.Exists(path)
            || new FileInfo(path).Length > 100_000_000
        )
            throw new InvalidOperationException("Capture invalide.");
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        try
        {
            if (Text(args, "action") == "pin")
            {
                PinWindow.Open(owner, image);
                return Strings.CapturePinned;
            }
            string text = await OcrService.RecognizeAsync(image);
            if (!string.IsNullOrWhiteSpace(text))
            {
                System.Windows.Clipboard.SetText(text);
                Send("clipboard");
                return Strings.TextCopiedToTheClipboard;
            }
            Send("capture");
            return "Aucun texte lisible dans cette zone";
        }
        finally
        {
            File.Delete(path);
        }
    }

    void WithDialog(Action action)
    {
        DialogOpen = true;
        try
        {
            action();
        }
        finally
        {
            DialogOpen = false;
        }
    }

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
        _ = dispatcher.BeginInvoke(
            (Action)(
                () =>
                    WithDialog(() =>
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
                    })
            )
        );
    }
}
