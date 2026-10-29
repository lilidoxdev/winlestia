// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Windows services for the original Caelestia QML frontend.

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

internal sealed partial class QtShellBridge:IDisposable {

    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint pid);

    uint frontendWindowPid;readonly Coordinator owner;readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;

    readonly CancellationTokenSource cancellation=new();readonly EventWaitHandle wake=new(false,EventResetMode.AutoReset,@"Local\PanneauxCaelestiaWake");readonly Queue<(string name,long epoch)> events=new();long frontendEpoch;readonly Action networkChanged;

    readonly ExplorerAppearance explorer=new();readonly WindowTiler tiler=new();readonly ScreenFrameReservations frame=new();readonly WindowsWallpaper wallpaper=new();readonly AudioMixerHost mixer=new();bool mixerVisible;readonly WindowsSpectrum spectrum=new();readonly MediaController media=new();readonly SystemAudio audio=new();readonly WindowsConnections connections=new();readonly PcTelemetry telemetry=new();

    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};

    readonly Dictionary<string,string> icons=new();readonly HashSet<string> requested=new();readonly Dictionary<string,Entry> dockEntries=new();

    readonly SocialService social;readonly BookmarksService bookmarks;readonly OutsideDismiss dismissal;readonly string root,cache;Process? frontend;NamedPipeServerStream? pipe;

    PcReading? reading;MediaSnapshot? player;AudioSnapshot? sound;BrightnessSnapshot? brightness;ConnectionSnapshot? connection;

    DateTime nextConnections,nextBrightness,nextTasks,lastReply;object[] tasks=Array.Empty<object>();

    bool dockVisible,active,controls,pageMusic,busy,disposed,startingMedia;int page,iconRevision;string artworkKey="",artworkPath="";

    static readonly JsonSerializerOptions Json=new(){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};

    internal void DisplaysChanged()=>frame.DisplaysChanged();
    internal bool Visible=>active;internal bool DialogOpen;
    internal bool CapturingShortcut {get;private set;}
    internal void CaptureHotkey(IntPtr data){uint value=unchecked((uint)data.ToInt64());Send("shortcut-key:"+(value&15)+":"+((value>>16)&255));}

    internal    static bool KeepEvent(string name,long queuedEpoch,long currentEpoch)=>name!="close"||queuedEpoch==currentEpoch;

    static string FrontendLog=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Panneaux","Caelestia","frontend.log");

    // Un moteur mort laissait un bureau sans barre et sans explication : on le dit, puis on rend la main.
    static void Fail()=>System.Windows.Application.Current?.Dispatcher.Invoke(()=>{
        System.Windows.MessageBox.Show(
            "Winlestia n'a pas pu démarrer son interface.\n\n"+
            "Le détail de l'incident est dans :\n"+FrontendLog+"\n\n"+
            "La barre des tâches Windows a été rétablie. Réinstalle Winlestia si cela se reproduit.",
            "Winlestia",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);
        System.Windows.Application.Current.Shutdown();
    });

    internal QtShellBridge(Coordinator coordinator){DialogOpen=Program.QtVerifyTools||Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_MATERIAL84")=="1"||Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_THICKNESS85")=="1";owner=coordinator;root=Program.QtRoot??Path.GetFullPath(Path.Combine(Store.Root,"..","Caelestia-Windows"));cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Panneaux","Caelestia");Directory.CreateDirectory(Path.Combine(cache,"icons"));

        dismissal=new OutsideDismiss(()=>Send("close"),()=>active&&!DialogOpen,h=>{GetWindowThreadProcessId(h,out uint pid);return frontend!=null&&(pid==frontend.Id||pid==frontendWindowPid);});

        _=Serve();
        var launched=DateTime.UtcNow;
        var start=new ProcessStartInfo(Path.Combine(root,".venv","Scripts","pythonw.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardError=true};
        // Le PySide6 livré est le seul valable : un site-packages utilisateur ne doit pas s'intercaler.
        start.Environment["PYTHONNOUSERSITE"]="1";
        start.ArgumentList.Add(Path.Combine(root,"frontend.py"));
        start.ArgumentList.Add("--backend-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        if(Program.QtVerifyTools)start.ArgumentList.Add("--verify-tools82");
        else if(Program.QtVerify)start.ArgumentList.Add("--verify");
        else if(Program.QtReview)start.ArgumentList.Add("--preview");
        frontend=Process.Start(start)??throw new InvalidOperationException("Le moteur Qt n'a pas pu être lancé.");
        // Sans console, une trace Python disparaîtrait : on la garde dans glass.log.
        frontend.ErrorDataReceived+=(_,line)=>{if(!disposed&&!string.IsNullOrWhiteSpace(line.Data))Store.Log("Qt · "+line.Data);};
        frontend.BeginErrorReadLine();
        frontend.EnableRaisingEvents=true;
        frontend.Exited+=(_,_)=>_=dispatcher.BeginInvoke((Action)(()=>{
            if(disposed)return;
            bool crashed=!active&&(DateTime.UtcNow-launched).TotalSeconds<10;
            Store.Log(crashed?"Le moteur Qt n'a pas démarré. La barre Windows est rétablie.":"Le moteur Qt s'est arrêté. La barre Windows est rétablie.");
            dockPreview.Close();tiler.SetEnabled(false);frame.Disable();owner.SuspendTaskbar();owner.RestoreDesktopIcons();active=false;CapturingShortcut=false;dismissal.SetActive(false);tiler.SetEnabled(false);StopSampling();
            if(crashed&&!Program.QtVerify&&!Program.QtReview&&!Program.QtVerifyTools)Fail();
        }));
        timer.Tick+=Tick;timer.Start();
        social=new SocialService(dispatcher,()=>Send("social"));_=social.Start();
        bookmarks=new BookmarksService(dispatcher,()=>Send("bookmarks"));bookmarks.Start();networkChanged=()=>{if(!disposed){try{wake.Set();}catch{}}};NetworkKindWatcher.Changed+=networkChanged;NetworkKindWatcher.Start();

        foreach(var entry in Store.HomeFavorites().Take(16))LoadIcon(entry);

    }

    internal void Send(string value){if(frontendWindowPid>0&&(value is "toggle" or "access" or "media" or "performance" or "controls" or "palette" or "settings" or "settings:social" or "settings:bookmarks" or "clipboard" or "audio" or "microphone" or "tools" or "capture"||value.StartsWith("workspace:")))AllowSetForegroundWindow(frontendWindowPid);if(events.Count<32)events.Enqueue((value,frontendEpoch));wake.Set();}

    async Task Serve(){while(!cancellation.IsCancellationRequested){try{using var server=new NamedPipeServerStream("PanneauxCaelestia",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);pipe=server;await server.WaitForConnectionAsync(cancellation.Token);using var reader=new StreamReader(server,new UTF8Encoding(false),false,65536,true);using var writer=new StreamWriter(server,new UTF8Encoding(false),65536,true){AutoFlush=true};while(server.IsConnected&&!cancellation.IsCancellationRequested){string? line=await reader.ReadLineAsync(cancellation.Token);if(line==null||line.Length>131072)break;using var request=JsonDocument.Parse(line);var element=request.RootElement.Clone();var operation=await dispatcher.InvokeAsync(()=>Handle(element));string reply=await operation;await writer.WriteLineAsync(reply.AsMemory(),cancellation.Token);}}catch(OperationCanceledException){break;}catch(Exception ex){if(!disposed)Store.Log("Qt IPC: "+ex.Message);}finally{pipe=null;if(!disposed)_ = dispatcher.BeginInvoke((Action)(()=>{dockPreview.Close();active=false;CapturingShortcut=false;dismissal.SetActive(false);tiler.SetEnabled(false);StopSampling();}));}}}

    static string Text(JsonElement args,string name)=>args.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";

    static double Number(JsonElement args,string name)=>args.TryGetProperty(name,out var value)&&value.TryGetDouble(out double number)&&double.IsFinite(number)?number:0;

    static bool Flag(JsonElement args,string name)=>args.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True;

    async Task<string> Handle(JsonElement request){string? notice=null;object? wallpaperCapture=null;object? bookmarksItems=null;try{string command=Text(request,"command");var args=request.TryGetProperty("args",out var a)?a:default;string id=args.ValueKind==JsonValueKind.Object?Text(args,"id"):"";

        if(args.ValueKind==JsonValueKind.Object&&args.TryGetProperty("epoch",out _)){long epoch=(long)Number(args,"epoch");bool navigationChanged=epoch!=frontendEpoch;if(navigationChanged){DesktopRecents.RefreshSoon();dockPreview.Close();}frontendEpoch=epoch;frontendWindowPid=(uint)Number(args,"pid");bool shown=Flag(args,"visible");tiler.ExcludedProcess=frontendWindowPid;explorer.ExcludedProcess=frontendWindowPid;tiler.SetEnabled(Store.State.AutoTile&&!Program.QtReview&&!Program.QtVerify&&!Program.QtVerifyTools);if(Store.State.ScreenFrame&&!Program.QtVerifyTools)frame.Enable();else frame.Disable();if(!shown)CapturingShortcut=false;page=(int)Number(args,"page");dockVisible=Flag(args,"dock");controls=Flag(args,"controls");mixerVisible=Flag(args,"mixer");if(active!=shown||navigationChanged){active=shown;dismissal.SetActive(active);}lastReply=DateTime.UtcNow;UpdateSampling();}
        if(command=="visualizer")return JsonSerializer.Serialize(new{spectrum=spectrum.Values},Json);

        switch(command){

            case "state":break;
            case "clipboardFiles":if(args.TryGetProperty("paths",out var clipPaths)){RecycleActions.ClipboardFiles(clipPaths.EnumerateArray().Select(p=>p.GetString()??"").ToArray());notice="Fichiers copiés dans le presse-papiers.";}break;
            case "zipFiles":if(args.TryGetProperty("paths",out var zipPaths)){var files=zipPaths.EnumerateArray().Select(p=>p.GetString()??"").ToArray();string? destination=RecycleActions.ChooseZip(files);if(destination!=null){await Task.Run(()=>RecycleActions.Zip(files,destination));notice="Archive ZIP créée.";}}break;
            case "explorerPalette":notice=null;await Task.Run(()=>explorer.Sync(Text(args,"accent"),Text(args,"background"),Text(args,"text")));break;
            case "explorerFolder":using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description="Choisir le dossier à personnaliser",UseDescriptionForTitle=true}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK&&!Store.State.ExplorerFolders.Contains(dialog.SelectedPath)){Store.State.ExplorerFolders.Add(dialog.SelectedPath);Store.Save();}}break;
            case "explorerRestore":Store.State.ExplorerTheme=false;Store.Save();notice=await Task.Run(()=>explorer.Restore());break;
            case "explorerReport":owner.LaunchTarget(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Winlestia","Explorer","last-report.txt"));break;
            case "explorerOpen":owner.LaunchTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));break;
            case "emptyRecycleBin":RecycleActions.Empty();break;
            case "recycleFiles":if(args.TryGetProperty("paths",out var paths)&&paths.ValueKind==JsonValueKind.Array)RecycleActions.Recycle(paths.EnumerateArray().Select(p=>p.GetString()??"").ToArray());break;
            case "wallpaperCapture":{Native.Rect? shotTarget=args.ValueKind==JsonValueKind.Object&&Number(args,"w")>0?new Native.Rect{Left=(int)Number(args,"x"),Top=(int)Number(args,"y"),Right=(int)(Number(args,"x")+Number(args,"w")),Bottom=(int)(Number(args,"y")+Number(args,"h"))}:null;string? shot=null;try{shot=await Task.Run(()=>{try{return WallpaperMedia.CaptureHost(shotTarget);}catch(Exception ex){Store.Log("Capture du fond: "+ex.Message);return null;}}).WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){Store.Log("Capture du fond: delai depasse.");}wallpaperCapture=new{token=Text(args,"token"),ok=shot!=null,path=shot??""};break;}
            case "dockApp":notice=DockAction(args);nextTasks=DateTime.MinValue;break;
            case "dockPreview":DockPreviewOpen(args);break;
            case "dockPreviewEnd":DockPreviewEnd(args);break;
            case "systemAction":DockSystem(Text(args,"id"));break;
            case "captureShortcutMode":CapturingShortcut=Flag(args,"enabled");break;

            case "runningRefresh":RefreshBackgroundApps();nextTasks=DateTime.MinValue;break;
            case "icon":var iconEntry=Store.Entries.FirstOrDefault(e=>e.Id==id)??dockEntries.GetValueOrDefault(id);if(iconEntry==null){lock(DesktopRecents.Entries)DesktopRecents.Entries.TryGetValue(id,out iconEntry);}if(iconEntry!=null)LoadIcon(iconEntry);break;

            case "openFiles":FilesDefaultHandler.Open();break;
            case "launch":var entry=Store.Visible().FirstOrDefault(e=>e.Id==id);if(entry!=null){Send("close");owner.Launch(entry);}break;

            case "activate":long hwnd=(long)Number(args,"hwnd");var app=TaskbarNative.Applications().FirstOrDefault(w=>w.Handle.ToInt64()==hwnd);if(app!=null)TaskbarNative.Activate(app);else if(dockEntries.TryGetValue(id,out var pin))owner.Launch(pin);break;

            case "media":await media.CommandAsync(Text(args,"action"),Number(args,"value"));break;

            case "mediaVolume":await mixer.SetMediaVolume(Math.Clamp(Number(args,"value"),0,100));break;
            case "volume":audio.SetVolume(Math.Clamp(Number(args,"value"),0,100));sound=audio.Read();break;

            case "brightness":Native.GetCursorPos(out var point);await MonitorBrightness.SetAsync(point,Math.Clamp(Number(args,"value"),0,100));nextBrightness=DateTime.MinValue;break;

            case "radio":await connections.ToggleAsync(Text(args,"kind")=="wifi"?RadioKind.WiFi:RadioKind.Bluetooth);connection=await connections.ReadAsync();break;

            case "customTheme":
                if(!args.TryGetProperty("colors",out var colors)||colors.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("Palette invalide.");
                var custom=new Dictionary<string,string>();foreach(string role in new[]{"background","text","primary","secondary","tertiary"}){string color=Text(colors,role);if(!System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))throw new InvalidOperationException("Couleur invalide : "+role);custom[role]=color.ToLowerInvariant();}
                Store.State.CustomPalette=custom;Store.State.CustomGlass=Flag(args,"glass");Store.State.CustomGlassOpacity=Math.Clamp(Number(args,"opacity"),.02,.95);Store.State.Theme="custom";Store.State.AutoTheme=false;Store.Save();owner.SetTheme("custom");break;
            case "theme":Store.State.AutoTheme=Text(args,"mode")=="auto";if(Text(args,"mode")=="custom"){Store.State.Theme="custom";owner.SetTheme("custom");}else if(!Store.State.AutoTheme)owner.SetTheme(Flag(args,"dark")?"nuit":"shell");else owner.SetTheme(Store.State.Theme);Store.Save();break;

            case "workspace":Send("workspace:"+(id is "apps" or "games" or "projects" or "documents"?id:"all"));break;

            case "palette":Send("palette");break;

            case "settings":Send("settings");if(Text(args,"section")=="social")Send("settings:social");else if(Text(args,"section")=="bookmarks")Send("settings:bookmarks");break;
            case "socialOpen":_=social.Open(id,false);break;
            case "socialLogin":_=social.Open(id,true);break;
            case "socialEnable":_=social.Enable(id,Flag(args,"enabled"));break;
            case "socialLogout":_=social.Logout(id);break;
            case "socialMute":social.MuteNet(id,Flag(args,"muted"));break;
            case "socialGlobalMute":social.GlobalMute(Flag(args,"muted"));break;
            case "socialSound":social.SoundEnabled(Flag(args,"enabled"));break;
            case "socialVolume":social.Volume(Number(args,"value"));break;
            case "socialHistoryClear":social.HistoryClear();break;
            case "bookmarksOpen":bookmarks.Open(id);break;
            case "bookmarksBrowser":bookmarks.ChooseBrowser(id);break;
            case "bookmarksHide":bookmarks.Hide(id);break;
            case "bookmarksUnhideAll":bookmarks.UnhideAll();break;
            case "bookmarksDeleteInBrowser":_=bookmarks.DeleteInBrowser(id,Flag(args,"confirmSync"));break;
            case "bookmarksItems":bookmarksItems=bookmarks.Items();break;
            case "bookmarksRefresh":bookmarks.Refresh(Flag(args,"ifDirty"));break;
            case "socialTestSound":social.TestSound();break;

            case "copyText":string copied=Text(args,"text");if(copied.Length<=512)System.Windows.Clipboard.SetText(copied);break;

            case "paletteRun":RunPalette(Text(args,"kind"),id,Text(args,"query"));break;

            case "clipboardCopy":var clip=owner.QtClipboard.Items.FirstOrDefault(e=>e.Id==id);if(clip!=null)owner.QtClipboard.CopyBack(clip);break;

            case "clipboardPin":var pinnedClip=owner.QtClipboard.Items.FirstOrDefault(e=>e.Id==id);if(pinnedClip!=null)owner.QtClipboard.TogglePin(pinnedClip);break;

            case "refreshCatalogue":owner.RefreshCatalogue();break;

            case "move":string board=Text(args,"board");if(board is "apps" or "games" or "projects" or "documents"&&Store.Entries.Any(e=>e.Id==id)){Store.State.BoardOverrides[id]=board;Store.Save();}break;

            case "editEntry":var edited=Store.Entries.FirstOrDefault(e=>e.Id==id);if(edited!=null){string name=Text(args,"name").Trim(),category=Text(args,"category").Trim();if(name.Length is >0 and <=120){edited.Name=name;Store.State.Names[id]=name;}if(category.Length<=100){edited.Category=category;Store.State.Categories[id]=category;}Store.Save();}break;

            case "entryIcon":var changedIcon=Store.Entries.FirstOrDefault(e=>e.Id==id);string path=Text(args,"path");if(changedIcon!=null&&File.Exists(path)&&new[]{".png",".ico",".jpg",".jpeg",".webp"}.Contains(Path.GetExtension(path).ToLowerInvariant())){changedIcon.IconPath=path;Store.State.Icons[id]=path;Store.Save();requested.Remove(id);LoadIcon(changedIcon);}break;

            case "addAccess":Send("close");string addBoard=Text(args,"board");_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(()=>owner.AddAccess(addBoard is "apps" or "games" or "projects" or "documents"?addBoard:"apps"))));break;

            case "addFolder":Send("close");string folderBoard=Text(args,"board");_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(()=>owner.AddFolder(folderBoard is "apps" or "games" or "projects" or "documents"?folderBoard:"documents"))));break;

            case "preference":string preference=Text(args,"key");switch(preference){case "autoTile":Store.State.AutoTile=Flag(args,"enabled");tiler.SetEnabled(Store.State.AutoTile);break;case "accentBorders":Store.State.AccentBorders=Flag(args,"enabled");break;case "explorerTheme":Store.State.ExplorerTheme=Flag(args,"enabled");if(!Store.State.ExplorerTheme)notice=await Task.Run(()=>explorer.Restore());break;case "desktopLogo":Store.State.DesktopLogo=Flag(args,"enabled");break;case "filesDefaultHandler":{bool wantHandler=Flag(args,"enabled");var handlerResult=await Task.Run(()=>FilesDefaultHandler.Set(wantHandler));Store.State.FilesDefaultHandler=handlerResult.pref;notice=handlerResult.notice;break;}case "filesPreload":Store.State.FilesPreload=Flag(args,"enabled");if(Store.State.FilesPreload)FilesPreload.Start();break;case "reducedEffects":Store.State.ReducedEffects=Flag(args,"enabled");owner.SetTheme(Store.State.Theme);break;case "topActivationMargin":Store.State.TopActivationMargin=(int)Math.Clamp(Number(args,"value"),0,160);break;case "dockWidth":Store.State.DockWidth=Math.Clamp(Number(args,"value"),560,4000);break;case "frameThickness":Store.State.FrameThickness=(int)Math.Round(Math.Clamp(Number(args,"value"),4,40));frame.UpdateThickness();break;case "glassOpacity":Store.State.GlassOpacity=Math.Clamp(Number(args,"value"),.02,.95);break;case "motion":Store.State.Motion=Math.Clamp(Number(args,"value"),0,1.5);break;case "iconSize":Store.State.IconSize=Math.Clamp(Number(args,"value"),40,80);break;case "taskbar":owner.SetTaskbar(Flag(args,"enabled"));break;case "autostart":Native.SetAutostart(Flag(args,"enabled"));break;case "screenFrame":Store.State.ScreenFrame=Flag(args,"enabled");if(Store.State.ScreenFrame)frame.Enable();else frame.Disable();break;case "clipboardHistory":Store.State.ClipboardHistory=Flag(args,"enabled");owner.ApplyClipboardSettings();break;}Store.Save();break;

            case "shortcut":uint modifiers=(uint)Number(args,"modifiers"),key=(uint)Number(args,"key");if(modifiers is >0 and <=15&&key is >=0x20 and <=0xFE){string error;bool success=Text(args,"kind")=="palette"?owner.SetPaletteShortcut(modifiers,key,out error):owner.SetShortcut(modifiers,key,"home",out error);notice=success?"Raccourci enregistré":error;if(success)Send("shortcut-saved:"+Text(args,"kind"));}else notice="Choisis Ctrl, Alt ou Windows avec une autre touche.";break;

            case "tools":Send("tools");break;

            case "audio":Send("audio");break;
            case "microphone":Send("microphone");break;
            case "sessionVolume":await mixer.SetApplication(id,Number(args,"value"),null);break;
            case "sessionMute":await mixer.SetApplication(id,null,Flag(args,"muted"));break;
            case "deviceVolume":await mixer.SetDevice(id,Number(args,"value"),null,false);break;
            case "deviceMute":await mixer.SetDevice(id,null,Flag(args,"muted"),false);break;
            case "deviceSelect":await mixer.SetDevice(id,null,null,true);sound=audio.Read();break;
            case "masterMute":audio.SetMute(Flag(args,"muted"));sound=audio.Read();break;
            case "toolRun":RunTool(id);break;
            case "captureExport":notice=await CaptureExport(args);break;

            case "clipboard":Send("clipboard");break;

            case "profiles":Send("close");_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(owner.OpenWorkspaces)));break;

            case "websites":Send("close");_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(()=>owner.AddWebsite())));break;

            case "capture":Send("capture");break;

            case "restoreTaskbar":owner.SetTaskbar(false);break;

            case "quit":_ = dispatcher.BeginInvoke((Action)(()=>System.Windows.Application.Current.Shutdown()));break;

            case "favorite":if(Store.Entries.Any(e=>e.Id==id)){if(!Store.State.Favorites.Remove(id))Store.State.Favorites.Add(id);Store.Save();}break;

            case "reveal":var reveal=Store.Entries.FirstOrDefault(e=>e.Id==id);if(reveal!=null)owner.Reveal(reveal);break;

            case "edit":var edit=Store.Entries.FirstOrDefault(e=>e.Id==id);if(edit!=null)_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(()=>owner.Edit(edit))));break;

            case "website":var site=Store.State.Websites.FirstOrDefault(w=>w.Id==id);if(site!=null)owner.LaunchTarget(site.Url);break;

            case "raiseMedia":var target=Store.Entries.FirstOrDefault(e=>e.Name.Equals(player?.App,StringComparison.OrdinalIgnoreCase));if(target!=null)owner.Launch(target);break;

        }

        }catch(Exception ex){notice=ex.Message;Store.Log("Commande Qt: "+ex.Message);}var queued=events.Where(e=>KeepEvent(e.name,e.epoch,frontendEpoch)).Select(e=>e.name).ToArray();events.Clear();return JsonSerializer.Serialize(new{state=State(),events=queued,notice,wallpaperCapture,bookmarksItems},Json);}

    void RefreshMixer(){foreach(var app in mixer.Applications.Where(a=>!string.IsNullOrEmpty(a.path)))LoadIcon(new Entry{Id=app.id,Name=app.name,Target=app.path,IconPath=app.path});}
    void RunTool(string id){
        switch(id){
            case "capture":Send("capture");return;case "ocr":Send("capture-ocr");return;
            case "audio":Send("audio");return;case "clipboard":Send("clipboard");return;
            case "notifications":owner.ToggleNotifications();return;
        }
        Send("close");_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(()=>{switch(id){
            case "pipette":owner.OpenColorPicker();break;case "ruler":owner.OpenRuler();break;
            case "pin":owner.ToggleAlwaysOnTop();break;case "largefiles":owner.OpenLargeFiles();break;
            case "profiles":owner.OpenWorkspaces();break;case "memo":owner.OpenMemo();break;case "help":owner.OpenHelp();break;
        }})));
    }
    async Task<string> CaptureExport(JsonElement args){
        string path=Path.GetFullPath(Text(args,"path")),directory=Path.GetFullPath(Path.Combine(cache,"captures"))+Path.DirectorySeparatorChar;
        if(!path.StartsWith(directory,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(path)!=".png"||!File.Exists(path)||new FileInfo(path).Length>100_000_000)throw new InvalidOperationException("Capture invalide.");
        BitmapImage image=new();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();image.Freeze();
        try{if(Text(args,"action")=="pin"){PinWindow.Open(owner,image);return "Capture épinglée";}string text=await OcrService.RecognizeAsync(image);if(!string.IsNullOrWhiteSpace(text)){System.Windows.Clipboard.SetText(text);Send("clipboard");return "Texte copié dans le presse-papiers";}Send("capture");return "Aucun texte lisible dans cette zone";}finally{File.Delete(path);}
    }

    void WithDialog(Action action){DialogOpen=true;try{action();}finally{DialogOpen=false;}}

    static bool LightRendering=>Store.State.ReducedEffects??GC.GetGCMemoryInfo().TotalAvailableMemoryBytes<=8L*1024*1024*1024;
    void UpdateSampling(){mixer.SetActive(active&&(page==1||page==7||mixerVisible));if(!active||page!=1)spectrum.Stop();bool need=active&&page is 0 or 2;if(need){if(!telemetry.Running)telemetry.Start(value=>dispatcher.BeginInvoke((Action)(()=>reading=value)),LightRendering?2000:1000,quick:true);}else if(telemetry.Running)_=telemetry.StopAsync();pageMusic=active&&page is 0 or 1||controls;if(pageMusic&&!startingMedia){startingMedia=true;_ =StartMedia();}else if(!pageMusic&&startingMedia){media.Stop();startingMedia=false;}}

    async Task StartMedia(){try{await media.StartAsync();}catch(Exception ex){Store.Log("Musique Qt: "+ex.Message);}}

    void StopSampling(){mixer.SetActive(false);spectrum.Stop();_=telemetry.StopAsync();media.Stop();startingMedia=false;}

    async void Tick(object? sender,EventArgs e){if(disposed||busy)return;if(active&&(DateTime.UtcNow-lastReply).TotalSeconds>5){active=false;StopSampling();dismissal.SetActive(false);}if(!active)return;busy=true;try{if(mixer.Running)RefreshMixer();if(pageMusic){var snapshot=await media.ReadAsync();if(snapshot!=null){player=snapshot;mixer.MediaApp=snapshot.App;var stem=snapshot.App switch{"Microsoft Edge"=>"msedge","Google Chrome"=>"chrome",_=>snapshot.App};spectrum.SetTarget(active&&page==1&&snapshot.Playing?mixer.Applications.FirstOrDefault(a=>Path.GetFileNameWithoutExtension(a.path).Equals(stem,StringComparison.OrdinalIgnoreCase))?.pid??0:0);string key=snapshot.Title+snapshot.Artist;if(snapshot.Artwork!=null&&key!=artworkKey){artworkKey=key;artworkPath=Path.Combine(cache,"cover-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]+".png");SaveImage(snapshot.Artwork,artworkPath);}}}if(controls||page==7){sound=audio.Read();if(DateTime.UtcNow>=nextConnections){nextConnections=DateTime.UtcNow.AddSeconds(5);connection=await connections.ReadAsync();}if(DateTime.UtcNow>=nextBrightness){nextBrightness=DateTime.UtcNow.AddSeconds(5);Native.GetCursorPos(out var point);brightness=await MonitorBrightness.ReadAsync(point);}}}catch(Exception ex){Store.Log("Services Qt: "+ex.Message);}finally{busy=false;}}

    object EntryValue(Entry e)=>new{id=e.Id,name=e.Name,target=e.Target,category=e.Category,board=Store.BoardFor(e),favorite=Store.State.Favorites.Contains(e.Id),recent=Store.State.Recent.IndexOf(e.Id),kind=Store.BoardFor(e) is "projects" or "documents"?"folder":"app"};

    void RunPalette(string kind,string id,string query){if(kind=="systemRecent"){lock(DesktopRecents.Entries)if(DesktopRecents.Entries.TryGetValue(id,out var recent))owner.Launch(recent);return;}if(kind=="app"){var entry=Store.Visible().FirstOrDefault(e=>e.Id==id);if(entry!=null)owner.Launch(entry);return;}if(kind=="quickTab"){var tab=Store.State.QuickTabs.FirstOrDefault(t=>t.Id==id);if(tab!=null)owner.RunQuickTab(tab);return;}if(kind=="website"){var site=Store.State.Websites.FirstOrDefault(w=>w.Id==id);if(site!=null)owner.LaunchTarget(site.Url);return;}if(kind=="web"){owner.WebSearch(query);return;}if(kind!="command")return;_ = dispatcher.BeginInvoke((Action)(()=>WithDialog(()=>{switch(id){case "capture":owner.OpenCapture();break;case "notes":owner.OpenNotes();break;case "focus":owner.OpenFocus();break;case "audio":owner.OpenAudio();break;case "profiles":owner.OpenWorkspaces();break;case "quicktabs":owner.EditQuickTabs();break;case "tools":owner.OpenTools();break;case "taskmgr":SystemActions.TaskManager(owner);break;case "calculator":owner.LaunchTarget("calc.exe");break;}})));}

    void LoadIcon(Entry entry){if(!requested.Add(entry.Id))return;owner.Icons.Load(entry,source=>{
        if(disposed)return;BitmapSource bitmap;if(source is BitmapSource native)bitmap=native;else{var image=new System.Windows.Media.DrawingVisual();using(var dc=image.RenderOpen())dc.DrawImage(source,new System.Windows.Rect(0,0,64,64));var target=new RenderTargetBitmap(64,64,96,96,System.Windows.Media.PixelFormats.Pbgra32);target.Render(image);target.Freeze();bitmap=target;}
        string file=Path.Combine(cache,"icons",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Id+entry.IconPath+entry.Target)))[..24]+".png");
        _=Task.Run(()=>{try{SaveImage(bitmap,file);dispatcher.BeginInvoke((Action)(()=>{if(!disposed){icons[entry.Id]=file;iconRevision++;}}));}catch(Exception ex){dispatcher.BeginInvoke((Action)(()=>requested.Remove(entry.Id)));Store.Log("Icon Qt: "+ex.Message);}});
    },"qt");}

    static void SaveImage(BitmapSource bitmap,string file){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);encoder.Save(stream);}

    void RefreshTasks(){if(!owner.TaskbarEnabled||(!active&&!dockVisible)||DateTime.UtcNow<nextTasks)return;nextTasks=DateTime.UtcNow.AddSeconds(3);RefreshDockApps();}

    object State(){M3.Wallpaper(wallpaper.Monitors,()=>owner.SetTheme(Store.State.Theme));RefreshTasks();var gpu=reading?.Gpus.FirstOrDefault();double mem=reading==null||reading.MemoryTotal==0?0:1-(double)reading.MemoryAvailable/reading.MemoryTotal;long total=reading?.Drives.Sum(d=>d.Total)??0,free=reading?.Drives.Sum(d=>d.Free)??0;var uptime=TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new{systemRecent=DesktopRecents.Values,wallpapers=wallpaper.Monitors,wallpaperRevision=wallpaper.Revision,audioReadMs=mixer.LastReadMilliseconds,audioMixer=new{running=mixer.Running,ready=mixer.Ready,applications=mixer.Applications,devices=mixer.Devices},quickTabs=Store.State.QuickTabs.Select(t=>new{id=t.Id,name=t.Name}).ToArray(),social=social.Snapshot(),bookmarks=bookmarks.Snapshot(),settings=new{accentBorders=Store.State.AccentBorders,explorerStatus=explorer.Status,explorerTheme=Store.State.ExplorerTheme,explorerFolders=Store.State.ExplorerFolders.ToArray(),autoTile=Store.State.AutoTile,desktopLogo=Store.State.DesktopLogo,reducedEffects=LightRendering,customPalette=Store.State.CustomPalette,customGlass=Store.State.CustomGlass,customGlassOpacity=Store.State.CustomGlassOpacity,themeMode=Store.State.AutoTheme?"auto":Store.State.Theme=="custom"?"custom":Store.State.Theme=="shell"?"light":"dark",screenFrame=Store.State.ScreenFrame,topActivationMargin=Store.State.TopActivationMargin,dockWidth=Store.State.DockWidth,frameThickness=Store.State.FrameThickness,glassOpacity=Store.State.GlassOpacity,motionScale=Store.State.Motion,iconSize=Store.State.IconSize,autostart=Native.Autostart,filesDefaultHandler=Store.State.FilesDefaultHandler,filesPreload=Store.State.FilesPreload,clipboardHistory=Store.State.ClipboardHistory,shortcut=GlobalShortcut.Label(Store.State.Shortcut.Modifiers,Store.State.Shortcut.Key),paletteShortcut=owner.PaletteLabel},clipboard=active&&page==6?owner.QtClipboard.Items.Take(80).Select(c=>new{id=c.Id,kind=c.Kind,text=c.Text,image=c.ImagePath,pinned=c.Pinned,at=c.At}).ToArray():Array.Empty<object>(),diagnostics=new{capturingShortcut=CapturingShortcut,root=Store.Root,frontendEpoch,frontendWindowPid,launcherPid=frontend?.Id??0,cpuSeconds=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds,workingSet=Process.GetCurrentProcess().WorkingSet64},dark=Store.State.Theme!="shell",taskbarEnabled=owner.TaskbarEnabled,networkKind=NetworkKindWatcher.Current,performance=new{cpu=reading?.Cpu,cpuTemp=reading?.CpuTemperature,cpuTempSource=reading?.CpuTempSource,gpu=gpu?.Load,gpuTemp=gpu?.Temperature,memPercent=mem,memUsedKiB=reading==null?0:(reading.MemoryTotal-reading.MemoryAvailable)/1024d,diskPercent=total>0?1-(double)free/total:0,diskUsedKiB=(total-free)/1024d,download=reading?.Download,upload=reading?.Upload,samples=telemetry.Samples,sampling=telemetry.Running},sysInfo=new{osName="Windows "+(Environment.OSVersion.Version.Build>=22000?"11":"10"),uptime=$"{(int)uptime.TotalHours} h {uptime.Minutes} min"},media=new{volume=mixer.Applications.Where(a=>System.IO.Path.GetFileNameWithoutExtension(a.path).Equals(player?.App switch{"Microsoft Edge"=>"msedge","Google Chrome"=>"chrome",_=>player?.App},StringComparison.OrdinalIgnoreCase)).Select(a=>a.volume).DefaultIfEmpty(70).Max(),available=player?.Available??false,app=player?.App??"",title=player?.Title??"",artist=player?.Artist??"",album=player?.Album??"",art=player?.Artwork!=null?new Uri(artworkPath).AbsoluteUri:"",playing=player?.Playing??false,position=player?.Position??0,duration=player?.Duration??0,canToggle=player?.CanToggle??false,canNext=player?.CanNext??false,canPrevious=player?.CanPrevious??false,canSeek=player?.CanSeek??false},controls=new{volume=sound?.Volume??0,muted=sound?.Muted??false,device=sound?.Device??"",brightnessAvailable=brightness?.Available??false,brightness=brightness?.Value??0,wifiOn=connection?.Wifi.On??false,bluetoothOn=connection?.Bluetooth.On??false,network=connection?.Network??""},apps=Store.Visible().Select(EntryValue).ToArray(),favorites=Store.HomeFavorites().Select(EntryValue).ToArray(),recent=Store.RecentEntries().Take(24).Select(EntryValue).ToArray(),websites=Store.State.Websites.Select(w=>new{id=w.Id,name=w.Name,icon=w.IconPath}).ToArray(),profiles=Store.State.Workspaces.Select(w=>new{id=w.Id,name=w.Name}).ToArray(),taskbarApps=tasks,runningApps=runningApps,icons,iconRevision};

    }

    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();dockPreview.Dispose();social.Dispose();bookmarks.Dispose();explorer.Dispose();dismissal.Dispose();cancellation.Cancel();pipe?.Dispose();tiler.Dispose();frame.Dispose();wallpaper.Dispose();mixer.Dispose();spectrum.Dispose();media.Dispose();telemetry.Dispose();NetworkKindWatcher.Changed-=networkChanged;NetworkKindWatcher.Stop();audio.Dispose();if(frontend is {HasExited:false}){try{frontend.Kill();frontend.WaitForExit(2000);}catch{}}frontend?.Dispose();wake.Dispose();cancellation.Dispose();}

}

