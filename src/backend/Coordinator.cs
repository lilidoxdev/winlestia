// language: C# 12, file: Coordinator.cs, target: Windows 11 / .NET 8 WPF, native shell and workspace lifecycle
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace PanneauxGlass;
internal sealed partial class Coordinator:IDisposable {
    internal readonly IconLoader Icons=new();internal readonly List<BoardWindow> Boards=new();
    readonly Window controller=new(){Width=1,Height=1,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
    readonly GlassWindow rail=new("Panneaux — poignée latérale"),toolbar=new("Panneaux Glass — commandes");
    internal QtShellBridge? QtShell;
    readonly ShellTaskbar? taskbar;
    readonly WebStrip websites;
    readonly OutsideDismiss dismissal;
    readonly HomeWindow home;
    readonly ControlCenterWindow controls;
    readonly PcStatusWindow pc;
    readonly ClockWindow clock;
    bool controlsExpanded,pcExpanded,paletteOpen;
    CommandPalette? palette;
    Button? paletteChip;
    readonly GlassWindow leftArrow=new("Panneaux Glass — explorer à gauche"),rightArrow=new(
        "Panneaux Glass — explorer à droite"
    );
    bool detailed,interacting;
    internal Rect PanelBounds {get;private set;}
    Window DialogParent=>QtShell!=null?controller:detailed?toolbar:home;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(120)},debounce=new(){
        Interval=TimeSpan.FromMilliseconds(120)
    };
    readonly Forms.NotifyIcon tray=new();
    readonly bool preview;
    readonly IntPtr hwnd;
    readonly HwndSource source;
    readonly GlobalShortcut shortcut;
    readonly ClipboardService clipboard;
    IntPtr lastForeign;
    bool open,closing,disposed,settingsOpen;
    DateTime edgeSince=DateTime.MinValue,awaySince=DateTime.MinValue,railAway=DateTime.MinValue;
    Forms.Screen? currentScreen;
    string railScreen="";
    Native.Point lastMouse;IntPtr lastForeground;internal bool PopupOpen,FavoritesOnly;internal bool Pinned;
    internal bool PreviewMode=>preview;
    AppCatalogueWatcher? catalogueWatcher;readonly RefreshGate catalogueGate=new();DateTime? catalogueAttempt;
    sealed class DialogOwner:Forms.IWin32Window {
        public IntPtr Handle {get;}public DialogOwner(Window window){Handle=new WindowInteropHelper(window).Handle;}
    }
    void DialogLayers(bool foreground){
        PinWindow.Lower(foreground);
        foreach(
            var board in Boards.Cast<Window>().Concat(websites.Windows).Append(home).Append(controls).Append(pc).Append(
                clock
            ).Append(leftArrow).Append(rightArrow).Append(toolbar)
        )board.Topmost=!foreground;
    }
    IEnumerable<Window> Windows=>Boards.Cast<Window>().Concat(websites.Windows).Append(toolbar).Append(home).Append(
        controls
    ).Append(pc).Append(clock).Append(leftArrow).Append(rightArrow).Append(rail).Append(controller).Concat(
        taskbar?.Windows??Array.Empty<Window>()
    );
    internal Window Anchor=>detailed?toolbar:home;
    internal Forms.Screen Screen=>currentScreen??Forms.Screen.PrimaryScreen!;
    internal IntPtr ToolbarHandle=>new WindowInteropHelper(toolbar).EnsureHandle();
    internal Coordinator(bool isPreview,bool interactionFixture=false,bool exportPreview=false){preview=isPreview;
    Store.Load();
    ShellSettings.Migrate(preview);
    Theme.Use(Program.ThemeOverride??Store.State.Theme);
    UI.Install();
    clipboard=new ClipboardService();
    websites=new WebStrip(this);
    foreach(var model in Store.State.Boards)Boards.Add(new BoardWindow(this,model));
    home=new HomeWindow(this);
    clock=new ClockWindow(this);
    controls=new ControlCenterWindow(this);
    pc=new PcStatusWindow(this);
    leftArrow.Radius=rightArrow.Radius=25;
    leftArrow.ContentHost.Children.Add(UI.IconButton("\uE76B","Afficher l’état du PC",TogglePc));
    rightArrow.ContentHost.Children.Add(UI.IconButton("\uE76C","Afficher les commandes Windows",ToggleControls));
        hwnd=new WindowInteropHelper(controller).EnsureHandle();
        source=HwndSource.FromHwnd(hwnd);
        source.AddHook(Hook);
        shortcut=new GlobalShortcut(
            hwnd,
            Store.State.Shortcut,
            Store.State.PaletteShortcut,
            Store.State.PinShortcut,
            Store.State.CaptureShortcut,
            Store.State.WallpaperShortcut
        );
        Store.Log(
            "Raccourcis · palette="
                +PaletteLabel
                +" · capture="
                +CaptureShortcutLabel
                +" · épinglage="
                +PinShortcutLabel
                +" · fonds d’écran="
                +WallpaperLabel
        );
        Pinned=preview;
        BuildToolbar();
        BuildRail();
        PinWindow.RestoreAll(this);
        foreach(var window in Windows)window.Activated+=(_,_)=>PinWindow.EnsureAbove();
        dismissal=new OutsideDismiss(
            CollapseImmediately,()=>open&&!settingsOpen&&!interacting&&!paletteOpen&&!(preview&&interactionFixture)
        );
        timer.Tick+=(_,_)=>Tick();
        timer.Start();
        clock.Shortcut=HandleShortcut;
        home.Shortcut=HandleShortcut;
        toolbar.Shortcut=HandleShortcut;
        foreach(var board in Boards)board.Shortcut=HandleShortcut;
        tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ??System.Drawing.SystemIcons.Application;
        tray.Text="Panneaux Glass · dj";
        tray.Visible=true;
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir l’espace",null,(_,_)=>Open());
        menu.Items.Add("Personnaliser",null,(_,_)=>Settings());
        menu.Items.Add("Actualiser les accès",null,(_,_)=>RefreshCatalogue());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Retrouver le Bureau organisé",null,(_,_)=>Rescue());
        menu.Items.Add("Quitter",null,(_,_)=>System.Windows.Application.Current.Shutdown());
        menu.Dispose();
        tray.MouseUp+=(_,e)=>{
            if(e.Button==Forms.MouseButtons.Right){
                if(QtShell!=null){QtShell.Send("controls");return;}
                if(!open)Open();
                GlassMenu.Open(
                    this,
                    DialogParent,
                    detailed?(FrameworkElement)toolbar.ContentHost:home.ContentHost,
                    "Panneaux",
                    new[]{
                        new MenuAction("Ouvrir l’espace","\uE8A9",Open),
                        new MenuAction("Commande rapide  ·  "+PaletteLabel,"\uE721",TogglePalette),
                        new MenuAction("Note rapide","\uE70B",OpenNotes),
                        new MenuAction("Minuteur…","\uE917",OpenFocus),
                        new MenuAction("Personnaliser","\uE713",()=>Settings(),Separator:true),
                        new MenuAction("Actualiser les accès","\uE72C",RefreshCatalogue),
                        new MenuAction("Retrouver le Bureau organisé","\uE777",Rescue,Separator:true),
                        new MenuAction("Quitter Panneaux","\uE711",()=>System.Windows.Application.Current.Shutdown())
                    },
                    true
                );
            }
        };
        tray.DoubleClick+=(_,_)=>Open();
        if(Program.QtMode)QtShell=new QtShellBridge(this);
        taskbar=new ShellTaskbar(this);
        if(!preview&&Store.State.ReplaceTaskbar){
            if(!TaskbarNative.ExplorerReady()){
                taskbarDeferred=true;
                Store.Log("Explorateur pas encore prêt : barre Windows laissée telle quelle jusqu'à TaskbarCreated.");
            }else if(!taskbar.Enable()){
                Store.State.ReplaceTaskbar=false;Store.Log("La barre Windows reste active : secours indisponible.");
            }
        }else if(preview&&Program.DockPreview)taskbar.ShowPreview();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        if(preview){
            Pinned=true;
            foreach(var window in Windows)window.ShowInTaskbar=window!=controller;
            Open();
            if(exportPreview){
                var capture=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
                capture.Tick+=(_,_)=>{capture.Stop();ExportPreview();};
                capture.Start();
            }
        }else if(
            !File.Exists(Path.Combine(Store.Root,"catalog.json"))||File.GetLastWriteTimeUtc(
                Path.Combine(Store.Root,"catalog.json")
            )<DateTime.UtcNow.AddDays(-1)
        )RefreshCatalogue();
        if(!preview){
            catalogueWatcher=new AppCatalogueWatcher(
                System.Windows.Application.Current.Dispatcher,
                AddCatalogueEntries,
                RemoveCatalogueEntries,
                RefreshCatalogue
            );
            catalogueWatcher.Start();
        }
        HideDesktopIcons();
        if(!preview)Native.MigrateAutostart();
        Store.Save();
        Store.Log(
            "Démarrage Panneaux "
                +(typeof(Coordinator).Assembly.GetName().Version?.ToString(3)??"?")
                +" · preview="
                +preview
                +" · t+"
                +(int)(DateTime.Now-Process.GetCurrentProcess().StartTime).TotalMilliseconds
                +" ms"
        );
    }
    IntPtr Hook(IntPtr w,int msg,IntPtr wp,IntPtr lp,ref bool handled){
        if(explorerCreated!=0&&(uint)msg==explorerCreated)ExplorerCreated();
        if(msg==0x0312){
            if(QtShell?.CapturingShortcut==true){handled=true;QtShell.CaptureHotkey(lp);return IntPtr.Zero;}
            int id=wp.ToInt32();
            if(id==2){handled=true;Rescue();}else if(shortcut.MatchesPalette(id)){
                handled=true;if(!settingsOpen)TogglePalette();
            }else if(shortcut.MatchesWallpaper(id)){handled=true;QtShell?.Send("wallpaper-switcher");}else if(
                shortcut.MatchesPin(id)
            ){handled=true;ToggleAlwaysOnTop();}else if(shortcut.MatchesCapture(id)){
                handled=true;if(!settingsOpen)OpenCapture();
            }else if(shortcut.Matches(id)){handled=true;if(!settingsOpen)ToggleShortcut();}
        }
        return IntPtr.Zero;
    }
    void BuildRail(){rail.ContentHost.Children.Clear();
    rail.Width=Store.State.RailWidth;
    rail.KeepOpen=true;
    rail.Radius=32;
    var hit=UI.Button("",Open);
    hit.Padding=new Thickness(0);
    hit.BorderThickness=new Thickness(0);
    hit.BorderBrush=Brushes.Transparent;
    hit.Background=Brushes.Transparent;
    hit.HorizontalContentAlignment=HorizontalAlignment.Stretch;
    hit.VerticalContentAlignment=VerticalAlignment.Stretch;
    hit.ToolTip="Ouvrir ton espace · "+GlobalShortcut.Label(Store.State.Shortcut.Modifiers,Store.State.Shortcut.Key);
    System.Windows.Automation.AutomationProperties.SetName(hit,"Ouvrir Panneaux Glass");
        var layout=new Grid();
        var spine=new Border{
            Width=3,
            Height=100,
            CornerRadius=new CornerRadius(2),
            Background=UI.Accent,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center
        };
        layout.Children.Add(spine);
        var glyph=UI.Glyph("\uE8A9",20);
        glyph.VerticalAlignment=VerticalAlignment.Top;
        glyph.Margin=new Thickness(0,28,0,0);
        glyph.Opacity=.9;
        layout.Children.Add(glyph);
        var dot=new Border{
            Width=6,
            Height=6,
            CornerRadius=new CornerRadius(3),
            Background=UI.Accent,
            VerticalAlignment=VerticalAlignment.Bottom,
            HorizontalAlignment=HorizontalAlignment.Center,
            Margin=new Thickness(0,0,0,28)
        };
        layout.Children.Add(dot);
        hit.Content=layout;
        rail.ContentHost.Children.Add(hit);
    }
    void BuildToolbar(){toolbar.Radius=30;
    toolbar.Height=64;
    toolbar.ContentHost.Margin=new Thickness(18,10,12,10);
    var row=new Grid();
    row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
    row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
    row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var brand=new StackPanel{
            Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,30,0)
        };
        var logo=UI.Glyph("\uE8A9",22);
        logo.Margin=new Thickness(5,0,12,0);
        brand.Children.Add(logo);
        brand.Children.Add(UI.Text("Panneaux",18,null,true));
        var dj=UI.Text(" / dj",13,UI.Dim);
        dj.VerticalAlignment=VerticalAlignment.Center;
        brand.Children.Add(dj);
        row.Children.Add(brand);
        // The field lives in the quick command (Ctrl F2) now: the bar keeps only the space and its shortcuts.
        var hint=UI.Text("Ctrl F2 · tout ton espace en une ligne",12,UI.Dim);
        hint.VerticalAlignment=VerticalAlignment.Center;
        hint.HorizontalAlignment=HorizontalAlignment.Center;
        hint.Opacity=.75;
        Grid.SetColumn(hint,1);
        row.Children.Add(hint);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(24,0,0,0)};
        var favorite=UI.IconButton(
            "\uE734","Afficher uniquement les favoris",()=>{FavoritesOnly=!FavoritesOnly;RefreshBoards();}
        );
        favorite.Click+=(_,_)=>favorite.Background=FavoritesOnly?UI.Brush("#45C5E8FF"):UI.Brush("#0FFFFFFF");
        paletteChip=UI.Button(PaletteLabel,TogglePalette);
        paletteChip.Padding=new Thickness(11,7,11,7);
        paletteChip.FontSize=12;
        paletteChip.ToolTip="Commande rapide · "+PaletteLabel;
        System.Windows.Automation.AutomationProperties.SetName(paletteChip,"Ouvrir la commande rapide");
        actions.Children.Add(paletteChip);
        actions.Children.Add(UI.IconButton("\uE80F","Revenir à l’accueil",ShowHome));
        actions.Children.Add(UI.IconButton("\uE8A9","Rétablir la disposition des panneaux",ResetLayout));
        actions.Children.Add(favorite);
        var pin=UI.IconButton("\uE718","Garder l’espace ouvert",()=>{Pinned=!Pinned;});
        pin.Background=Pinned?UI.Brush("#45C5E8FF"):UI.Brush("#0FFFFFFF");
        pin.Click+=(_,_)=>pin.Background=Pinned?UI.Brush("#45C5E8FF"):UI.Brush("#0FFFFFFF");
        actions.Children.Add(pin);
        actions.Children.Add(UI.IconButton("\uE713","Personnaliser l’espace",()=>Settings()));
        actions.Children.Add(UI.IconButton("\uE777","Retrouver le Bureau organisé",Rescue));
        actions.Children.Add(UI.IconButton("\uE711","Replier l’espace",Collapse));
        foreach(Button button in actions.Children)button.Margin=new Thickness(3,0,0,0);
        Grid.SetColumn(actions,2);
        row.Children.Add(actions);
        toolbar.ContentHost.Children.Add(row);
        toolbar.KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Collapse();};
    }
}
