// Preferences: the user settings serialized to preferences-glass.json (member order is the JSON order).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PanneauxGlass.Storage;

public sealed class Preferences
{
    public Dictionary<string, string> CustomPalette { get; set; } =
        new()
        {
            { "background", "#fff8f6" },
            { "text", "#261916" },
            { "primary", "#895143" },
            { "secondary", "#775b52" },
            { "tertiary", "#68613c" },
        };
    public bool CustomGlass { get; set; } = false;
    public double CustomGlassOpacity { get; set; } = .12;
    public bool AccentBorders { get; set; } = true;
    public bool ExplorerTheme { get; set; } = false;
    public List<string> ExplorerFolders { get; set; } = new();

    // default on (one-time migration FilesDefaultsMigrated for existing installs): HKCU Directory/Drive shell handler
    // -> Winlestia Fichiers (see FilesHandler.cs)
    public bool FilesDefaultHandler { get; set; } = true;

    // set once the default-on / default-pinned migration ran; a later opt-out is never undone
    public bool FilesDefaultsMigrated { get; set; }

    // set once the old "Winlestia Fichiers" dock pin was removed (the app has its own dock button now); see
    // FilesPinMigration
    public bool FilesPinRemoved { get; set; }

    // default on: keep a hidden Winlestia Fichiers resident so folders open instantly
    public bool FilesPreload { get; set; } = true;
    public bool AutoTile { get; set; } = false;
    public bool DesktopLogo { get; set; } = false;
    public bool Osd { get; set; } = true;
    public bool QuietFullscreen { get; set; } = true;
    public bool DesktopIndicator { get; set; } = true;
    public bool DockDrawerOpen { get; set; } = false;
    public bool ActiveBorderOnly { get; set; } = false;
    public bool? ReducedEffects { get; set; }
    public int TopActivationMargin { get; set; } = 2;
    public double DockWidth { get; set; } = 0;
    public int ShellRevision { get; set; }
    public bool ReplaceTaskbar { get; set; } = true;
    public string RailSide { get; set; } = "left";
    public string PreviousTheme { get; set; } = "";
    public int FrameThickness { get; set; } = 16;
    public double GlassOpacity { get; set; } = .95;
    public int GlassOpacityRevision { get; set; }
    public bool AutoTheme { get; set; } = false;
    public bool ScreenFrame { get; set; } = true;
    public List<string> TaskbarPins { get; set; } = new();
    public ShortcutPreferences Shortcut { get; set; } = new();
    public ShortcutPreferences PaletteShortcut { get; set; } = new() { Modifiers = 2, Key = 0x71 };
    public ControlPreferences Controls { get; set; } = new();
    public string HomeScope { get; set; } = "Tous";
    public Dictionary<string, int> LaunchCounts { get; set; } = new();
    public double Tint { get; set; } = .20;
    public double Reflections { get; set; } = .80;
    public double IconSize { get; set; } = 56;
    public double LeftRatio { get; set; } = .40;
    public double TopRatio { get; set; } = .50;
    public double Gap { get; set; } = 24;
    public double Margin { get; set; } = 52;
    public double Motion { get; set; } = 1;
    public double RailWidth { get; set; } = 62;
    public double RailHeight { get; set; } = .87;
    public bool AutoHide { get; set; } = true;
    public bool PauseFullscreen { get; set; } = true;
    public bool LightGlass { get; set; } = false;
    public string Theme { get; set; } = "nuit";
    public int MotionLevel { get; set; } = 1;
    public bool ClipboardHistory { get; set; } = true;
    public bool ClipboardImages { get; set; } = true;
    public int ClipboardLimit { get; set; } = 30;
    public List<WorkspaceProfile> Workspaces { get; set; } = new();
    public List<string> ColorHistory { get; set; } = new();
    public bool TourDone { get; set; }
    public List<PinNote> Pins { get; set; } = new();
    public ShortcutPreferences PinShortcut { get; set; } = new() { Modifiers = 2, Key = 0x77 };
    public ShortcutPreferences CaptureShortcut { get; set; } = new() { Modifiers = 2, Key = 0x72 };
    public ShortcutPreferences WallpaperShortcut { get; set; } = new() { Modifiers = 9, Key = 0x57 };
    public bool SocialBackground { get; set; } = true;
    public bool DictationEnabled { get; set; } = false;
    public int DictationKey { get; set; } = 0xA5;
    public string Scratch { get; set; } = "";
    public bool SocialMuted { get; set; } = false;
    public bool SocialSound { get; set; } = true;
    public int SocialVolume { get; set; } = 60;
    public string BookmarksBrowser { get; set; } = "";
    public Dictionary<string, List<string>> BookmarksHidden { get; set; } = new();
    public Dictionary<string, bool> SocialEnabled { get; set; } = new();
    public Dictionary<string, bool> SocialNetMuted { get; set; } = new();
    public Dictionary<string, bool> SocialConnected { get; set; } = new();
    public List<string> Favorites { get; set; } = new();
    public List<string> Recent { get; set; } = new();
    public List<string> Hidden { get; set; } = new();
    public List<string> Order { get; set; } = new();
    public List<Entry> Custom { get; set; } = new();
    public List<WebShortcut> Websites { get; set; } = new();
    public List<LauncherTool> LauncherTools { get; set; } = new();
    public List<WindowsShortcut> QuickTabs { get; set; } = DefaultQuickTabs();

    public static List<WindowsShortcut> DefaultQuickTabs() =>
        new()
        {
            new()
            {
                Id = "tab-apps",
                Name = "Applications",
                Target = "glass:apps",
                Glyph = "\uE71D",
            },
            new()
            {
                Id = "tab-games",
                Name = "Jeux",
                Target = "glass:games",
                Glyph = "\uE7FC",
            },
            new()
            {
                Id = "tab-projects",
                Name = "Projets",
                Target = "glass:projects",
                Glyph = "\uE943",
            },
            new()
            {
                Id = "tab-documents",
                Name = "Documents",
                Target = "glass:documents",
                Glyph = "\uE8B7",
            },
            new()
            {
                Id = "tab-note",
                Name = "Note",
                Target = "glass:note",
                Glyph = "\uE70B",
            },
            new()
            {
                Id = "tab-focus",
                Name = "Minuteur",
                Target = "glass:focus",
                Glyph = "\uE917",
            },
            new()
            {
                Id = "tab-tools",
                Name = "Atelier",
                Target = "glass:tools",
                Glyph = "\uE74C",
            },
            new()
            {
                Id = "tab-capture",
                Name = "Capture",
                Target = "glass:capture",
                Glyph = "\uE722",
            },
        };

    public Dictionary<string, string> BoardOverrides { get; set; } = new();
    public Dictionary<string, string> Names { get; set; } = new();
    public Dictionary<string, string> Categories { get; set; } = new();
    public Dictionary<string, string> Icons { get; set; } = new();
    public List<BoardState> Boards { get; set; } = Defaults();

    public static List<BoardState> Defaults() =>
        new()
        {
            new()
            {
                Id = "apps",
                Title = "Applications",
                Accent = "#91DFF5",
                Filter = "Sélection",
            },
            new()
            {
                Id = "games",
                Title = "Jeux",
                Accent = "#ADA7FF",
            },
            new()
            {
                Id = "projects",
                Title = "Projets",
                Accent = "#8BE6C5",
            },
            new()
            {
                Id = "documents",
                Title = "Documents & médias",
                Accent = "#F0D195",
            },
        };

    public void Normalize()
    {
        FrameThickness = Math.Clamp(FrameThickness, 4, 40);
        GlassOpacity = double.IsFinite(GlassOpacity) ? Math.Clamp(GlassOpacity, .02, .95) : .95;
        Tint = double.IsFinite(Tint) ? Math.Clamp(Tint, .05, .75) : .2;
        Reflections = double.IsFinite(Reflections) ? Math.Clamp(Reflections, 0, 1) : .8;
        IconSize = double.IsFinite(IconSize) ? Math.Clamp(IconSize, 40, 80) : 56;
        LeftRatio = Math.Clamp(LeftRatio, .3, .7);
        TopRatio = Math.Clamp(TopRatio, .3, .7);
        Gap = Math.Clamp(Gap, 12, 48);
        Margin = Math.Clamp(Margin, 20, 100);
        Motion = Math.Clamp(Motion, 0, 1.5);
        RailWidth = Math.Clamp(RailWidth, 40, 90);
        RailHeight = Math.Clamp(RailHeight, .45, .94);
        Controls ??= new();
        Controls.Normalize();
        Shortcut ??= new();
        Shortcut.Normalize();
        LaunchCounts ??= new();
        if (HomeScope != "Tous" && !new[] { "apps", "games", "projects", "documents" }.Contains(HomeScope))
            HomeScope = "Tous";
        if (DictationKey is < 1 or > 254)
            DictationKey = 0xA5;
        Scratch ??= "";
        if (Scratch.Length > 20000)
            Scratch = Scratch[..20000];
        if (Theme != "custom" && PanneauxGlass.Views.Theme.Find(Theme) == null)
            Theme = "nuit";
        CustomGlassOpacity = double.IsFinite(CustomGlassOpacity) ? Math.Clamp(CustomGlassOpacity, .02, .95) : .12;
        CustomPalette ??= new();
        Websites ??= new();
        LauncherTools ??= new();
        MotionLevel = Math.Clamp(MotionLevel, 0, 2);
        ClipboardLimit = Math.Clamp(ClipboardLimit, 8, 80);
        Workspaces ??= new();
        Workspaces = Workspaces.Where(w => w != null && !string.IsNullOrWhiteSpace(w.Name)).Take(24).ToList();
        foreach (var workspace in Workspaces)
        {
            workspace.Targets ??= new();
            workspace.Layout ??= new();
            workspace.Targets = workspace
                .Targets.Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
        }
        ColorHistory ??= new();
        ColorHistory = ColorHistory.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Take(24).ToList();
        Pins ??= new();
        Pins = Pins.Where(p => p != null && (p.Kind == "memo" || !string.IsNullOrWhiteSpace(p.ImagePath)))
            .Take(24)
            .ToList();
        foreach (var pin in Pins)
        {
            if (string.IsNullOrWhiteSpace(pin.Id))
                pin.Id = Guid.NewGuid().ToString("N");
            if (pin.Kind != "image")
                pin.Kind = "memo";
            pin.Text ??= "";
            if (pin.Text.Length > 4000)
                pin.Text = pin.Text[..4000];
            pin.ImagePath ??= "";
            pin.Width = double.IsFinite(pin.Width) ? Math.Clamp(pin.Width, 160, 1400) : 300;
            pin.Height = double.IsFinite(pin.Height) ? Math.Clamp(pin.Height, 0, 1600) : 0;
            pin.Opacity = double.IsFinite(pin.Opacity) ? Math.Clamp(pin.Opacity, .35, 1) : 1;
            pin.Angle = double.IsFinite(pin.Angle) ? Math.Clamp(pin.Angle, -12, 12) : -2.4;
            if (pin.Left is double left && !double.IsFinite(left))
                pin.Left = null;
            if (pin.Top is double top && !double.IsFinite(top))
                pin.Top = null;
        }
        PinShortcut ??= new() { Modifiers = 2, Key = 0x77 };
        PinShortcut.Normalize();
        CaptureShortcut ??= new() { Modifiers = 2, Key = 0x72 };
        CaptureShortcut.Normalize();
        WallpaperShortcut ??= new() { Modifiers = 9, Key = 0x57 };
        if (!GlobalShortcut.ValidWallpaper(WallpaperShortcut.Modifiers, WallpaperShortcut.Key))
        {
            WallpaperShortcut.Modifiers = 9;
            WallpaperShortcut.Key = 0x57;
        }
        PaletteShortcut ??= new() { Modifiers = 2, Key = 0x71 };
        PaletteShortcut.Normalize();
        QuickTabs ??= DefaultQuickTabs();
        QuickTabs = QuickTabs.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Target)).Take(12).ToList();
        foreach (var tab in QuickTabs)
        {
            if (string.IsNullOrWhiteSpace(tab.Id))
                tab.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(tab.Name))
                tab.Name = tab.Target;
            if (string.IsNullOrWhiteSpace(tab.Glyph))
                tab.Glyph = "\uE713";
            tab.Arguments ??= "";
        }
        Favorites ??= new();
        Recent ??= new();
        Hidden ??= new();
        Order ??= new();
        Custom ??= new();
        BoardOverrides ??= new();
        Names ??= new();
        Categories ??= new();
        Icons ??= new();
        if (
            Boards == null
            || Boards.Count != 4
            || !Boards
                .Select(x => x.Id)
                .OrderBy(x => x)
                .SequenceEqual(new[] { "apps", "documents", "games", "projects" })
        )
            Boards = Defaults();
        foreach (var b in Boards)
        {
            if (string.IsNullOrWhiteSpace(b.Title))
                b.Title = Defaults().First(x => x.Id == b.Id).Title;
            try
            {
                _ = System.Windows.Media.ColorConverter.ConvertFromString(b.Accent);
            }
            catch
            {
                b.Accent = "#91DFF5";
            }
            b.Filter ??= "Tous";
            if (b.Placement?.Valid == false)
                b.Placement = null;
        }
    }
}
