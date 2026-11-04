// State snapshot sent to the frontend, entry values and their icons.

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

internal sealed partial class QtShellBridge {

    object EntryValue(Entry e)=>new{
        id=e.Id,
        name=e.Name,
        target=e.Target,
        category=e.Category,
        board=Store.BoardFor(e),
        favorite=Store.State.Favorites.Contains(e.Id),
        recent=Store.State.Recent.IndexOf(e.Id),
        kind=Store.BoardFor(e) is "projects" or "documents"?"folder":"app"
    };

    void RefreshTasks(){
        if(!owner.TaskbarEnabled||(!active&&!dockVisible)||DateTime.UtcNow<nextTasks)return;
        nextTasks=DateTime.UtcNow.AddSeconds(3);
        RefreshDockApps();
    }

    object State(){M3.Wallpaper(wallpaper.Monitors,()=>owner.SetTheme(Store.State.Theme));
    RefreshTasks();
    var gpu=reading?.Gpus.FirstOrDefault();
    double mem=reading==null||reading.MemoryTotal==0?0:1-(double)reading.MemoryAvailable/reading.MemoryTotal;
    long total=reading?.Drives.Sum(d=>d.Total)??0,free=reading?.Drives.Sum(d=>d.Free)??0;
    var uptime=TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new{
            systemRecent=DesktopRecents.Values,
            wallpapers=wallpaper.Monitors,
            wallpaperRevision=wallpaper.Revision,
            audioReadMs=mixer.LastReadMilliseconds,
            audioMixer=new{
                running=mixer.Running,ready=mixer.Ready,applications=mixer.Applications,devices=mixer.Devices
            },
            quickTabs=Store.State.QuickTabs.Select(t=>new{id=t.Id,name=t.Name}).ToArray(),
            social=social.Snapshot(),
            bookmarks=bookmarks.Snapshot(),
            settings=new{
                accentBorders=Store.State.AccentBorders,
                explorerStatus=explorer.Status,
                explorerTheme=Store.State.ExplorerTheme,
                explorerFolders=Store.State.ExplorerFolders.ToArray(),
                autoTile=Store.State.AutoTile,
                tileExclusions=tiler.Exclusions,
                desktopLogo=Store.State.DesktopLogo,
                osd=Store.State.Osd,
                quietFullscreen=Store.State.QuietFullscreen,
                desktopIndicator=Store.State.DesktopIndicator,
                dockDrawerOpen=Store.State.DockDrawerOpen,
                activeBorderOnly=Store.State.ActiveBorderOnly,
                reducedEffects=LightRendering,
                customPalette=Store.State.CustomPalette,
                customGlass=Store.State.CustomGlass,
                customGlassOpacity=Store.State.CustomGlassOpacity,
                themeMode=Store.State.AutoTheme
                    ?"auto":Store.State.Theme=="custom"
                    ?"custom":Store.State.Theme=="shell"
                    ?"light":"dark",
                screenFrame=Store.State.ScreenFrame,
                topActivationMargin=Store.State.TopActivationMargin,
                dockWidth=Store.State.DockWidth,
                frameThickness=Store.State.FrameThickness,
                glassOpacity=Store.State.GlassOpacity,
                motionScale=Store.State.Motion,
                iconSize=Store.State.IconSize,
                autostart=Native.Autostart,
                filesDefaultHandler=Store.State.FilesDefaultHandler,
                filesPreload=Store.State.FilesPreload,
                socialBackground=Store.State.SocialBackground,
                clipboardHistory=Store.State.ClipboardHistory,
                dictationEnabled=Store.State.DictationEnabled,
                dictationKey=Store.State.DictationKey,
                shortcut=GlobalShortcut.Label(Store.State.Shortcut.Modifiers,Store.State.Shortcut.Key),
                paletteShortcut=owner.PaletteLabel,
                wallpaperShortcut=owner.WallpaperLabel
            },
            clipboard=active&&page==6?owner.QtClipboard.Items.Take(80).Select(
                c=>new{id=c.Id,kind=c.Kind,text=c.Text,image=c.ImagePath,pinned=c.Pinned,at=c.At}
            ).ToArray():Array.Empty<object>(),
            diagnostics=new{
                capturingShortcut=CapturingShortcut,
                root=Store.Root,
                frontendEpoch,
                frontendWindowPid,
                launcherPid=frontend?.Id??0,
                cpuSeconds=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds,
                workingSet=Process.GetCurrentProcess().WorkingSet64
            },
            dark=Store.State.Theme!="shell",
            taskbarEnabled=owner.TaskbarEnabled,
            networkKind=NetworkKindWatcher.Current,
            virtualDesktops=new{
                count=VirtualDesktops.Current.Count,
                current=VirtualDesktops.Current.Current,
                names=VirtualDesktops.Current.Names
            },
            fullscreenMonitors=FullscreenWatcher.Current,
            quiet=Quiet,
            quietForced=QuietPolicy.Label(quietForce),
            performance=new{
                cpu=reading?.Cpu,
                cpuTemp=reading?.CpuTemperature,
                cpuTempSource=reading?.CpuTempSource,
                gpu=gpu?.Load,
                gpuTemp=gpu?.Temperature,
                memPercent=mem,
                memUsedKiB=reading==null?0:(reading.MemoryTotal-reading.MemoryAvailable)/1024d,
                diskPercent=total>0?1-(double)free/total:0,
                diskUsedKiB=(total-free)/1024d,
                download=reading?.Download,
                upload=reading?.Upload,
                samples=telemetry.Samples,
                sampling=telemetry.Running
            },
            sysInfo=new{
                osName="Windows "+(Environment.OSVersion.Version.Build>=22000?"11":"10"),
                uptime=$"{(int)uptime.TotalHours} h {uptime.Minutes} min",
                userName=displayUserName.Value
            },
            media=new{
                volume=mixer.MediaVolume>=0?Math.Round(mixer.MediaVolume):70,
                durationKnown=player?.DurationKnown??true,
                rate=player?.Rate??1,
                available=player?.Available??false,
                app=player?.App??"",
                title=player?.Title??"",
                artist=player?.Artist??"",
                album=player?.Album??"",
                art=player?.Artwork!=null?new Uri(artworkPath).AbsoluteUri:"",
                playing=player?.Playing??false,
                position=player?.Position??0,
                duration=player?.Duration??0,
                canToggle=player?.CanToggle??false,
                canNext=player?.CanNext??false,
                canPrevious=player?.CanPrevious??false,
                canSeek=player?.CanSeek??false
            },
            controls=new{
                volume=sound?.Volume??0,
                muted=sound?.Muted??false,
                device=sound?.Device??"",
                brightnessAvailable=displays.Any(d=>d.Available),
                brightness=displays.FirstOrDefault(d=>d.Available)?.Value??0,
                displays=displays.Select(d=>new{id=d.Id,name=d.Name,value=d.Value,available=d.Available}).ToArray(),
                wifiOn=connection?.Wifi.On??false,
                wifi=wifiVisible?wifi.Snapshot():null,
                bluetoothOn=connection?.Bluetooth.On??false,
                network=connection?.Network??""
            },
            apps=Store.Visible().Select(EntryValue).ToArray(),
            favorites=Store.HomeFavorites().Select(EntryValue).ToArray(),
            recent=Store.RecentEntries().Take(24).Select(EntryValue).ToArray(),
            websites=Store.State.Websites.Select(w=>new{id=w.Id,name=w.Name,icon=w.IconPath}).ToArray(),
            profiles=Store.State.Workspaces.Select(w=>new{id=w.Id,name=w.Name}).ToArray(),
            taskbarApps=tasks,
            runningApps=runningApps,
            icons,
            iconRevision
        };

    }

    void RefreshMixer(){
        foreach(var app in mixer.Applications.Where(a=>!string.IsNullOrEmpty(a.path)))LoadIcon(
            new Entry{Id=app.id,Name=app.name,Target=app.path,IconPath=app.path}
        );
    }

    void LoadIcon(Entry entry){if(!requested.Add(entry.Id))return;owner.Icons.Load(entry,source=>{
        if(disposed)return;
        BitmapSource bitmap;
        if(source is BitmapSource native)bitmap=native;
        else{
            var image=new System.Windows.Media.DrawingVisual();
            using(var dc=image.RenderOpen())dc.DrawImage(source,new System.Windows.Rect(0,0,64,64));
            var target=new RenderTargetBitmap(64,64,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            target.Render(image);
            target.Freeze();
            bitmap=target;
        }
        bool fallback=owner.Icons.IsPlaceholder(source);
        string file=Path.Combine(
            cache,
            "icons",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Id+entry.IconPath+entry.Target)))[..24]+(
                fallback?".fb.png":".png"
            )
        );
        _=Task.Run(
            ()=>{
                try{
                    SaveImage(bitmap,file);
                    dispatcher.BeginInvoke(
                        (Action)(
                            ()=>{
                                if(disposed)return;
                                string previous=icons.GetValueOrDefault(entry.Id,"");
                                icons[entry.Id]=file;
                                iconRevision++;
                                if(fallback)RetryIcon(entry);
                                else{
                                    placeholderIcons.Remove(entry.Id);
                                    if(previous.EndsWith(".fb.png",StringComparison.Ordinal)){
                                        try{File.Delete(previous);}catch{}
                                    }
                                }
                            }
                        )
                    );
                }catch(Exception ex){
                    dispatcher.BeginInvoke((Action)(()=>requested.Remove(entry.Id)));Store.Log("Icon Qt: "+ex.Message);
                }
            }
        );
    },"qt");}

    // A stand-in tile is shown from a separate .fb.png file and replaced by the real PNG once a bounded retry succeeds.
    readonly HashSet<string> placeholderIcons=new();
    void RetryIcon(Entry entry){
        placeholderIcons.Add(entry.Id);
        if(CatalogueWatchRules.IconRetryDelay(owner.Icons.FailureCount(entry)) is not TimeSpan delay)return;
        _=Task.Delay(delay).ContinueWith(
            _=>dispatcher.BeginInvoke(
                (Action)(
                    ()=>{
                        if(disposed||!placeholderIcons.Contains(entry.Id))return;
                        requested.Remove(entry.Id);
                        LoadIcon(Store.Entries.FirstOrDefault(e=>e.Id==entry.Id)??entry);
                    }
                )
            )
        );
    }
    // Catalogue read again: failed icons get a fresh series of attempts.
    internal void CatalogueChanged(){
        dispatcher.BeginInvoke(
            (Action)(
                ()=>{
                    if(disposed)return;
                    owner.Icons.ResetFailures();
                    foreach(string id in placeholderIcons.ToArray()){
                        var entry=Store.Entries.FirstOrDefault(e=>e.Id==id);
                        if(entry==null||owner.Icons.FailureCount(entry)>0)continue;
                        requested.Remove(id);
                        LoadIcon(entry);
                    }
                }
            )
        );
    }

    static void SaveImage(BitmapSource bitmap,string file){
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file);
        encoder.Save(stream);
    }

}
