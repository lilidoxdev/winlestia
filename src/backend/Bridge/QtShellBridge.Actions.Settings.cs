// language: C# 12, file: QtShellBridge.Actions.Settings.cs, target: Windows 11 / .NET 8, actions du pont : thème, fond d'écran,
// préférences et raccourcis
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    static readonly TimeSpan WallpaperCaptureTimeout = TimeSpan.FromSeconds(3);

    [BridgeAction("wallpaperCapture")]
    async Task OnWallpaperCapture(BridgeCall c)
    {
        Native.Rect? shotTarget =
            c.Args.ValueKind == JsonValueKind.Object && Number(c.Args, "w") > 0
                ? new Native.Rect
                {
                    Left = (int)Number(c.Args, "x"),
                    Top = (int)Number(c.Args, "y"),
                    Right = (int)(Number(c.Args, "x") + Number(c.Args, "w")),
                    Bottom = (int)(Number(c.Args, "y") + Number(c.Args, "h"))
                }
                : null;
        string? shot = null;
        try
        {
            shot = await Task.Run(() =>
            {
                try
                {
                    return wallpaperPort.CaptureWallpaper(shotTarget);
                }
                catch (Exception ex)
                {
                    Store.Log("Capture du fond: " + ex.Message);
                    return null;
                }
            }).WaitAsync(WallpaperCaptureTimeout);
        }
        catch (TimeoutException) { Store.Log("Capture du fond: delai depasse."); }
        c.WallpaperCapture = new
        {
            token = Text(c.Args, "token"),
            ok = shot != null,
            path = shot ?? ""
        };
    }

    [BridgeAction("wallpaperChanged")]
    void OnWallpaperChanged(BridgeCall c)
    {
        wallpaperPort.NotifyWallpaper();
    }

    [BridgeAction("captureShortcutMode")]
    void OnCaptureShortcutMode(BridgeCall c)
    {
        CapturingShortcut = Flag(c.Args, "enabled");
    }

    [BridgeAction("customTheme")]
    void OnCustomTheme(BridgeCall c)
    {
        if (!c.Args.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
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
        Store.State.CustomGlass = Flag(c.Args, "glass");
        Store.State.CustomGlassOpacity = Math.Clamp(Number(c.Args, "opacity"), .02, .95);
        Store.State.Theme = "custom";
        Store.State.AutoTheme = false;
        Store.Save();
        owner.SetTheme("custom");
    }

    [BridgeAction("theme")]
    void OnTheme(BridgeCall c)
    {
        Store.State.AutoTheme = Text(c.Args, "mode") == "auto";
        if (Text(c.Args, "mode") == "custom")
        {
            Store.State.Theme = "custom";
            owner.SetTheme("custom");
        }
        else if (!Store.State.AutoTheme)
        {
            var themeId = Flag(c.Args, "dark") ? "nuit" : "shell";
            Store.State.Theme = themeId;
            owner.SetTheme(themeId);
        }
        else
            owner.SetTheme(Store.State.Theme);
        Store.Save();
    }

    [BridgeAction("workspace")]
    void OnWorkspace(BridgeCall c)
    {
        Send("workspace:" + (c.Id is "apps" or "games" or "projects" or "documents" ? c.Id : "all"));
    }

    [BridgeAction("settings")]
    void OnSettings(BridgeCall c)
    {
        Send("settings");
        if (Text(c.Args, "section") == "social")
            Send("settings:social");
        else if (Text(c.Args, "section") == "bookmarks")
            Send("settings:bookmarks");
    }

    [BridgeAction("wallpaperPicker")]
    void OnWallpaperPicker(BridgeCall c)
    {
        _ = Task.Run(() =>
        {
            var picked = wallpaperPort.WallpaperPicker();
            dispatcher.BeginInvoke((Action)(() => lateWallpaperPicker = picked));
        });
        // Détaché : le pipe sert une requête à la fois et le COM peut prendre 5 s.
    }

    [BridgeAction("wallpaperApply")]
    void OnWallpaperApply(BridgeCall c)
    {
        // Détaché : la réponse partira avec le prochain échange (lateNotice, lateWallpaperPicker).
        string wp = Text(c.Args, "path");
        int wd = (int)Number(c.Args, "display");
        bool light = LightRendering;
        _ = Task.Run(() =>
        {
            var done = wallpaperPort.ApplyWallpaper(wp, wd, light);
            var names = done.animated;
            dispatcher.BeginInvoke((Action)(() =>
            {
                if (done.code == 0)
                    wallpaperPort.NotifyWallpaper();
                else
                    lateNotice = done.text;
                lateWallpaperPicker = new
                {
                    animated = names
                };
            }));
        });
    }

    [BridgeAction("preference")]
    async Task OnPreference(BridgeCall c)
    {
        string preference = Text(c.Args, "key");
        switch (preference)
        {
            case "autoTile":
                Store.State.AutoTile = Flag(c.Args, "enabled");
                tiler.SetEnabled(Store.State.AutoTile);
                break;
            case "tileExclusionRemove":
                tiler.RemoveExclusion(Text(c.Args, "value"));
                break;
            case "accentBorders":
                Store.State.AccentBorders = Flag(c.Args, "enabled");
                break;
            case "activeBorderOnly":
                Store.State.ActiveBorderOnly = Flag(c.Args, "enabled");
                explorer.RefreshFrames();
                break;
            case "explorerTheme":
                Store.State.ExplorerTheme = Flag(c.Args, "enabled");
                if (!Store.State.ExplorerTheme)
                    c.Notice = await Task.Run(() => explorer.Restore());
                break;
            case "desktopLogo":
                Store.State.DesktopLogo = Flag(c.Args, "enabled");
                break;
            case "osd":
                Store.State.Osd = Flag(c.Args, "enabled");
                break;
            case "quietFullscreen":
                Store.State.QuietFullscreen = Flag(c.Args, "enabled");
                ApplyQuiet();
                break;
            case "desktopIndicator":
                Store.State.DesktopIndicator = Flag(c.Args, "enabled");
                break;
            case "dockDrawerOpen":
                Store.State.DockDrawerOpen = Flag(c.Args, "enabled");
                break;
            case "filesDefaultHandler":
                {
                    bool wantHandler = Flag(c.Args, "enabled");
                    var handlerResult = await Task.Run(() => FilesDefaultHandler.Set(wantHandler));
                    Store.State.FilesDefaultHandler = handlerResult.pref;
                    c.Notice = handlerResult.notice;
                    break;
                }
            case "filesPreload":
                Store.State.FilesPreload = Flag(c.Args, "enabled");
                if (Store.State.FilesPreload)
                    FilesPreload.Start();
                break;
            case "reducedEffects":
                Store.State.ReducedEffects = Flag(c.Args, "enabled");
                owner.SetTheme(Store.State.Theme);
                break;
            case "topActivationMargin":
                Store.State.TopActivationMargin = (int)Math.Clamp(Number(c.Args, "value"), 0, 160);
                break;
            case "dockWidth":
                Store.State.DockWidth = Math.Clamp(Number(c.Args, "value"), 560, 4000);
                break;
            case "frameThickness":
                Store.State.FrameThickness = (int)Math.Round(Math.Clamp(Number(c.Args, "value"), 4, 40));
                frame.UpdateThickness();
                break;
            case "glassOpacity":
                Store.State.GlassOpacity = Math.Clamp(Number(c.Args, "value"), .02, .95);
                break;
            case "motion":
                Store.State.Motion = Math.Clamp(Number(c.Args, "value"), 0, 1.5);
                break;
            case "iconSize":
                Store.State.IconSize = Math.Clamp(Number(c.Args, "value"), 40, 80);
                break;
            case "taskbar":
            case "replaceTaskbar":
                owner.SetTaskbar(Flag(c.Args, "enabled"));
                break;
            case "dockVisible":
                Store.State.DockVisible = Flag(c.Args, "enabled");
                if (!Store.State.DockVisible && owner.TaskbarEnabled)
                    owner.SetTaskbar(false);
                break;   // dock masqué pendant qu'il remplace la barre Windows : on rend la barre Windows, jamais sans barre
            case "bookmarksRailVisible":
                Store.State.BookmarksRailVisible = Flag(c.Args, "enabled");
                break;
            case "socialRailVisible":
                Store.State.SocialRailVisible = Flag(c.Args, "enabled");
                break;
            case "clipboardImages":
                Store.State.ClipboardImages = Flag(c.Args, "enabled");
                owner.ApplyClipboardSettings();
                break;
            case "clipboardLimit":
                Store.State.ClipboardLimit = (int)Math.Clamp(Math.Round(Number(c.Args, "value")), 8, 80);
                owner.ApplyClipboardSettings();
                break;
            case "customGlassOpacity":
                Store.State.CustomGlassOpacity = Math.Clamp(Number(c.Args, "value"), .02, .95);
                break;
            case "autostart":
                Native.SetAutostart(Flag(c.Args, "enabled"));
                break;
            case "screenFrame":
                Store.State.ScreenFrame = Flag(c.Args, "enabled");
                if (Store.State.ScreenFrame)
                    frame.Enable();
                else
                    frame.Disable();
                break;
            case "socialBackground":
                Store.State.SocialBackground = Flag(c.Args, "enabled");
                social.BackgroundChanged();
                break;
            case "dictationEnabled":
                Store.State.DictationEnabled = Flag(c.Args, "enabled");
                dictation.Apply(Store.State.DictationEnabled, Store.State.DictationKey);
                break;
            case "dictationKey":
                {
                    int vk = (int)Number(c.Args, "value");
                    if (vk is > 0 and < 255)
                        Store.State.DictationKey = vk;
                    dictation.Apply(Store.State.DictationEnabled, Store.State.DictationKey);
                    break;
                }
            case "clipboardHistory":
                Store.State.ClipboardHistory = Flag(c.Args, "enabled");
                owner.ApplyClipboardSettings();
                break;
        }
        Store.Save();
    }

    [BridgeAction("shortcut-reset")]
    void OnShortcutReset(BridgeCall c)
    {
        owner.Shortcuts.Reset(Text(c.Args, "id"));
    }

    [BridgeAction("shortcut")]
    void OnShortcut(BridgeCall c)
    {
        if (c.Args.ValueKind == JsonValueKind.Object && c.Args.TryGetProperty("id", out _))
        {
            owner.Shortcuts.Apply(Text(c.Args, "id"), Text(c.Args, "combo"));
            return;
        }
        uint modifiers = (uint)Number(c.Args, "modifiers"), key = (uint)Number(c.Args, "key");
        if (modifiers is > 0 and <= 15 && key is >= 0x20 and <= 0xFE)
        {
            string error;
            bool success = Text(c.Args, "kind") switch
            {
                "palette" => owner.Keys.SetPaletteShortcut(modifiers, key, out error),
                "wallpaper" => owner.Keys.SetWallpaperShortcut(modifiers, key, out error),
                _ => owner.Keys.SetShortcut(modifiers, key, "home", out error)
            };
            c.Notice = success ? "Raccourci enregistré" : error;
            if (success)
                Send("shortcut-saved:" + Text(c.Args, "kind"));
        }
        else
            c.Notice = "Choisis Ctrl, Alt ou Windows avec une autre touche.";
    }
}
