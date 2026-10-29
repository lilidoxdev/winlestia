// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Panneau de favoris (bord gauche) : état, actions, surveillance du fichier (drapeau seulement).
// Lecture, analyse et favicons hors fil UI ; l'état n'est modifié que sur le dispatcher. Aucune URL ni aucun titre dans glass.log.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PanneauxGlass;

internal sealed class BookmarkItem {
    public string id{get;set;}="";public string title{get;set;}="";public string url{get;set;}="";public string icon{get;set;}="";public bool folder{get;set;}
    public List<BookmarkItem> children{get;set;}=new();
}

internal sealed class BookmarksService:IDisposable {

    readonly Dispatcher dispatcher;readonly Action changed;
    readonly DispatcherTimer notifyTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    List<BrowserInfo> browsers=new();List<BNode> tree=new();
    string loaded="",error="";volatile bool dirty;bool busy=true,scanning,pending,disposed;DateTime lastScan=DateTime.MinValue;
    FileSystemWatcher? watcher;
    int version,cachedVersion=-1;object? cachedItems;int cachedHidden;

    internal BookmarksService(Dispatcher dispatcher,Action changed){
        this.dispatcher=dispatcher;this.changed=changed;
        var s=Store.State;s.BookmarksHidden??=new();s.BookmarksBrowser??="";
        notifyTimer.Tick+=(_,_)=>{notifyTimer.Stop();if(!disposed)changed();};
    }

    void Changed(){if(disposed)return;if(!notifyTimer.IsEnabled)notifyTimer.Start();}

    internal void Start(){
        _=Task.Run(()=>{try{Directory.Delete(Path.Combine(BookmarksCore.WorkRoot,"tmp"),true);}catch{}}).ContinueWith(t=>dispatcher.BeginInvoke(()=>{if(!disposed)_=Rescan();}));
    }

    BrowserInfo? Loaded=>browsers.FirstOrDefault(b=>b.Id==loaded);

    // ---- État --------------------------------------------------------------------------------------------------
    internal object Snapshot(){
        var current=Loaded;
        return new{version,browser=loaded,browsers=browsers.Select(b=>new{id=b.Id,name=b.Name,installed=b.Installed,isDefault=b.IsDefault}).ToArray(),
            hiddenCount=Hidden(),canDeleteInBrowser=current?.Chromium??false,busy,error};
    }

    int Hidden(){Refill();return cachedHidden;}

    // Les éléments ne transitent que sur demande (commande bookmarksItems) quand la version change.
    internal object Items(){Refill();return new{version=cachedVersion,items=cachedItems??new List<BookmarkItem>()};}

    void Refill(){
        if(cachedVersion!=version){
            var hidden=new HashSet<string>(Store.State.BookmarksHidden.GetValueOrDefault(loaded)??new List<string>());
            int count=0;
            cachedItems=Project(tree,hidden,ref count);cachedHidden=count;cachedVersion=version;
        }
    }

    static List<BookmarkItem> Project(IEnumerable<BNode> nodes,HashSet<string> hidden,ref int hiddenCount){
        var list=new List<BookmarkItem>();
        foreach(var n in nodes){
            if(hidden.Contains(n.Id)){hiddenCount++;continue;}
            var item=new BookmarkItem{id=n.Id,title=n.Title,url=n.Url,icon=n.Icon,folder=n.Folder};
            if(n.Folder)item.children=Project(n.Children,hidden,ref hiddenCount);
            list.Add(item);
        }
        return list;
    }

    // ---- Analyse hors fil UI ------------------------------------------------------------------------------------
    sealed class ScanResult{public List<BrowserInfo> Browsers=new();public string Chosen="";public List<BNode> Tree=new();public string Error="";public int Urls,IconsCreated;}

    ScanResult Scan(string preferred){
        var result=new ScanResult{Browsers=BookmarksCore.Detect()};
        var installed=result.Browsers.Where(b=>b.Installed).ToList();
        var chosen=installed.FirstOrDefault(b=>b.Id==preferred)??installed.FirstOrDefault(b=>b.IsDefault)??installed.FirstOrDefault();
        if(chosen==null)return result;
        result.Chosen=chosen.Id;
        try{
            result.Tree=BookmarksCore.Read(chosen);
            result.Urls=BookmarksCore.CountUrls(result.Tree);
            result.IconsCreated=BookmarksCore.BuildIcons(chosen,result.Tree);
        }catch(Exception ex){result.Error="Lecture impossible ("+ex.GetType().Name+")";Store.Log("Favoris lecture : "+ex.GetType().Name);}
        return result;
    }

    async Task Rescan(){
        if(disposed)return;
        if(scanning){pending=true;return;}
        scanning=true;dirty=false;busy=true;Changed();
        try{
            string preferred=Store.State.BookmarksBrowser;
            var result=await Task.Run(()=>Scan(preferred));
            if(disposed)return;
            browsers=result.Browsers;loaded=result.Chosen;tree=result.Tree;
            if(result.Error.Length>0)error=result.Error;else if(error.StartsWith("Lecture"))error="";
            lastScan=DateTime.UtcNow;version++;
            Watch();
            Store.Log("Favoris : "+(loaded.Length==0?"aucun navigateur":loaded+" "+result.Urls+" favoris, "+result.IconsCreated+" icônes créées"));
        }catch(Exception ex){error="Lecture impossible";Store.Log("Favoris : "+ex.GetType().Name);}
        finally{
            scanning=false;busy=false;version++;Changed();
            if(pending&&!disposed){pending=false;_=Rescan();}
        }
    }

    // ---- Surveillance du fichier (pas de polling), regroupée sur 1 s --------------------------------------------
    void Watch(){
        StopWatching();
        var b=Loaded;
        if(b==null||!Directory.Exists(b.ProfileDir))return;
        try{
            var w=new FileSystemWatcher(b.ProfileDir){Filter=b.Chromium?"Bookmarks":"places.sqlite*",NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size,IncludeSubdirectories=false};
            w.Changed+=OnFile;w.Created+=OnFile;w.Renamed+=OnFile;
            w.EnableRaisingEvents=true;watcher=w;
        }catch(Exception ex){Store.Log("Favoris surveillance : "+ex.GetType().Name);}
    }

    void StopWatching(){
        var w=watcher;watcher=null;if(w==null)return;
        try{w.EnableRaisingEvents=false;w.Changed-=OnFile;w.Created-=OnFile;w.Renamed-=OnFile;w.Dispose();}catch{}
    }

    // Le watcher ne fait que lever un drapeau : aucun rescan tant que le panneau n'est pas affiché (bookmarksRefresh{ifDirty:true}).
    void OnFile(object? sender,FileSystemEventArgs e)=>dirty=true;

    // ---- Actions ----------------------------------------------------------------------------------------------
    internal void Refresh(bool ifDirty){if(ifDirty&&!dirty)return;error="";_=Rescan();}

    internal void ChooseBrowser(string id){
        if(!browsers.Any(b=>b.Id==id&&b.Installed))return;
        Store.State.BookmarksBrowser=id;Store.Save();error="";_=Rescan();
    }

    static BNode? FindNode(IEnumerable<BNode> nodes,string id){
        foreach(var n in nodes){
            if(n.Id==id)return n;
            if(n.Folder&&FindNode(n.Children,id) is {} found)return found;
        }
        return null;
    }

    internal void Open(string id){
        var b=Loaded;var node=FindNode(tree,id);
        if(b==null||node==null||node.Folder||b.Exe.Length==0)return;
        if(!Uri.TryCreate(node.Url,UriKind.Absolute,out var uri)||(uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps&&uri.Scheme!=Uri.UriSchemeFile))return;
        try{
            var start=new ProcessStartInfo(b.Exe){UseShellExecute=false};
            start.ArgumentList.Add(uri.AbsoluteUri);
            Process.Start(start)?.Dispose();
        }catch(Exception ex){error="Ouverture impossible";Store.Log("Favoris ouverture : "+ex.GetType().Name);Changed();}
    }

    internal void Hide(string id){
        if(loaded.Length==0||FindNode(tree,id)==null)return;
        var list=Store.State.BookmarksHidden.TryGetValue(loaded,out var l)?l:Store.State.BookmarksHidden[loaded]=new();
        if(!list.Contains(id)&&list.Count<5000){list.Add(id);Store.Save();version++;Changed();}
    }

    internal void UnhideAll(){
        if(loaded.Length==0)return;
        if(Store.State.BookmarksHidden.Remove(loaded)){Store.Save();version++;Changed();}
    }

    internal async Task DeleteInBrowser(string id,bool confirmSync){
        var b=Loaded;
        if(b==null){return;}
        error="";Changed();
        string? code=await Task.Run(()=>BookmarksCore.DeleteChromium(b,id,confirmSync,Path.Combine(BookmarksCore.WorkRoot,"backups")));
        if(disposed)return;
        error=code switch{null=>"",_=>code};
        Store.Log("Favoris suppression dans le navigateur : "+(code??"ok"));
        Changed();
        if(code==null)_=Rescan();
    }

    public void Dispose(){
        if(disposed)return;disposed=true;
        notifyTimer.Stop();StopWatching();
        try{Directory.Delete(Path.Combine(BookmarksCore.WorkRoot,"tmp"),true);}catch{}
    }
}
