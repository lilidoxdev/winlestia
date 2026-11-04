// Views: open, switch and collapse the layers, layout, theme and the edge tick.

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

internal sealed partial class Coordinator {

    void OpenView(bool full,bool showControls=false,bool showPc=false){
        if(disposed)return;
        if(closing)CollapseImmediately();
        if(open){if(detailed!=full)SwitchView(full,null);else Native.Raise(full?toolbar:home);return;}
        Native.GetCursorPos(out var mouse);
        currentScreen=Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X,mouse.Y));
        dismissal.SetActive(false);
        open=true;
        detailed=full;
        controlsExpanded=showControls;
        pcExpanded=showPc;
        if(controlsExpanded&&pcExpanded&&!FitsBoth())pcExpanded=false;
        rail.HideImmediately();
        awaySince=DateTime.MinValue;
        if(!full)home.Reset();
        PinWindow.SpaceOpen(true,ToolbarHandle);
        Arrange(true);
        var focus=full?(Window)toolbar:controlsExpanded?controls:pcExpanded?pc:home;
        Native.Raise(focus);
        PinWindow.EnsureAbove();
        dismissal.SetActive(true);
        Store.Log(full?"Ouverture · complète":"Ouverture · accueil");
    }
    internal void ShowHome(){
        if(QtShell!=null){QtShell.Send("toggle");return;}if(!open){Open();return;}SwitchView(false,null);
    }
    internal void ShowWorkspace(string? board=null){
        if(QtShell!=null){QtShell.Send("workspace:"+(board??"apps"));return;}
        if(!open){OpenView(true);return;}
        SwitchView(true,board);
    }
    void SwitchView(bool full,string? board){
        dismissal.SetActive(false);
        GlassMenu.DismissActive();
        PopupOpen=false;
        foreach(
            var window in Boards.Cast<GlassWindow>().Append(toolbar).Append(home).Append(controls).Append(pc).Append(
                clock
            ).Append(leftArrow).Append(rightArrow)
        )window.HideImmediately();
        websites.HideImmediately();
        detailed=full;
        controlsExpanded=pcExpanded=false;
        awaySince=DateTime.MinValue;
        if(!full)home.Reset();
        Arrange(true);
        var active=full?(Window?)Boards.FirstOrDefault(b=>b.Model.Id==board)??toolbar:home;
        active.Activate();
        dismissal.SetActive(true);
        Store.Log(full?"Vue complète":"Vue accueil");
    }
    internal void ToggleControls(){
        if(QtShell!=null){QtShell.Send("controls");return;}
        if(!open){Open();}
        if(detailed)ShowHome();
        dismissal.SetActive(false);
        GlassMenu.DismissActive();
        controlsExpanded=!controlsExpanded;
        if(controlsExpanded&&!FitsBoth())pcExpanded=false;
        Arrange(true);
        (controlsExpanded?(Window)controls:home).Activate();
        if(controlsExpanded){controls.ContentHost.Focusable=true;Keyboard.Focus(controls.ContentHost);}
        dismissal.SetActive(true);
        Store.Log(controlsExpanded?"Commandes Windows ouvertes":"Commandes Windows repliées");
    }
    internal void ControlsChanged()=>controls.Rebuild();
    bool FitsBoth(){
        var screen=currentScreen??Forms.Screen.PrimaryScreen!;
        return screen.WorkingArea.Width/VisualTreeHelperDpi()>=1700;
    }
    double VisualTreeHelperDpi()=>VisualTreeHelper.GetDpi(home).DpiScaleX;
    internal void TogglePc(){
        if(QtShell!=null){QtShell.Send("performance");return;}
        if(!open)Open();
        if(detailed)ShowHome();
        dismissal.SetActive(false);
        GlassMenu.DismissActive();
        pcExpanded=!pcExpanded;
        if(pcExpanded&&!FitsBoth())controlsExpanded=false;
        Arrange(true);
        (pcExpanded?(Window)pc:home).Activate();
        if(pcExpanded){pc.ContentHost.Focusable=true;Keyboard.Focus(pc.ContentHost);}
        dismissal.SetActive(true);
        Store.Log(pcExpanded?"État PC ouvert":"État PC replié");
    }
    internal (long samples,bool running) PcActivity=>(pc.Samples,pc.Sampling);
    internal (bool open,bool closing,int visible) UiActivity=>(
        open,closing,Windows.Count(w=>w.IsVisible&&w!=controller)
    );
    internal void RunControlDialog(Window? parent,Action action){
        bool previous=settingsOpen;
        settingsOpen=true;
        DialogLayers(true);
        try{action();}finally{settingsOpen=previous;DialogLayers(previous);awaySince=DateTime.MinValue;}
    }
    internal async Task RunControlTask(Func<Task> action){
        bool previous=settingsOpen;
        settingsOpen=true;
        DialogLayers(true);
        try{await action();}finally{settingsOpen=previous;DialogLayers(previous);awaySince=DateTime.MinValue;}
    }
    internal void EditControls(Window parent,bool add=false)=>RunControlDialog(
        parent,
        ()=>{var dialog=new ControlEditorWindow(this,add){Owner=parent,ShowInTaskbar=preview};dialog.ShowDialog();}
    );
    internal async void LaunchWindowsShortcut(WindowsShortcut shortcut){
        CollapseImmediately();
        try{
            if(shortcut.Target.StartsWith("ms-screenclip:",StringComparison.OrdinalIgnoreCase))await System
                .Threading
                .Tasks
                .Task
                .Delay(120);
            Process.Start(new ProcessStartInfo(shortcut.Target){Arguments=shortcut.Arguments,UseShellExecute=true});
        }catch(Exception ex){Open();Message("Le raccourci « "+shortcut.Name+" » n’a pas pu s’ouvrir.\n"+ex.Message);}
    }
    internal void BeginLayoutInteraction(){interacting=true;awaySince=DateTime.MinValue;}
    internal void EndLayoutInteraction(){interacting=false;awaySince=DateTime.MinValue;Store.Save();}
    internal void ResetLayout(){foreach(var board in Boards)board.Model.Placement=null;Store.Save();Arrange(true);}
    internal void Arrange(bool animate=false){if(!open)return;
    var screen=currentScreen??Forms.Screen.PrimaryScreen!;
    var w=screen.WorkingArea;
    var dpi=VisualTreeHelper.GetDpi(home);
    double d=dpi.DpiScaleX;
    double sx=w.Left/d,sy=w.Top/d,sw=w.Width/d,sh=w.Height/d;
    if(sw<1700&&controlsExpanded&&pcExpanded)controlsExpanded=false;
        double margin=Math.Min(Store.State.Margin,sw*.045),gap=Store.State.Gap;
        double full=sw-margin*2,top=sy+108,bottom=sy+sh-margin;
        PanelBounds=new Rect(sx+margin,top,full,Math.Max(310,bottom-top));
        if(!detailed){
            var pair=ControlPairLayout.Calculate(
                new Rect(sx,sy,sw,sh),
                controlsExpanded,
                pcExpanded,
                !Theme.Current.Id.StartsWith("shell",StringComparison.Ordinal)
            );
            if(Theme.Current.Id.StartsWith("shell",StringComparison.Ordinal))clock.HideImmediately();
            else clock.TransitionTo(pair.Clock,0,animate);
            home.TransitionTo(pair.Home,0,animate);
            home.Refresh();
            leftArrow.TransitionTo(pair.LeftArrow,1,animate);
            rightArrow.TransitionTo(pair.RightArrow,1,animate);
            if(pcExpanded)pc.TransitionTo(pair.Pc,2,animate);
            else pc.HideImmediately();
            if(controlsExpanded)controls.TransitionTo(pair.Controls,2,animate);
            else controls.HideImmediately();
            if(leftArrow.ContentHost.Children[0] is Button leftButton){
                leftButton.Content=UI.Glyph(pcExpanded?"\uE76C":"\uE76B");
                leftButton.ToolTip=pcExpanded?"Replier l’état du PC":"Afficher l’état du PC";
                System.Windows.Automation.AutomationProperties.SetName(leftButton,leftButton.ToolTip.ToString());
            }
            if(rightArrow.ContentHost.Children[0] is Button arrow){
                arrow.Content=UI.Glyph(controlsExpanded?"\uE76B":"\uE76C");
                arrow.ToolTip=controlsExpanded?"Replier les commandes Windows":"Afficher les commandes Windows";
                System.Windows.Automation.AutomationProperties.SetName(arrow,arrow.ToolTip.ToString());
            }
            return;
        }
        clock.HideImmediately();
        double usable=bottom-top-gap,
            h1=usable*Store.State.TopRatio,
            h2=usable-h1,
            left=(full-gap)*Store.State.LeftRatio,
            right=full-gap-left;
        double toolbarWidth=Math.Min(1130,Math.Max(780,full*.56)),sideWidth=Math.Max(110,(full-toolbarWidth-32)/2);
        toolbar.MoveInto(sx+(sw-toolbarWidth)/2,sy+24,toolbarWidth,64,0,animate);
        websites.Arrange(sx+(sw-toolbarWidth)/2-16,sy+24,sideWidth,animate);
        var targets=new[]{
            new Rect(sx+margin,top,left,h1),
            new Rect(sx+margin+left+gap,top,right,h1),
            new Rect(sx+margin,top+h1+gap,left,h2),
            new Rect(sx+margin+left+gap,top+h1+gap,right,h2)
        };
        for(int i=0;i<Boards.Count;i++){
            var r=Boards[i].Model.Placement?.Resolve(PanelBounds)??PanelLayout.Clamp(targets[i],PanelBounds);
            Boards[i].MoveInto(r.X,r.Y,r.Width,r.Height,i+1,animate);
            Boards[i].Refresh(false);
        }
        toolbar.Topmost=true;
    }
    // One gesture closes the whole layer: no dialog may outlive the surface that opened it.
    void CloseDialogs(){
        foreach(
            var window in System.Windows.Application.Current.Windows.OfType<GlassWindow>().Where(
                w=>!Windows.Contains(w)&&w is not FocusTimer
            ).ToArray()
        ){try{window.Close();}catch(Exception ex){Store.Log("Fermeture dialogue: "+ex.Message);}}
    }
    internal void SetTheme(string id){
        Theme.Use(id);
        Store.Save();
        foreach(
            var window in System.Windows.Application.Current.Windows.OfType<GlassWindow>().Where(
                w=>!Program.QtMode||w.IsVisible
            )
        ){window.RefreshTheme();window.FlashTheme();}
        clock.RefreshLabels();
        RefreshBoards();
        taskbar?.Refresh();
    }
    internal void Collapse(){
        QtShell?.Send("close");
        if(!open||closing)return;
        CloseDialogs();
        open=false;
        dismissal.SetActive(false);
        closing=true;
        var visible=Boards
            .Cast<GlassWindow>()
            .Append(toolbar)
            .Append(home)
            .Append(controls)
            .Append(pc)
            .Append(clock)
            .Append(leftArrow)
            .Append(rightArrow)
            .Where(w=>w.IsVisible)
            .ToArray();
        int remaining=visible.Length+1;
        void Done(){
            if(--remaining==0){closing=false;railAway=DateTime.UtcNow;PinWindow.SpaceOpen(false,ToolbarHandle);}
        }
        websites.SlideOut(Done);
        for(int i=0;i<visible.Length;i++)visible[i].SlideOut(i,Done);
    }
    internal void CollapseImmediately(){
        QtShell?.Send("close");
        CloseDialogs();
        if(!open&&!closing)return;
        open=false;
        dismissal.SetActive(false);
        closing=false;
        GlassMenu.DismissActive();
        PopupOpen=false;
        foreach(
            var window in Boards.Cast<GlassWindow>().Append(toolbar).Append(home).Append(controls).Append(pc).Append(
                clock
            ).Append(leftArrow).Append(rightArrow).Append(rail)
        )window.HideImmediately();
        websites.HideImmediately();
        PinWindow.SpaceOpen(false,ToolbarHandle);
        awaySince=edgeSince=DateTime.MinValue;
        railAway=DateTime.UtcNow;
        Store.Log("Repli immédiat · clic extérieur ou changement d’application");
    }
    void Tick(){try{
        if(Program.TaskbarRestoreSignal.WaitOne(0)){SetTaskbar(false);return;}
        if(Program.QuitSignal.WaitOne(0)){
            Store.Log(
                "QuitSignal reçu : arrêt demandé par un second processus --quit (voir la ligne 'Args' correspondante)"
            );
            System.Windows.Application.Current.Shutdown();
            return;
        }
        if(Program.ShowSignal.WaitOne(0)){if(open)Collapse();else Open();return;}
        if(QtShell!=null&&!open)return;taskbar?.PointerTick();
        if(open&&(!detailed||interacting||Pinned||settingsOpen||PopupOpen||!Store.State.AutoHide))return;
        if(!Native.GetCursorPos(out var mouse))return;
        IntPtr foreground=Native.GetForegroundWindow();
        bool stationary=mouse.X==lastMouse.X&&mouse.Y==lastMouse.Y&&foreground==lastForeground;
        lastMouse=mouse;
        lastForeground=foreground;
        if(foreground!=IntPtr.Zero&&!Native.Owns(foreground))lastForeign=foreground;
        if(stationary&&!open&&!rail.IsVisible&&edgeSince==DateTime.MinValue)return;
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X,mouse.Y));
        var work=screen.WorkingArea;
        if(!open&&!closing){bool atEdge=(
            Store.State.RailSide=="right"?mouse.X>=screen.Bounds.Right-4&&
                mouse.X<=screen.Bounds.Right:mouse.X<=screen.Bounds.Left+4&&
                mouse.X>=screen.Bounds.Left
        )&&mouse.Y>work.Top+24&&mouse.Y<work.Bottom-24;
            if((atEdge||rail.IsVisible)&&Store.State.PauseFullscreen&&Native.Fullscreen(screen,Windows)){
                rail.Hide();edgeSince=DateTime.MinValue;return;
            }
            if(atEdge){
                railAway=DateTime.MinValue;
                if(edgeSince==DateTime.MinValue)edgeSince=DateTime.UtcNow;
                if(
                    (DateTime.UtcNow-edgeSince).TotalMilliseconds>=150&&(!rail.IsVisible||railScreen!=screen.DeviceName)
                ){
                    double scale=VisualTreeHelper.GetDpi(rail).DpiScaleX;
                    double height=work.Height/scale*Store.State.RailHeight;
                    rail.MoveInto(
                        (
                            Store.State.RailSide=="right"
                                ?screen.Bounds.Right/scale-Store.State.RailWidth-12:screen.Bounds.Left/scale+12
                        ),
                        work.Top/scale+(work.Height/scale-height)/2,
                        Store.State.RailWidth,
                        height,
                        0,
                        false
                    );
                    railScreen=screen.DeviceName;
                }
            }
            else{
                edgeSince=DateTime.MinValue;
                if(!Contains(rail,mouse)){
                    if(railAway==DateTime.MinValue)railAway=DateTime.UtcNow;
                    if((DateTime.UtcNow-railAway).TotalMilliseconds>650)rail.Hide();
                }else railAway=DateTime.MinValue;
            }
        }else if(open&&!Pinned&&!settingsOpen&&!PopupOpen&&Store.State.AutoHide){
            bool inside=Boards.Any(b=>Contains(b,mouse))||Contains(toolbar,mouse)||websites.Windows.Any(
                w=>Contains(w,mouse)
            );
            if(inside)awaySince=DateTime.MinValue;
            else{
                if(awaySince==DateTime.MinValue)awaySince=DateTime.UtcNow;
                if((DateTime.UtcNow-awaySince).TotalMilliseconds>1800)Collapse();
            }
        }
    }catch(Exception ex){Store.Log("Tick: "+ex.Message);}}
    static bool Contains(Window window,Native.Point point){
        if(!window.IsVisible)return false;
        var dpi=VisualTreeHelper.GetDpi(window).DpiScaleX;
        return point.X>=window.Left*dpi&&point.Y>=window.Top*dpi&&point.X<(window.Left+window.Width)*dpi&&point.Y<(
            window.Top+window.Height
        )*dpi;
    }
    internal void RefreshWebsites()=>websites.Refresh();
    internal void AddWebsite(WebShortcut? site=null,bool chooseImage=false){
        bool previous=settingsOpen;
        settingsOpen=true;
        DialogLayers(true);
        try{
            var dialog=new WebShortcutWindow(this,site,chooseImage){Owner=DialogParent,ShowInTaskbar=preview};
            dialog.ShowDialog();
        }finally{settingsOpen=previous;DialogLayers(previous);awaySince=DateTime.MinValue;}
    }
    internal void RefreshBoards(){
        foreach(var board in Boards.Where(b=>b.IsVisible))board.Refresh(false);
        if(home.IsVisible)home.Refresh();
        if(detailed&&open)websites.Refresh();
    }
    internal void ProbeRightEdge(){
        Collapse();
        var screen=Forms.Screen.PrimaryScreen!;
        double scale=VisualTreeHelper.GetDpi(toolbar).DpiScaleX;
        var probe=new Window{
            Title="Panneaux Glass — test du bord",
            WindowStyle=WindowStyle.None,
            ResizeMode=ResizeMode.NoResize,
            ShowInTaskbar=true,
            Topmost=true,
            Width=6,
            Height=90,
            Left=screen.Bounds.Right/scale-6,
            Top=(screen.WorkingArea.Top+screen.WorkingArea.Height/2)/scale,
            Background=Brushes.SlateGray
        };
        probe.Show();
    }
    internal void ExportPreview(string file="preview-layout.png"){
        try{
            var views=System.Windows.Application.Current.Windows.Cast<Window>().OfType<GlassWindow>().Where(
                w=>w.IsVisible
            ).ToList();
            File.WriteAllText(
                Path.Combine(Store.Root,"window-metrics.json"),
                JsonSerializer.Serialize(views.Select(Native.Describe),Store.Json)
            );
            if(views.Count==0)return;
            double left=views.Min(w=>w.Left)-20,
                top=views.Min(w=>w.Top)-20,
                right=views.Max(w=>w.Left+w.Width)+20,
                bottom=views.Max(w=>w.Top+w.Height)+20;
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen()){
                dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,right-left,bottom-top));
                foreach(var view in views)dc.DrawRectangle(
                    new VisualBrush(view.Surface),
                    null,
                    new Rect(view.Left-left,view.Top-top,view.ActualWidth,view.ActualHeight)
                );
            }
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(right-left),(int)Math.Ceiling(bottom-top),96,96,PixelFormats.Pbgra32
            );
            bitmap.Render(visual);
            using(visual.RenderOpen()){}
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(Store.Root,file));
            encoder.Save(stream);
            Store.Log("Export aperçu · icônes="+Icons.LoadedCount+" · génériques="+Icons.Failures);
        }catch(Exception ex){Store.Log("Export: "+ex);}
    }

}
