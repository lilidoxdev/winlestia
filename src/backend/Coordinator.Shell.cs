// Shell services: taskbar, shortcuts, tools and the control dialogs.

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

    internal void Open(){if(QtShell!=null){QtShell.Send("toggle");return;}OpenView(false);}
    internal bool TaskbarEnabled=>taskbar?.Enabled==true;
    internal void ToggleTaskbarPin(Entry entry){
        if(!Store.State.TaskbarPins.Remove(entry.Id))Store.State.TaskbarPins.Add(entry.Id);
        Store.Save();
        taskbar?.Refresh();
    }
    internal (bool Running,long Samples) ShellSampling=>home.ShellSampling;
    internal string ShellPage=>home.SelectedPage;
    internal void SetTaskbar(bool enabled){
        bool applied=!enabled||taskbar?.Enable()==true;
        if(!enabled)taskbar?.Disable();
        Store.State.ReplaceTaskbar=enabled&&applied;
        Store.Save();
        if(!applied)Message("Le secours de la barre n’a pas démarré. La barre Windows reste disponible.");
    }
    // Engine shutdown restores the Windows bar without rewriting the user's preference:
    // a crashed frontend must not decide that the dock is unwanted from now on.
    internal void SuspendTaskbar(){taskbar?.Disable();}
    internal void ShowShellPage(string id){
        if(QtShell!=null){
            QtShell.Send(id=="dashboard"?"toggle":id=="media"?"media":id=="performance"?"performance":"access");return;
        }
        if(id=="files"){ShowWorkspace();return;}
        if(!open)Open();
        if(detailed)ShowHome();
        controlsExpanded=pcExpanded=false;
        home.SelectPage(id);
        Arrange(true);
        Native.Raise(home);
    }
    internal void LaunchTarget(string target){
        CollapseImmediately();
        try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception ex){Message(ex.Message);}
    }
    internal void TaskbarMenu(Window parent,FrameworkElement anchor)=>GlassMenu.Open(
        this,
        parent,
        anchor,
        "Ta barre des tâches",
        new[]{
            new MenuAction("Tableau de bord","\uE8A9",()=>ShowShellPage("dashboard")),
            new MenuAction("Applications et fichiers","\uE8B7",()=>ShowWorkspace()),
            new MenuAction("Personnaliser…","\uE713",()=>Settings(),Separator:true),
            new MenuAction("Rétablir la barre Windows","\uE777",()=>SetTaskbar(false)),
            new MenuAction("Quitter Panneaux","\uE711",()=>System.Windows.Application.Current.Shutdown())
        },
        true
    );
    internal void TaskbarAppMenu(Window parent,FrameworkElement anchor,Entry entry,bool pinned){
        GlassMenu.Open(
            this,
            parent,
            anchor,
            entry.Name,
            new[]{
                new MenuAction("Ouvrir une nouvelle fenêtre","\uE8A7",()=>Launch(entry)),
                new MenuAction(
                    pinned?"Détacher de la barre":"Épingler à la barre",
                    "\uE718",
                    ()=>{
                        string id=Store.Entries.Any(e=>e.Id==entry.Id)?entry.Id:entry.Target;
                        if(pinned)Store.State.TaskbarPins.Remove(entry.Id);
                        else if(!string.IsNullOrWhiteSpace(id)&&!Store.State.TaskbarPins.Contains(id))Store
                            .State
                            .TaskbarPins
                            .Add(id);
                        Store.Save();
                        taskbar?.Refresh();
                    }
                ),
                new MenuAction("Tout mon espace","\uE8B7",()=>ShowWorkspace(),Separator:true)
            },
            true
        );
    }
    internal string ShortcutStatus=>shortcut.Error;
    internal string PaletteStatus=>shortcut.PaletteError;
    internal string PinStatus=>shortcut.PinError;
    internal string PaletteLabel=>GlobalShortcut.Label(
        Store.State.PaletteShortcut.Modifiers,Store.State.PaletteShortcut.Key
    );
    internal string PinShortcutLabel=>GlobalShortcut.Label(
        Store.State.PinShortcut.Modifiers,Store.State.PinShortcut.Key
    );
    internal string WallpaperStatus=>shortcut.WallpaperError;
    internal string WallpaperLabel=>shortcut.WallpaperActive?GlobalShortcut.Label(
        shortcut.WallpaperModifiers,shortcut.WallpaperKey
    ):GlobalShortcut.Label(Store.State.WallpaperShortcut.Modifiers,Store.State.WallpaperShortcut.Key);
    internal string CaptureStatus=>shortcut.CaptureError;
    internal string CaptureShortcutLabel=>GlobalShortcut.Label(
        Store.State.CaptureShortcut.Modifiers,Store.State.CaptureShortcut.Key
    );
    internal bool SetPinShortcut(uint modifiers,uint key,out string error){
        if(!shortcut.TrySetPin(modifiers,key,out error))return false;
        Store.State.PinShortcut=new(){Modifiers=modifiers,Key=key};
        Store.Save();
        RefreshPaletteLabels();
        return true;
    }
    internal bool SetCaptureShortcut(uint modifiers,uint key,out string error){
        if(!shortcut.TrySetCapture(modifiers,key,out error))return false;
        Store.State.CaptureShortcut=new(){Modifiers=modifiers,Key=key};
        Store.Save();
        RefreshPaletteLabels();
        return true;
    }
    internal void ToggleAlwaysOnTop(IntPtr? handle=null){
        IntPtr target=handle??Native.GetForegroundWindow();
        if(target!=IntPtr.Zero&&Native.Owns(target))target=lastForeign;
        if(target==IntPtr.Zero||!Native.IsWindow(target)){ShowToast("Aucune fenêtre à épingler");return;}
        bool applied=Native.ToggleTopmost(target,out bool topmost);
        if(!applied){ShowToast("Windows a refusé ce changement");return;}
        string title=Native.Title(target);if(title.Length>38)title=title[..37]+"…";
        ShowToast((topmost?"Au premier plan · ":"Rangée normalement · ")+title);
    }
    internal void ApplyClipboardSettings()=>clipboard.ApplySettings();
    internal void ToggleNotifications(){
        bool next=!SystemTools.NotificationsOn;
        SystemTools.SetNotifications(next);
        ShowToast(next?"Notifications rétablies":"Notifications coupées");
    }
    internal void RestartExplorer(){
        if(
            !Confirm(
                "Redémarrer l’Explorateur Windows ?\n\nLa barre des tâches et le Bureau vont clignoter, tes fenêtres restent ouvertes."
            )
        )return;
        CloseDialogs();
        SystemTools.RestartExplorer();
        ShowToast("Explorateur redémarré");
    }
    internal async void OpenCapture(bool textOnly=false){
        if(QtShell!=null){QtShell.Send(textOnly?"capture-ocr":"capture-select");return;}
        CloseDialogs();
        CollapseImmediately();
        await Task.Delay(260);
        CaptureOverlay.Begin(this,textOnly);
    }
    internal async void OpenColorPicker(){
        CloseDialogs();CollapseImmediately();await Task.Delay(180);ColorTools.OpenPicker(this);
    }
    internal async void OpenRuler(){
        CloseDialogs();CollapseImmediately();await Task.Delay(180);ColorTools.OpenRuler(this);
    }
    internal void OpenMemo(){CollapseImmediately();PinWindow.OpenMemo(this);}
    internal void OpenClipboard(){if(QtShell!=null){QtShell.Send("clipboard");return;}OpenClipboardLegacy();}
    internal ClipboardService QtClipboard=>clipboard;
    internal void OpenClipboardLegacy()=>RunControlDialog(
        open?Anchor:null,
        ()=>{
            var dialog=new ClipboardWindow(this,clipboard){Owner=open?Anchor:null,ShowInTaskbar=preview};
            dialog.ShowDialog();
        }
    );
    internal void OpenAudio(){if(QtShell!=null){QtShell.Send("audio");return;}OpenLegacyAudio();}
    void OpenLegacyAudio()=>RunControlDialog(
        open?Anchor:null,
        ()=>{var dialog=new AudioWindow(this){Owner=open?Anchor:null,ShowInTaskbar=preview};dialog.ShowDialog();}
    );
    internal void OpenWorkspaces()=>RunControlDialog(
        open?Anchor:null,
        ()=>{var dialog=new WorkspaceWindow(this){Owner=open?Anchor:null,ShowInTaskbar=preview};dialog.ShowDialog();}
    );
    internal void OpenLargeFiles()=>RunControlDialog(
        open?Anchor:null,
        ()=>{var dialog=new LargeFilesWindow(this){Owner=open?Anchor:null,ShowInTaskbar=preview};dialog.ShowDialog();}
    );
    internal void OpenTools(){if(QtShell!=null){QtShell.Send("tools");return;}OpenLegacyTools();}
    void OpenLegacyTools()=>RunControlDialog(
        open?Anchor:null,
        ()=>{var dialog=new ToolsWindow(this){Owner=open?Anchor:null,ShowInTaskbar=preview};dialog.ShowDialog();}
    );
    internal void OpenHelp(){
        RunControlDialog(
            open?Anchor:null,
            ()=>{var dialog=new HelpWindow(this){Owner=open?Anchor:null,ShowInTaskbar=preview};dialog.ShowDialog();}
        );
    }
    internal void RefreshPaletteLabels(){
        if(paletteChip!=null){paletteChip.Content=PaletteLabel;paletteChip.ToolTip="Commande rapide · "+PaletteLabel;}
        clock.RefreshLabels();
        palette?.ReloadTabs();
    }
    internal bool SetWallpaperShortcut(uint modifiers,uint key,out string error){
        if(!shortcut.TrySetWallpaper(modifiers,key,out error))return false;
        Store.State.WallpaperShortcut=new(){Modifiers=modifiers,Key=key};
        Store.Save();
        return true;
    }
    internal bool SetPaletteShortcut(uint modifiers,uint key,out string error){
        if(!shortcut.TrySetPalette(modifiers,key,out error))return false;
        Store.State.PaletteShortcut=new(){Modifiers=modifiers,Key=key};
        Store.Save();
        RefreshPaletteLabels();
        return true;
    }
    internal bool SetShortcut(uint modifiers,uint key,string view,out string error){
        if(!shortcut.TrySet(modifiers,key,out error))return false;
        Store.State.Shortcut=new(){Modifiers=modifiers,Key=key,View=view};
        Store.State.Shortcut.Normalize();
        Store.Save();
        BuildRail();
        return true;
    }
    internal void ToggleShortcut(){
        if(QtShell!=null){QtShell.Send("toggle");return;}
        if(open){Collapse();return;}
        var choice=Store.State.Shortcut.View;
        bool full=choice=="workspace"||choice=="last"&&detailed,
            windows=choice=="controls"||choice=="last"&&controlsExpanded,
            status=choice=="pc"||choice=="last"&&pcExpanded;
        OpenView(full,windows,status);
    }

}
