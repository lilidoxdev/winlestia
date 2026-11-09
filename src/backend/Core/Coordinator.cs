// language: C# 12, file: Coordinator.cs, target: Windows 11 / .NET 8 WPF, cycle de vie du shell natif et de l'espace de travail
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

// Orchestration : les décisions et les dialogues vivent dans les services (Dialog, Launch, Tick, Shortcut, Catalogue…) et dans
// ShellState, l'état partagé que lisent aussi ces services.
internal sealed partial class Coordinator : IDisposable
{
    internal readonly IconLoader Icons = new();
    internal readonly List<BoardWindow> Boards = new();
    readonly Window controller = new() { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
    readonly GlassWindow rail = new("Winlestia — poignée latérale"), toolbar = new("Winlestia — commandes");
    readonly GlassWindow leftArrow = new("Winlestia — explorer à gauche"), rightArrow = new("Winlestia — explorer à droite");
    internal QtShellBridge? QtShell;
    // Vue des raccourcis globaux pour la page de réglages (liste, réaffectation avec retour arrière, conflits)
    internal ShortcutManager Shortcuts = null!;
    readonly ShellTaskbar? taskbar;
    readonly WebStrip websites;
    readonly OutsideDismiss dismissal;
    readonly HomeWindow home;
    readonly ControlCenterWindow controls;
    readonly PcStatusWindow pc;
    readonly ClockWindow clock;
    readonly ShellState state = new();
    CommandPalette? palette;
    Button? paletteChip;
    internal Rect PanelBounds
    {
        get; private set;
    }
    static readonly TimeSpan TimerInterval = TimeSpan.FromMilliseconds(120);
    Window DialogParent => QtShell != null ? controller : state.Detailed ? toolbar : home;
    readonly DispatcherTimer timer = new() { Interval = TimerInterval }, debounce = new() { Interval = TimerInterval };
    readonly ITrayService tray = new TrayService();
    readonly ICatalogueService catalogue;
    readonly IDesktopIconsService desktopIcons;
    readonly IPreviewExportService previewExport;
    readonly IDialogService dialogs;
    readonly ILaunchService launcher;
    readonly ITickController ticks;
    internal readonly IShortcutBindings Keys;
    readonly bool preview;
    readonly IntPtr hwnd;
    readonly HwndSource source;
    readonly ClipboardService clipboard;
    bool disposed;
    Forms.Screen? currentScreen;
    // Indicateurs partagés avec les fenêtres : l'API publique du Coordinator reste inchangée, l'état vit dans ShellState.
    internal bool PopupOpen
    {
        get => state.PopupOpen; set => state.PopupOpen = value;
    }
    internal bool FavoritesOnly
    {
        get => state.FavoritesOnly; set => state.FavoritesOnly = value;
    }
    internal bool Pinned
    {
        get => state.Pinned; set => state.Pinned = value;
    }
    internal bool PreviewMode => preview;
    // Fenêtres glass dans l'ordre d'animation du repli (l'index décale chaque glissement) ; la poignée est gérée à part.
    IEnumerable<GlassWindow> Glass => Boards.Cast<GlassWindow>()
        .Append(toolbar).Append(home).Append(controls).Append(pc).Append(clock).Append(leftArrow).Append(rightArrow);
    IEnumerable<Window> Layered => Glass.Cast<Window>().Concat(websites.Windows);
    IEnumerable<Window> Windows
        => Layered.Append(rail).Append(controller).Concat(taskbar?.Windows ?? Array.Empty<Window>());
    internal Window Anchor => state.Detailed ? toolbar : home;
    internal Forms.Screen Screen => currentScreen ?? Forms.Screen.PrimaryScreen!;
    internal IntPtr ToolbarHandle => new WindowInteropHelper(toolbar).EnsureHandle();
    internal Coordinator(bool isPreview, bool interactionFixture = false, bool exportPreview = false, RunOptions? options = null)
    {
        preview = isPreview;
        catalogue = new CatalogueService(preview, () =>
        {
            RefreshBoards();
            QtShell?.CatalogueChanged();
        });
        desktopIcons = new DesktopIconsService(preview);
        previewExport = new PreviewExportService(Icons);
        dialogs = new DialogService(state, () => Layered, () => DialogParent);
        launcher = new LaunchService(dialogs.Message, Collapse, CollapseImmediately, () => Open());
        Store.Load();
        ShellSettings.Migrate(preview);
        Theme.Use(Program.ThemeOverride ?? Store.State.Theme);
        UI.Install();
        clipboard = new ClipboardService();
        websites = new WebStrip(this);
        ticks = new TickController(state, rail, new TickHost(() => Boards.Cast<Window>().Append(toolbar).Concat(websites.Windows),
            () => Windows, () => taskbar?.PointerTick(), SetTaskbar, () => Open(), Collapse, () => QtShell != null && !state.Open));
        // HomeWindow lit Keys dans son constructeur : les liaisons existent avant toute fenetre. Le hook reste branche apres.
        hwnd = new WindowInteropHelper(controller).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd);
        var global = new GlobalShortcut(hwnd, Store.State.Shortcut, Store.State.PaletteShortcut, Store.State.PinShortcut,
            Store.State.CaptureShortcut, Store.State.WallpaperShortcut, Store.State.SettingsShortcut);
        Keys = new ShortcutBindings(global, BuildRail, RefreshPaletteLabels);
        foreach (var model in Store.State.Boards)
            Boards.Add(new BoardWindow(this, model));
        home = new HomeWindow(this);
        clock = new ClockWindow(this);
        controls = new ControlCenterWindow(this);
        pc = new PcStatusWindow(this);
        leftArrow.Radius = rightArrow.Radius = 25;
        leftArrow.ContentHost.Children.Add(UI.IconButton("", "Afficher l’état du PC", TogglePc));
        rightArrow.ContentHost.Children.Add(UI.IconButton("", "Afficher les commandes Windows", ToggleControls));
        source.AddHook(Hook);
        Shortcuts = new ShortcutManager(global, () => Store.State, Store.Save, id =>
        {
            if (id == "dashboard")
                BuildRail();
            else if (id is "palette" or "pin" or "capture")
                RefreshPaletteLabels();
        });
        Store.Log("Raccourcis · palette=" + Keys.PaletteLabel + " · capture=" + Keys.CaptureShortcutLabel + " · épinglage="
            + Keys.PinShortcutLabel + " · fonds d’écran=" + Keys.WallpaperLabel);
        Pinned = preview;
        BuildToolbar();
        BuildRail();
        PinWindow.RestoreAll(this);
        foreach (var window in Windows)
            window.Activated += (_, _) => PinWindow.EnsureAbove();
        dismissal = new OutsideDismiss(CollapseImmediately,
            () => state.Open && !state.SettingsOpen && !state.Interacting && !state.PaletteOpen && !(preview && interactionFixture));
        timer.Tick += (_, _) => ticks.Tick();
        timer.Start();
        clock.Shortcut = HandleShortcut;
        home.Shortcut = HandleShortcut;
        toolbar.Shortcut = HandleShortcut;
        foreach (var board in Boards)
            board.Shortcut = HandleShortcut;
        BuildTray();
        if (Program.QtMode)
            QtShell = new QtShellBridge(this, options ?? RunOptions.Default);
        taskbar = new ShellTaskbar(this);
        StartTaskbar();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        if (preview)
        {
            Pinned = true;
            foreach (var window in Windows)
                window.ShowInTaskbar = window != controller;
            Open();
            if (exportPreview)
                previewExport.ScheduleExport();
        }
        else if (!File.Exists(Path.Combine(Store.Root, "catalog.json"))
            || File.GetLastWriteTimeUtc(Path.Combine(Store.Root, "catalog.json")) < DateTime.UtcNow.AddDays(-1))
            RefreshCatalogue();
        if (!preview)
            catalogue.StartWatching();
        desktopIcons.Hide();
        if (!preview)
            Native.MigrateAutostart();
        Store.Save();
        Store.Log("Démarrage Winlestia " + (typeof(Coordinator).Assembly.GetName().Version?.ToString(3) ?? "?") + " · preview="
            + preview + " · t+" + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms");
    }
    IntPtr Hook(IntPtr w, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (explorerCreated != 0 && (uint)msg == explorerCreated)
            ExplorerCreated();
        if (msg != 0x0312)
            return IntPtr.Zero;
        if (QtShell?.CapturingShortcut == true)
        {
            handled = true;
            QtShell.CaptureHotkey(lp);
            return IntPtr.Zero;
        }
        var action = Keys.Resolve(wp.ToInt32());
        handled = action != HotkeyAction.None;
        switch (action)
        {
            case HotkeyAction.Rescue:
                Rescue();
                break;
            case HotkeyAction.Palette:
                if (!ShortcutGateRules.IsBlockedBySettings(action, state.SettingsOpen))
                    TogglePalette();
                break;
            case HotkeyAction.Settings:
                if (QtShell != null)
                    QtShell.Send("settings");
                else
                    Settings();
                break;
            case HotkeyAction.Wallpaper:
                QtShell?.Send("wallpaper-switcher");
                break;
            case HotkeyAction.Pin:
                ToggleAlwaysOnTop();
                break;
            case HotkeyAction.Capture:
                if (!ShortcutGateRules.IsBlockedBySettings(action, state.SettingsOpen))
                    OpenCapture();
                break;
            case HotkeyAction.Toggle:
                if (!ShortcutGateRules.IsBlockedBySettings(action, state.SettingsOpen))
                    ToggleShortcut();
                break;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        catalogue.Dispose();
        taskbar?.Dispose();
        QtShell?.Dispose();
        RestoreDesktopIcons();
        desktopIcons.Dispose();
        dismissal.Dispose();
        Store.Save();
        timer.Stop();
        debounce.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Keys.Dispose();
        source.RemoveHook(Hook);
        clipboard.Dispose();
        tray.Dispose();
        Icons.Dispose();
        foreach (var window in Windows.ToArray())
            window.Close();
    }
}
