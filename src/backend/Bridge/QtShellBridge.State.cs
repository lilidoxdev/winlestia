// language: C# 12, file: QtShellBridge.State.cs, target: Windows 11 / .NET 8, état envoyé au frontend
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
    object State()
    {
        M3.Wallpaper(wallpaper.Monitors, () => owner.SetTheme(Store.State.Theme));
        RefreshTasks();
        var gpu = reading?.Gpus.FirstOrDefault();
        double mem = reading == null || reading.MemoryTotal == 0 ? 0 : 1 - (double)reading.MemoryAvailable / reading.MemoryTotal;
        long total = reading?.Drives.Sum(d => d.Total) ?? 0, free = reading?.Drives.Sum(d => d.Free) ?? 0;
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new
        {
            systemRecent = DesktopRecents.Values,
            wallpapers = wallpaper.Monitors,
            wallpaperRevision = wallpaper.Revision,
            audioReadMs = mixer.LastReadMilliseconds,
            audioMixer = new
            {
                running = mixer.Running,
                ready = mixer.Ready,
                applications = mixer.Applications,
                devices = mixer.Devices
            },
            quickTabs = Store.State.QuickTabs.Select(t => new { id = t.Id, name = t.Name }).ToArray(),
            social = social.Snapshot(),
            bookmarks = bookmarks.Snapshot(),
            settings = new
            {
                dataFolder = Store.DataDir,
                version = (System.Reflection
                    .Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion
                    ?? "").Split('+')[0],
                accentBorders = Store.State.AccentBorders,
                explorerStatus = explorer.Status,
                explorerTheme = Store.State.ExplorerTheme,
                explorerFolders = Store.State.ExplorerFolders.ToArray(),
                autoTile = Store.State.AutoTile,
                tileExclusions = tiler.Exclusions,
                desktopLogo = Store.State.DesktopLogo,
                osd = Store.State.Osd,
                quietFullscreen = Store.State.QuietFullscreen,
                desktopIndicator = Store.State.DesktopIndicator,
                dockDrawerOpen = Store.State.DockDrawerOpen,
                activeBorderOnly = Store.State.ActiveBorderOnly,
                reducedEffects = LightRendering,
                customPalette = Store.State.CustomPalette,
                customGlass = Store.State.CustomGlass,
                customGlassOpacity = Store.State.CustomGlassOpacity,
                themeMode = Store.State.AutoTheme ? "auto" : Store.State.Theme == "custom" ? "custom" : Store.State.Theme == "shell"
                    ? "light" : "dark",
                screenFrame = Store.State.ScreenFrame,
                topActivationMargin = Store.State.TopActivationMargin,
                dockWidth = Store.State.DockWidth,
                frameThickness = Store.State.FrameThickness,
                glassOpacity = Store.State.GlassOpacity,
                motionScale = Store.State.Motion,
                iconSize = Store.State.IconSize,
                autostart = Native.Autostart,
                filesDefaultHandler = Store.State.FilesDefaultHandler,
                filesPreload = Store.State.FilesPreload,
                socialBackground = Store.State.SocialBackground,
                clipboardHistory = Store.State.ClipboardHistory,
                dictationEnabled = Store.State.DictationEnabled,
                dictationKey = Store.State.DictationKey,
                shortcut = GlobalShortcut.Label(Store.State.Shortcut.Modifiers, Store.State.Shortcut.Key),
                paletteShortcut = owner.Keys.PaletteLabel,
                wallpaperShortcut = owner.Keys.WallpaperLabel,
                settingsShortcut = GlobalShortcut.Label(Store.State.SettingsShortcut.Modifiers, Store.State.SettingsShortcut.Key),
                dockVisible = Store.State.DockVisible,
                replaceTaskbar = Store.State.ReplaceTaskbar,
                bookmarksRailVisible = Store.State.BookmarksRailVisible,
                socialRailVisible = Store.State.SocialRailVisible,
                clipboardImages = Store.State.ClipboardImages,
                clipboardLimit = Store.State.ClipboardLimit,
                shortcuts = owner.Shortcuts.List(),
                shortcutStatus = owner.Shortcuts.Status()
            },
            clipboard = active && page == 6
                ? owner.QtClipboard
                    .Items
                    .Take(80)
                    .Select(c => new { id = c.Id, kind = c.Kind, text = c.Text, image = c.ImagePath, pinned = c.Pinned, at = c.At })
                    .ToArray()
                : Array.Empty<object>(),
            diagnostics = new
            {
                capturingShortcut = CapturingShortcut,
                root = Store.Root,
                frontendEpoch,
                frontendWindowPid,
                launcherPid = frontend?.Id ?? 0,
                cpuSeconds = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds,
                workingSet = Process.GetCurrentProcess().WorkingSet64
            },
            dark = Store.State.Theme != "shell",
            taskbarEnabled = owner.TaskbarEnabled,
            networkKind = NetworkKindWatcher.Current,
            virtualDesktops = new
            {
                count = VirtualDesktops.Current.Count,
                current = VirtualDesktops.Current.Current,
                names = VirtualDesktops.Current.Names
            },
            fullscreenMonitors = FullscreenWatcher.Current,
            quiet = Quiet,
            quietForced = QuietPolicy.Label(quietForce),
            performance = new
            {
                cpu = reading?.Cpu,
                cpuTemp = reading?.CpuTemperature,
                cpuTempSource = reading?.CpuTempSource,
                gpu = gpu?.Load,
                gpuTemp = gpu?.Temperature,
                memPercent = mem,
                memUsedKiB = reading == null ? 0 : (reading.MemoryTotal - reading.MemoryAvailable) / 1024d,
                diskPercent = total > 0 ? 1 - (double)free / total : 0,
                diskUsedKiB = (total - free) / 1024d,
                download = reading?.Download,
                upload = reading?.Upload,
                samples = telemetry.Samples,
                sampling = telemetry.Running
            },
            sysInfo = new
            {
                osName = "Windows " + (Environment.OSVersion.Version.Build >= 22000 ? "11" : "10"),
                uptime = $"{(int)uptime.TotalHours} h {uptime.Minutes} min",
                userName = displayUserName.Value
            },
            media = new
            {
                volume = mixer.Applications
                    .Where(a =>
                        System.IO.Path.GetFileNameWithoutExtension(a.path).Equals(player?.App switch
                        {
                            "Microsoft Edge" => "msedge",
                            "Google Chrome" => "chrome",
                            _ => player?.App
                        },
                                                                                  StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.volume)
                    .DefaultIfEmpty(70)
                    .Max(),
                available = player?.Available ?? false,
                app = player?.App ?? "",
                title = player?.Title ?? "",
                artist = player?.Artist ?? "",
                album = player?.Album ?? "",
                art = player?.Artwork != null ? new Uri(artworkPath).AbsoluteUri : "",
                playing = player?.Playing ?? false,
                position = player?.Position ?? 0,
                duration = player?.Duration ?? 0,
                canToggle = player?.CanToggle ?? false,
                canNext = player?.CanNext ?? false,
                canPrevious = player?.CanPrevious ?? false,
                canSeek = player?.CanSeek ?? false
            },
            controls = new
            {
                volume = sound?.Volume ?? 0,
                muted = sound?.Muted ?? false,
                device = sound?.Device ?? "",
                brightnessAvailable = displays.Any(d => d.Available),
                brightness = displays.FirstOrDefault(d => d.Available)?.Value ?? 0,
                displays = displays.Select(d => new { id = d.Id, name = d.Name, value = d.Value, available = d.Available }).ToArray(),
                wifiOn = connection?.Wifi.On ?? false,
                wifi = wifiVisible ? wifi.Snapshot() : null,
                bluetoothOn = connection?.Bluetooth.On ?? false,
                network = connection?.Network ?? ""
            },
            apps = Store.Visible().Select(EntryValue).ToArray(),
            favorites = Store.HomeFavorites().Select(EntryValue).ToArray(),
            recent = Store.RecentEntries().Take(24).Select(EntryValue).ToArray(),
            websites = Store.State.Websites.Select(w => new { id = w.Id, name = w.Name, icon = w.IconPath }).ToArray(),
            profiles = Store.State.Workspaces.Select(w => new { id = w.Id, name = w.Name }).ToArray(),
            taskbarApps = tasks,
            runningApps = runningApps,
            icons,
            iconRevision
        };

    }
}
