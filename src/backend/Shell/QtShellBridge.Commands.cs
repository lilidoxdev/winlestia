// Command dispatch for the Qt frontend: JSON requests received on the pipe.

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
    static string Text(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    static double Number(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.TryGetDouble(out double number) && double.IsFinite(number)
            ? number
            : 0;

    static bool Flag(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    string? lateNotice;
    object? lateWallpaperPicker;

    async Task<string> Handle(JsonElement request)
    {
        string? notice = null;
        object? wallpaperCapture = null;
        object? bookmarksItems = null;
        object? windowItems = null;
        object? wallpaperPicker = null;
        object? fileItems = null;
        try
        {
            string command = Text(request, "command");
            var args = request.TryGetProperty("args", out var a) ? a : default;
            string id = args.ValueKind == JsonValueKind.Object ? Text(args, "id") : "";

            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("epoch", out _))
            {
                long epoch = (long)Number(args, "epoch");
                bool navigationChanged = epoch != frontendEpoch;
                if (navigationChanged)
                {
                    DesktopRecents.RefreshSoon();
                    dockPreview.Close();
                }
                if (Interlocked.Exchange(ref firstReply, 1) == 0)
                    Store.Log(
                        "Premier affichage Qt · t+"
                            + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds
                            + " ms"
                    );
                frontendEpoch = epoch;
                frontendWindowPid = (uint)Number(args, "pid");
                bool shown = Flag(args, "visible");
                tiler.ExcludedProcess = frontendWindowPid;
                explorer.ExcludedProcess = frontendWindowPid;
                tiler.SetEnabled(
                    Store.State.AutoTile && !Program.QtReview && !Program.QtVerify && !Program.QtVerifyTools
                );
                if (Store.State.ScreenFrame && !Program.QtVerifyTools)
                    frame.Enable();
                else
                    frame.Disable();
                if (!shown)
                    CapturingShortcut = false;
                page = (int)Number(args, "page");
                dockVisible = Flag(args, "dock");
                controls = Flag(args, "controls");
                mixerVisible = Flag(args, "mixer");
                wifiVisible = Flag(args, "wifi");
                barsVisible = Flag(args, "bars");
                if (active != shown || navigationChanged)
                {
                    active = shown;
                    dismissal.SetActive(active);
                }
                lastReply = DateTime.UtcNow;
                wifi.SetOpen(shown && wifiVisible && !Quiet);
                UpdateSampling();
            }
            if (command == "dictation")
            {
                var d = dictation.Snapshot();
                return JsonSerializer.Serialize(
                    new
                    {
                        dictation = new
                        {
                            state = d.State,
                            level = d.Level,
                            levels = d.Levels,
                            lang = d.Lang,
                            text = d.Text,
                            error = d.Error,
                        },
                    },
                    Json
                );
            }
            if (command == "audioBars")
            {
                var head = bars.Snapshot(false);
                var v = head.Seq == (int)Number(args, "seq") ? head : bars.Snapshot(true);
                return JsonSerializer.Serialize(
                    new
                    {
                        audioBars = new
                        {
                            seq = v.Seq,
                            idle = v.Idle,
                            bars = v.Bars.Length > 0 ? v.Bars : null,
                        },
                    },
                    Json
                );
            }
            if (command == "visualizer")
                return JsonSerializer.Serialize(new { spectrum = spectrum.Values }, Json);

            switch (command)
            {
                case "state":
                    break;
                case "clipboardFiles":
                    if (args.TryGetProperty("paths", out var clipPaths))
                    {
                        RecycleActions.ClipboardFiles(
                            clipPaths.EnumerateArray().Select(p => p.GetString() ?? "").ToArray()
                        );
                        notice = Strings.FilesCopiedToTheClipboard;
                    }
                    break;
                case "zipFiles":
                    if (args.TryGetProperty("paths", out var zipPaths))
                    {
                        var files = zipPaths.EnumerateArray().Select(p => p.GetString() ?? "").ToArray();
                        string? destination = RecycleActions.ChooseZip(files);
                        if (destination != null)
                        {
                            await Task.Run(() => RecycleActions.Zip(files, destination));
                            notice = Strings.ZIPArchiveCreated;
                        }
                    }
                    break;
                case "explorerPalette":
                    notice = null;
                    await Task.Run(() =>
                        explorer.Sync(Text(args, "accent"), Text(args, "background"), Text(args, "text"))
                    );
                    break;
                case "explorerFolder":
                    using (
                        var dialog = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = Strings.ChooseTheFolderToCustomize,
                            UseDescriptionForTitle = true,
                        }
                    )
                    {
                        if (
                            dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                            && !Store.State.ExplorerFolders.Contains(dialog.SelectedPath)
                        )
                        {
                            Store.State.ExplorerFolders.Add(dialog.SelectedPath);
                            Store.Save();
                        }
                    }
                    break;
                case "explorerRestore":
                    Store.State.ExplorerTheme = false;
                    Store.Save();
                    notice = await Task.Run(() => explorer.Restore());
                    break;
                case "explorerReport":
                    owner.LaunchTarget(
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Winlestia",
                            "Explorer",
                            "last-report.txt"
                        )
                    );
                    break;
                case "explorerOpen":
                    owner.LaunchTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    break;
                case "emptyRecycleBin":
                    RecycleActions.Empty();
                    break;
                case "desktopSwitch":
                {
                    int rel = (int)Math.Clamp(Number(args, "relative"), -1, 1),
                        to = (int)Number(args, "index");
                    _ = Task.Run(() => VirtualDesktops.SwitchTo(to, rel));
                    break;
                }
                case "desktopNew":
                    _ = Task.Run(() => VirtualDesktops.CreateNew());
                    break;
                case "desktopClose":
                {
                    int at = (int)Number(args, "index") - 1;
                    _ = Task.Run(() => VirtualDesktops.Close(at));
                    break;
                }
                case "recycleFiles":
                    if (args.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
                        RecycleActions.Recycle(paths.EnumerateArray().Select(p => p.GetString() ?? "").ToArray());
                    break;
                case "wallpaperCapture":
                {
                    Native.Rect? shotTarget =
                        args.ValueKind == JsonValueKind.Object && Number(args, "w") > 0
                            ? new Native.Rect
                            {
                                Left = (int)Number(args, "x"),
                                Top = (int)Number(args, "y"),
                                Right = (int)(Number(args, "x") + Number(args, "w")),
                                Bottom = (int)(Number(args, "y") + Number(args, "h")),
                            }
                            : null;
                    string? shot = null;
                    try
                    {
                        shot = await Task.Run(() =>
                            {
                                try
                                {
                                    return WallpaperMedia.CaptureHost(shotTarget);
                                }
                                catch (Exception ex)
                                {
                                    Store.Log("Capture du fond: " + ex.Message);
                                    return null;
                                }
                            })
                            .WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    catch (TimeoutException)
                    {
                        Store.Log("Capture du fond: delai depasse.");
                    }
                    wallpaperCapture = new
                    {
                        token = Text(args, "token"),
                        ok = shot != null,
                        path = shot ?? "",
                    };
                    break;
                }
                case "wallpaperChanged":
                    wallpaper.Notify(100, true);
                    break;
                case "dockApp":
                    notice = DockAction(args);
                    nextTasks = DateTime.MinValue;
                    break;
                case "dockPreview":
                    DockPreviewOpen(args);
                    break;
                case "dockPreviewEnd":
                    DockPreviewEnd(args);
                    break;
                case "systemAction":
                    DockSystem(Text(args, "id"), Flag(args, "confirmed"));
                    break;
                case "dictationCapture":
                    if (Flag(args, "enabled"))
                        dictation.BeginCapture();
                    else
                        dictation.EndCapture();
                    break;
                case "captureShortcutMode":
                    CapturingShortcut = Flag(args, "enabled");
                    break;

                case "runningRefresh":
                    RefreshBackgroundApps();
                    nextTasks = DateTime.MinValue;
                    break;
                case "icon":
                    var iconEntry = Store.Entries.FirstOrDefault(e => e.Id == id) ?? dockEntries.GetValueOrDefault(id);
                    if (iconEntry == null)
                    {
                        lock (DesktopRecents.Entries)
                            DesktopRecents.Entries.TryGetValue(id, out iconEntry);
                    }
                    if (iconEntry != null)
                        LoadIcon(iconEntry);
                    break;

                case "openFiles":
                    FilesDefaultHandler.Open();
                    break;
                case "launch":
                    var entry = Store.Visible().FirstOrDefault(e => e.Id == id);
                    if (entry != null)
                    {
                        Send("close");
                        owner.Launch(entry);
                    }
                    break;

                case "activate":
                    long hwnd = (long)Number(args, "hwnd");
                    var app = TaskbarNative.Applications().FirstOrDefault(w => w.Handle.ToInt64() == hwnd);
                    if (app != null)
                        TaskbarNative.Activate(app);
                    else if (dockEntries.TryGetValue(id, out var pin))
                        owner.Launch(pin);
                    break;

                case "media":
                    await media.CommandAsync(Text(args, "action"), Number(args, "value"));
                    break;

                case "mediaVolume":
                    await mixer.SetMediaVolume(Math.Clamp(Number(args, "value"), 0, 100));
                    break;
                case "volume":
                    audio.SetVolume(Math.Clamp(Number(args, "value"), 0, 100));
                    sound = audio.Read();
                    break;

                case "brightness":
                {
                    double brightLevel = Math.Clamp(Number(args, "value"), 0, 100);
                    if (args.TryGetProperty("id", out _))
                        _ = SetDisplay((int)Number(args, "id"), brightLevel, false);
                    else
                    {
                        Native.GetCursorPos(out var point);
                        await MonitorBrightness.SetAsync(point, brightLevel);
                        nextBrightness = DateTime.MinValue;
                    }
                    break;
                }

                case "wifiConnect":
                    _ = wifi.ConnectAsync(Text(args, "ssid"), Text(args, "password"));
                    break;
                case "wifiForget":
                    _ = wifi.ForgetAsync(Text(args, "ssid"));
                    break;
                case "wifiPortal":
                    WifiHost.OpenPortal();
                    break;
                case "wifiSettings":
                    WifiHost.OpenSettings(Text(args, "page") == "location");
                    break;
                case "radio":
                    {
                        var radioWifi = Text(args, "kind") == "wifi";
                        try
                        {
                            await connections.ToggleAsync(radioWifi ? RadioKind.WiFi : RadioKind.Bluetooth);
                        }
                        catch (RadioAccessDeniedException)
                        {
                            if (radioWifi ? !wifiSettingsOpened : !bluetoothSettingsOpened)
                            {
                                if (radioWifi)
                                    wifiSettingsOpened = true;
                                else
                                    bluetoothSettingsOpened = true;
                                try
                                {
                                    Process
                                        .Start(
                                            new ProcessStartInfo(
                                                radioWifi ? "ms-settings:network-wifi" : "ms-settings:bluetooth"
                                            )
                                            {
                                                UseShellExecute = true,
                                            }
                                        )
                                        ?.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    Store.Log("Connection settings: " + ex.Message);
                                }
                            }
                            throw;
                        }
                    }
                    connection = await connections.ReadAsync();
                    break;

                case "customTheme":
                    if (!args.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException("Palette invalide.");
                    var custom = new Dictionary<string, string>();
                    foreach (string role in new[] { "background", "text", "primary", "secondary", "tertiary" })
                    {
                        string color = Text(colors, role);
                        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                            throw new InvalidOperationException("Couleur invalide : " + role);
                        custom[role] = color.ToLowerInvariant();
                    }
                    Store.State.CustomPalette = custom;
                    Store.State.CustomGlass = Flag(args, "glass");
                    Store.State.CustomGlassOpacity = Math.Clamp(Number(args, "opacity"), .02, .95);
                    Store.State.Theme = "custom";
                    Store.State.AutoTheme = false;
                    Store.Save();
                    owner.SetTheme("custom");
                    break;
                case "theme":
                    Store.State.AutoTheme = Text(args, "mode") == "auto";
                    if (Text(args, "mode") == "custom")
                    {
                        Store.State.Theme = "custom";
                        owner.SetTheme("custom");
                    }
                    else if (!Store.State.AutoTheme)
                        owner.SetTheme(Flag(args, "dark") ? "nuit" : "shell");
                    else
                        owner.SetTheme(Store.State.Theme);
                    Store.Save();
                    break;

                case "workspace":
                    Send("workspace:" + (id is "apps" or "games" or "projects" or "documents" ? id : "all"));
                    break;

                case "palette":
                    Send("palette");
                    break;

                case "settings":
                    Send("settings");
                    if (Text(args, "section") == "social")
                        Send("settings:social");
                    else if (Text(args, "section") == "bookmarks")
                        Send("settings:bookmarks");
                    break;
                case "socialOpen":
                    _ = social.Open(id, false);
                    break;
                case "socialLogin":
                    _ = social.Open(id, true);
                    break;
                case "socialEnable":
                    _ = social.Enable(id, Flag(args, "enabled"));
                    break;
                case "socialLogout":
                    _ = social.Logout(id);
                    break;
                case "socialMute":
                    social.MuteNet(id, Flag(args, "muted"));
                    break;
                case "socialGlobalMute":
                    social.GlobalMute(Flag(args, "muted"));
                    break;
                case "socialSound":
                    social.SoundEnabled(Flag(args, "enabled"));
                    break;
                case "socialVolume":
                    social.Volume(Number(args, "value"));
                    break;
                case "socialHistoryClear":
                    social.HistoryClear();
                    break;
                case "bookmarksOpen":
                    bookmarks.Open(id);
                    break;
                case "bookmarksBrowser":
                    bookmarks.ChooseBrowser(id);
                    break;
                case "bookmarksHide":
                    bookmarks.Hide(id);
                    break;
                case "bookmarksUnhideAll":
                    bookmarks.UnhideAll();
                    break;
                case "bookmarksDeleteInBrowser":
                    _ = bookmarks.DeleteInBrowser(id, Flag(args, "confirmSync"));
                    break;
                case "bookmarksItems":
                    bookmarksItems = bookmarks.Items();
                    break;
                // detached: the pipe is served one request at a time, COM may take 5 s
                case "wallpaperPicker":
                    _ = Task.Run(() =>
                    {
                        var picked = new
                        {
                            displays = WallpaperApply
                                .List()
                                .Select(d => new
                                {
                                    d.id,
                                    d.name,
                                    d.width,
                                    d.height,
                                    d.primary,
                                    d.path,
                                })
                                .ToArray(),
                            animated = WallpaperApply.Animated(),
                        };
                        dispatcher.BeginInvoke((Action)(() => lateWallpaperPicker = picked));
                    });
                    break;
                // detached, answered with the next reply
                case "wallpaperApply":
                {
                    string wp = Text(args, "path");
                    int wd = (int)Number(args, "display");
                    bool light = LightRendering;
                    _ = Task.Run(() =>
                    {
                        var done = WallpaperApply.ApplyKnown(wp, wd, light);
                        var names = WallpaperApply.Animated();
                        dispatcher.BeginInvoke(
                            (Action)(
                                () =>
                                {
                                    if (done.code == 0)
                                        wallpaper.Notify(100, true);
                                    else
                                        lateNotice = done.text;
                                    lateWallpaperPicker = new { animated = names };
                                }
                            )
                        );
                    });
                    break;
                }
                case "windowList":
                    windowItems = await Task.Run(() => PaletteWindows().Take(60).ToArray());
                    break;
                case "fileSearch":
                    fileSearch.Start(
                        Text(args, "term"),
                        (long)Number(args, "gen"),
                        () =>
                            _ = dispatcher.BeginInvoke(
                                (Action)(
                                    () =>
                                    {
                                        if (!disposed)
                                            Send("files");
                                    }
                                )
                            )
                    );
                    break;
                case "fileResults":
                    fileItems = fileSearch.Payload();
                    break;
                case "fileCancel":
                    fileSearch.Cancel();
                    break;
                case "fileOpen":
                    if (fileSearch.TryResolve((long)Number(args, "gen"), (int)Number(args, "index"), out var filePath))
                    {
                        try
                        {
                            if (FileQuery.IsExecutable(filePath))
                            {
                                var selectInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                                selectInfo.ArgumentList.Add("/select," + filePath.Replace("\"", ""));
                                Process.Start(selectInfo);
                            }
                            else
                                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                        }
                        catch
                        {
                            notice = Strings.CouldNotOpenThisFile;
                        }
                    }
                    break;
                case "bookmarksRefresh":
                    bookmarks.Refresh(Flag(args, "ifDirty"));
                    break;
                case "socialTestSound":
                    social.TestSound();
                    break;

                case "copyText":
                    string copied = Text(args, "text");
                    if (copied.Length <= 512)
                        System.Windows.Clipboard.SetText(copied);
                    break;

                case "paletteRun":
                    RunPalette(Text(args, "kind"), id, Text(args, "query"));
                    break;

                case "clipboardCopy":
                    var clip = owner.QtClipboard.Items.FirstOrDefault(e => e.Id == id);
                    if (clip != null)
                        owner.QtClipboard.CopyBack(clip);
                    break;

                case "clipboardPin":
                    var pinnedClip = owner.QtClipboard.Items.FirstOrDefault(e => e.Id == id);
                    if (pinnedClip != null)
                        owner.QtClipboard.TogglePin(pinnedClip);
                    break;

                case "refreshCatalogue":
                    owner.RefreshCatalogue();
                    break;

                case "move":
                    string board = Text(args, "board");
                    if (board is "apps" or "games" or "projects" or "documents" && Store.Entries.Any(e => e.Id == id))
                    {
                        Store.State.BoardOverrides[id] = board;
                        Store.Save();
                    }
                    break;

                case "editEntry":
                    var edited = Store.Entries.FirstOrDefault(e => e.Id == id);
                    if (edited != null)
                    {
                        string name = Text(args, "name").Trim(),
                            category = Text(args, "category").Trim();
                        if (name.Length is > 0 and <= 120)
                        {
                            edited.Name = name;
                            Store.State.Names[id] = name;
                        }
                        if (category.Length <= 100)
                        {
                            edited.Category = category;
                            Store.State.Categories[id] = category;
                        }
                        Store.Save();
                    }
                    break;

                case "entryIcon":
                    var changedIcon = Store.Entries.FirstOrDefault(e => e.Id == id);
                    string path = Text(args, "path");
                    if (
                        changedIcon != null
                        && File.Exists(path)
                        && new[] { ".png", ".ico", ".jpg", ".jpeg", ".webp" }.Contains(
                            Path.GetExtension(path).ToLowerInvariant()
                        )
                    )
                    {
                        changedIcon.IconPath = path;
                        Store.State.Icons[id] = path;
                        Store.Save();
                        requested.Remove(id);
                        LoadIcon(changedIcon);
                    }
                    break;

                case "addAccess":
                    Send("close");
                    string addBoard = Text(args, "board");
                    _ = dispatcher.BeginInvoke(
                        (Action)(
                            () =>
                                WithDialog(() =>
                                    owner.AddAccess(
                                        addBoard is "apps" or "games" or "projects" or "documents" ? addBoard : "apps"
                                    )
                                )
                        )
                    );
                    break;

                case "addFolder":
                    Send("close");
                    string folderBoard = Text(args, "board");
                    _ = dispatcher.BeginInvoke(
                        (Action)(
                            () =>
                                WithDialog(() =>
                                    owner.AddFolder(
                                        folderBoard is "apps" or "games" or "projects" or "documents"
                                            ? folderBoard
                                            : "documents"
                                    )
                                )
                        )
                    );
                    break;

                case "preference":
                    string preference = Text(args, "key");
                    switch (preference)
                    {
                        case "autoTile":
                            Store.State.AutoTile = Flag(args, "enabled");
                            tiler.SetEnabled(Store.State.AutoTile);
                            break;
                        case "tileExclusionRemove":
                            tiler.RemoveExclusion(Text(args, "value"));
                            break;
                        case "accentBorders":
                            Store.State.AccentBorders = Flag(args, "enabled");
                            break;
                        case "activeBorderOnly":
                            Store.State.ActiveBorderOnly = Flag(args, "enabled");
                            explorer.RefreshFrames();
                            break;
                        case "explorerTheme":
                            Store.State.ExplorerTheme = Flag(args, "enabled");
                            if (!Store.State.ExplorerTheme)
                                notice = await Task.Run(() => explorer.Restore());
                            break;
                        case "desktopLogo":
                            Store.State.DesktopLogo = Flag(args, "enabled");
                            break;
                        case "osd":
                            Store.State.Osd = Flag(args, "enabled");
                            break;
                        case "quietFullscreen":
                            Store.State.QuietFullscreen = Flag(args, "enabled");
                            ApplyQuiet();
                            break;
                        case "desktopIndicator":
                            Store.State.DesktopIndicator = Flag(args, "enabled");
                            break;
                        case "dockDrawerOpen":
                            Store.State.DockDrawerOpen = Flag(args, "enabled");
                            break;
                        case "filesDefaultHandler":
                        {
                            bool wantHandler = Flag(args, "enabled");
                            var handlerResult = await Task.Run(() => FilesDefaultHandler.Set(wantHandler));
                            Store.State.FilesDefaultHandler = handlerResult.pref;
                            notice = handlerResult.notice;
                            break;
                        }
                        case "filesPreload":
                            Store.State.FilesPreload = Flag(args, "enabled");
                            if (Store.State.FilesPreload)
                                FilesPreload.Start();
                            break;
                        case "reducedEffects":
                            Store.State.ReducedEffects = Flag(args, "enabled");
                            owner.SetTheme(Store.State.Theme);
                            break;
                        case "topActivationMargin":
                            Store.State.TopActivationMargin = (int)Math.Clamp(Number(args, "value"), 0, 160);
                            break;
                        case "dockWidth":
                            Store.State.DockWidth = Math.Clamp(Number(args, "value"), 560, 4000);
                            break;
                        case "frameThickness":
                            Store.State.FrameThickness = (int)Math.Round(Math.Clamp(Number(args, "value"), 4, 40));
                            frame.UpdateThickness();
                            break;
                        case "glassOpacity":
                            Store.State.GlassOpacity = Math.Clamp(Number(args, "value"), .02, .95);
                            break;
                        case "motion":
                            Store.State.Motion = Math.Clamp(Number(args, "value"), 0, 1.5);
                            break;
                        case "iconSize":
                            Store.State.IconSize = Math.Clamp(Number(args, "value"), 40, 80);
                            break;
                        case "taskbar":
                            owner.SetTaskbar(Flag(args, "enabled"));
                            break;
                        case "autostart":
                            Native.SetAutostart(Flag(args, "enabled"));
                            break;
                        case "screenFrame":
                            Store.State.ScreenFrame = Flag(args, "enabled");
                            if (Store.State.ScreenFrame)
                                frame.Enable();
                            else
                                frame.Disable();
                            break;
                        case "socialBackground":
                            Store.State.SocialBackground = Flag(args, "enabled");
                            social.BackgroundChanged();
                            break;
                        case "dictationEnabled":
                            Store.State.DictationEnabled = Flag(args, "enabled");
                            dictation.Apply(Store.State.DictationEnabled, Store.State.DictationKey);
                            break;
                        case "dictationKey":
                        {
                            int vk = (int)Number(args, "value");
                            if (vk is > 0 and < 255)
                                Store.State.DictationKey = vk;
                            dictation.Apply(Store.State.DictationEnabled, Store.State.DictationKey);
                            break;
                        }
                        case "clipboardHistory":
                            Store.State.ClipboardHistory = Flag(args, "enabled");
                            owner.ApplyClipboardSettings();
                            break;
                    }
                    Store.Save();
                    break;

                case "shortcut":
                    uint modifiers = (uint)Number(args, "modifiers"),
                        key = (uint)Number(args, "key");
                    if (modifiers is > 0 and <= 15 && key is >= 0x20 and <= 0xFE)
                    {
                        string error;
                        bool success = Text(args, "kind") switch
                        {
                            "palette" => owner.SetPaletteShortcut(modifiers, key, out error),
                            "wallpaper" => owner.SetWallpaperShortcut(modifiers, key, out error),
                            _ => owner.SetShortcut(modifiers, key, "home", out error),
                        };
                        notice = success ? Strings.ShortcutSaved : error;
                        if (success)
                            Send("shortcut-saved:" + Text(args, "kind"));
                    }
                    else
                        notice = "Choisis Ctrl, Alt ou Windows avec une autre touche.";
                    break;

                case "tools":
                    Send("tools");
                    break;

                case "audio":
                    Send("audio");
                    break;
                case "microphone":
                    Send("microphone");
                    break;
                case "sessionVolume":
                    await mixer.SetApplication(id, Number(args, "value"), null);
                    break;
                case "sessionMute":
                    await mixer.SetApplication(id, null, Flag(args, "muted"));
                    break;
                case "deviceVolume":
                    await mixer.SetDevice(id, Number(args, "value"), null, false);
                    break;
                case "deviceMute":
                    await mixer.SetDevice(id, null, Flag(args, "muted"), false);
                    break;
                case "deviceSelect":
                    await mixer.SetDevice(id, null, null, true);
                    sound = audio.Read();
                    break;
                case "masterMute":
                    audio.SetMute(Flag(args, "muted"));
                    sound = audio.Read();
                    break;
                case "toolRun":
                    RunTool(id);
                    break;
                case "captureExport":
                    notice = await CaptureExport(args);
                    break;

                case "clipboard":
                    Send("clipboard");
                    break;

                case "profiles":
                    Send("close");
                    _ = dispatcher.BeginInvoke((Action)(() => WithDialog(owner.OpenWorkspaces)));
                    break;

                case "websites":
                    Send("close");
                    _ = dispatcher.BeginInvoke((Action)(() => WithDialog(() => owner.AddWebsite())));
                    break;

                case "capture":
                    Send("capture");
                    break;

                case "restoreTaskbar":
                    owner.SetTaskbar(false);
                    break;

                case "quit":
                    _ = dispatcher.BeginInvoke((Action)(() => System.Windows.Application.Current.Shutdown()));
                    break;

                case "favorite":
                    if (Store.Entries.Any(e => e.Id == id))
                    {
                        if (!Store.State.Favorites.Remove(id))
                            Store.State.Favorites.Add(id);
                        Store.Save();
                    }
                    break;

                case "reveal":
                    var reveal = Store.Entries.FirstOrDefault(e => e.Id == id);
                    if (reveal != null)
                        owner.Reveal(reveal);
                    break;

                case "edit":
                    var edit = Store.Entries.FirstOrDefault(e => e.Id == id);
                    if (edit != null)
                        _ = dispatcher.BeginInvoke((Action)(() => WithDialog(() => owner.Edit(edit))));
                    break;

                case "website":
                    var site = Store.State.Websites.FirstOrDefault(w => w.Id == id);
                    if (site != null)
                        owner.LaunchTarget(site.Url);
                    break;

                case "raiseMedia":
                    var target = Store.Entries.FirstOrDefault(e =>
                        e.Name.Equals(player?.App, StringComparison.OrdinalIgnoreCase)
                    );
                    if (target != null)
                        owner.Launch(target);
                    break;
            }
        }
        catch (Exception ex)
        {
            notice = ex.Message;
            Store.Log("Commande Qt: " + ex.Message);
        }
        notice ??= lateNotice;
        lateNotice = null;
        wallpaperPicker ??= lateWallpaperPicker;
        lateWallpaperPicker = null;
        var queued = events.Where(e => KeepEvent(e.name, e.epoch, frontendEpoch)).Select(e => e.name).ToArray();
        events.Clear();
        return JsonSerializer.Serialize(
            new
            {
                state = State(),
                events = queued,
                notice,
                wallpaperCapture,
                bookmarksItems,
                windows = windowItems,
                fileResults = fileItems,
                wallpaperPicker,
            },
            Json
        );
    }
}
