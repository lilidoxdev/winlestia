// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Réseaux sociaux en arrière-plan (WebView2) : connexion, compteur de notifications, son.
// Un seul environnement WebView2 (un seul navigateur partagé), un profil par réseau (cookies persistants séparés).
// Tout s'exécute sur le dispatcher WPF ; les écritures disque (compteurs, préférences) partent sur un thread d'arrière-plan.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PanneauxGlass;

internal sealed class SocialService:IDisposable {

    [StructLayout(LayoutKind.Sequential)]struct MemoryStatus{public uint Length,Load;public ulong TotalPhys,AvailPhys,TotalPageFile,AvailPageFile,TotalVirtual,AvailVirtual,AvailExtendedVirtual;}
    [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    static readonly Regex TitleCount=new(@"^\s*\(([\d\s\u00a0\u202f,.]*\d\+?)\)",RegexOptions.Compiled);
    static readonly Regex TitleCountGmail=new(@"\(([\d\s\u00a0\u202f,.]*\d\+?)\)\s*$",RegexOptions.Compiled);
    const ulong LowMemory=4UL<<30,RecoveredMemory=5UL<<30;

    internal sealed class HistoryItem{public int Seq{get;set;}public string Net{get;set;}="";public string Title{get;set;}="";public string Body{get;set;}="";public string Kind{get;set;}="notif";public long At{get;set;}}

    sealed class Net {
        public string Id="",Name="",Url="",Login="";public string[] Hosts=Array.Empty<string>();public bool Script;
        public string[] Allowed=Array.Empty<string>();public Task? CreateTask;public HistoryItem? LastCountEntry;
        public readonly UnreadCounter U=new();public int Count{get=>U.Count;set=>U.Count=value;}public bool Connected,Navigating,Creating,Wanted,Suspended,ForceClose,Armed,Arming;
        public DateTime LastNative=DateTime.MinValue,TitleChangedAt=DateTime.MinValue,NextCheck=DateTime.MinValue;
        public System.Windows.Window? Win;public CoreWebView2Controller? Ctl;public CoreWebView2? Wv;
    }

    readonly Net[] nets={
        new(){Id="discord",Name="Discord",Url="https://discord.com/channels/@me",Login="https://discord.com/login",Hosts=new[]{"discord.com"},Allowed=new[]{"discord.com","discordapp.com"}},
        new(){Id="instagram",Name="Instagram",Url="https://www.instagram.com/direct/inbox/",Hosts=new[]{"instagram.com"},Allowed=new[]{"instagram.com","facebook.com"}},
        new(){Id="snapchat",Name="Snapchat",Url="https://web.snapchat.com/",Hosts=new[]{"snapchat.com"},Allowed=new[]{"snapchat.com","accounts.snapchat.com"}},
        new(){Id="whatsapp",Name="WhatsApp",Url="https://web.whatsapp.com/",Hosts=new[]{"whatsapp.com"},Allowed=new[]{"whatsapp.com","web.whatsapp.com"},Script=true},
        new(){Id="telegram",Name="Telegram",Url="https://web.telegram.org/k/",Hosts=new[]{"telegram.org"},Allowed=new[]{"telegram.org","web.telegram.org","t.me"},Script=true},
        new(){Id="gmail",Name="Gmail",Url="https://mail.google.com/mail/u/0/#inbox",Hosts=new[]{"mail.google.com"},Allowed=new[]{"mail.google.com","accounts.google.com","www.google.com","google.com","ssl.gstatic.com","googleusercontent.com","myaccount.google.com"}},
    };

    readonly Dispatcher dispatcher;readonly Action changed;
    readonly DispatcherTimer notifyTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    readonly DispatcherTimer slowTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    readonly string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Winlestia","Social");
    readonly object saveLock=new();
    Task<CoreWebView2Environment>? environment;
    // Historique : modifié et lu uniquement sur le dispatcher (les écritures disque reçoivent une copie).
    readonly List<HistoryItem> history=new();int nextSeq=1;
    bool disposed,countsDirty,prefsSaveQueued,saveQueued,started,lowMemory;
    DateTime bootUtc;

    internal SocialService(Dispatcher dispatcher,Action changed){
        this.dispatcher=dispatcher;this.changed=changed;
        var s=Store.State;s.SocialEnabled??=new();s.SocialNetMuted??=new();s.SocialConnected??=new();s.SocialVolume=Math.Clamp(s.SocialVolume,0,100);
        bootUtc=DateTime.UtcNow-TimeSpan.FromMilliseconds(Environment.TickCount64);
        foreach(var n in nets){n.Connected=s.SocialConnected.GetValueOrDefault(n.Id);}
        LoadCounts();
        notifyTimer.Tick+=(_,_)=>{notifyTimer.Stop();if(!disposed)changed();};
        slowTimer.Tick+=SlowTick;
    }

    bool Enabled(Net n)=>Store.State.SocialEnabled.TryGetValue(n.Id,out bool chosen)?chosen:true;
    Net? Find(string id)=>nets.FirstOrDefault(n=>n.Id==id);

    // ---- État pour le front ----------------------------------------------------------------------------------
    internal object Snapshot()=>new{muted=Store.State.SocialMuted,sound=Store.State.SocialSound,volume=Store.State.SocialVolume,
        history=history.Select(h=>new{seq=h.Seq,net=h.Net,title=h.Title,body=h.Body,kind=h.Kind,at=h.At}).ToArray(),
        networks=nets.Select(n=>new{id=n.Id,name=n.Name,enabled=Enabled(n),connected=n.Connected,muted=Store.State.SocialNetMuted.GetValueOrDefault(n.Id),count=n.Count,busy=n.Creating||n.Navigating}).ToArray()};

    // Coalesce les rafales : au plus un push "social" toutes les 250 ms.
    void Changed(){if(disposed)return;if(!notifyTimer.IsEnabled)notifyTimer.Start();}

    // ---- Démarrage échelonné ---------------------------------------------------------------------------------
    internal async Task Start(){
        if(started||disposed)return;started=true;
        try{
            await Task.Delay(6000);
            foreach(var n in nets){
                if(disposed)return;
                if(Enabled(n)&&n.Connected)await CreateAsync(n);
                await Task.Delay(4000);
            }
        }catch(Exception ex){Store.Log("Social démarrage: "+ex.Message);}
    }

    // ---- Actions -----------------------------------------------------------------------------------------------
    internal async Task Open(string id,bool login){
        var n=Find(id);if(n==null||disposed)return;
        try{
            if(login&&!Enabled(n)){Store.State.SocialEnabled[n.Id]=true;Store.Save();}
            n.Wanted=true;
            if(n.Ctl==null)await CreateAsync(n,navigate:false);
            if(n.Ctl==null&&n.Wanted)await CreateAsync(n,navigate:false);
            if(n.Ctl==null||n.Wv==null||n.Win==null||disposed)return;
            ShowWindow(n);
            string target=login&&!n.Connected&&n.Login.Length>0?n.Login:n.Url;
            if(!OnService(n,n.Wv.Source)||(login&&!n.Connected&&n.Login.Length>0&&!SamePath(n.Wv.Source,n.Login)&&!OnLoginPage(n)))n.Wv.Navigate(target);
            Changed();
        }catch(Exception ex){Store.Log("Social ouverture: "+ex.Message);}
    }

    internal async Task Enable(string id,bool enabled){
        var n=Find(id);if(n==null)return;
        Store.State.SocialEnabled[n.Id]=enabled;Store.Save();
        if(enabled){if(n.Connected){n.Wanted=true;await CreateAsync(n);if(n.Ctl==null&&n.Wanted)await CreateAsync(n);}}
        else{n.Wanted=false;CloseView(n);}
        Changed();
    }

    internal async Task Logout(string id){
        var n=Find(id);if(n==null)return;
        n.Wanted=false;
        try{
            if(n.Wv!=null){var profile=n.Wv.Profile;await ClearAsync(profile);CloseView(n);}
            else{
                // Pas de vue ouverte : on en ouvre une, invisible, juste pour atteindre le profil.
                var env=await GetEnvironment();var opt=env.CreateCoreWebView2ControllerOptions();opt.ProfileName=n.Id;
                var host=new System.Windows.Window{ShowInTaskbar=false,Width=1,Height=1,WindowStyle=System.Windows.WindowStyle.None};
                var handle=new WindowInteropHelper(host).EnsureHandle();var ctl=await env.CreateCoreWebView2ControllerAsync(handle,opt);
                try{await ClearAsync(ctl.CoreWebView2.Profile);}finally{ctl.Close();host.Close();}
            }
        }catch(Exception ex){Store.Log("Social déconnexion: "+ex.Message);}
        n.U.Reset();SetConnected(n,false);countsDirty=true;QueueSave();Changed();
    }

    static Task ClearAsync(CoreWebView2Profile profile)=>profile.ClearBrowsingDataAsync();

    internal void MuteNet(string id,bool muted){var n=Find(id);if(n==null)return;Store.State.SocialNetMuted[n.Id]=muted;Store.Save();Changed();}
    internal void GlobalMute(bool muted){Store.State.SocialMuted=muted;Store.Save();Changed();}
    internal void SoundEnabled(bool enabled){Store.State.SocialSound=enabled;Store.Save();Changed();}
    internal void Volume(double value){Store.State.SocialVolume=(int)Math.Round(Math.Clamp(value,0,100));Store.Save();Changed();}
    internal void HistoryClear(){if(history.Count==0)return;history.Clear();foreach(var n in nets)n.LastCountEntry=null;countsDirty=true;QueueSave();Changed();}
    internal void TestSound()=>SocialChime.Play(Store.State.SocialVolume);

    // ---- Environnement et vues -------------------------------------------------------------------------------
    Task<CoreWebView2Environment> GetEnvironment(){
        if(environment==null||environment.IsFaulted||environment.IsCanceled){
            Directory.CreateDirectory(folder);
            environment=CoreWebView2Environment.CreateAsync(null,Path.Combine(folder,"WebView2"),null);
        }
        return environment;
    }

    // Une seule création à la fois par réseau : Start, Open et Enable attendent la même tâche.
    async Task CreateAsync(Net n,bool navigate=true){
        if(n.Ctl!=null||disposed)return;
        if(n.CreateTask!=null){await n.CreateTask;return;}
        var task=CreateCore(n,navigate);n.CreateTask=task;
        try{await task;}finally{if(ReferenceEquals(n.CreateTask,task))n.CreateTask=null;}
    }

    async Task CreateCore(Net n,bool navigate){
        if(n.Ctl!=null||disposed)return;
        n.Creating=true;n.Wanted=true;Changed();
        try{
            var env=await GetEnvironment();
            if(!n.Wanted||disposed)return;
            EnsureWindow(n);
            var handle=new WindowInteropHelper(n.Win!).EnsureHandle();
            var options=env.CreateCoreWebView2ControllerOptions();options.ProfileName=n.Id;
            var ctl=await env.CreateCoreWebView2ControllerAsync(handle,options);
            if(!n.Wanted||disposed||n.Win==null){ctl.Close();CloseView(n);return;}
            n.Ctl=ctl;n.Wv=ctl.CoreWebView2;n.U.Title=0;n.Armed=false;n.Arming=false;
            n.Wv.Settings.IsStatusBarEnabled=false;n.Wv.Settings.IsPasswordAutosaveEnabled=false;n.Wv.Settings.IsGeneralAutofillEnabled=false;
            n.Wv.NewWindowRequested+=OnNewWindow;n.Wv.IsMuted=!n.Win.IsVisible;
            n.Wv.NavigationStarting+=OnNavigationStarting;n.Wv.NavigationCompleted+=OnNavigationCompleted;n.Wv.SourceChanged+=OnSourceChanged;
            n.Wv.DocumentTitleChanged+=OnTitleChanged;n.Wv.PermissionRequested+=OnPermission;n.Wv.NotificationReceived+=OnNotification;
            n.Wv.ProcessFailed+=OnProcessFailed;
            ctl.IsVisible=n.Win!.IsVisible;n.Wv.MemoryUsageTargetLevel=n.Win.IsVisible?CoreWebView2MemoryUsageTargetLevel.Normal:CoreWebView2MemoryUsageTargetLevel.Low;
            SyncBounds(n);
            if(!slowTimer.IsEnabled)slowTimer.Start();
            if(navigate)n.Wv.Navigate(n.Url);
        }catch(Exception ex){Store.Log("Social vue "+n.Id+": "+ex.Message);}
        finally{n.Creating=false;Changed();}
    }

    void EnsureWindow(Net n){
        if(n.Win!=null)return;
        var w=new System.Windows.Window{Title="Winlestia — "+n.Name,Width=1100,Height=800,WindowStartupLocation=System.Windows.WindowStartupLocation.CenterScreen,ShowInTaskbar=false,
            Background=System.Windows.Media.Brushes.Black,ShowActivated=true};
        w.Closing+=(_,e)=>{if(n.ForceClose)return;e.Cancel=true;HideWindow(n);};
        w.SizeChanged+=(_,_)=>SyncBounds(n);
        w.DpiChanged+=(_,_)=>SyncBounds(n);
        n.Win=w;
    }

    void SyncBounds(Net n){
        if(n.Ctl==null||n.Win==null||!n.Win.IsVisible)return;
        try{
            var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(n.Win);
            n.Ctl.Bounds=new System.Drawing.Rectangle(0,0,Math.Max(1,(int)Math.Round(n.Win.ActualWidth*dpi.DpiScaleX)),Math.Max(1,(int)Math.Round(n.Win.ActualHeight*dpi.DpiScaleY)));
        }catch{}
    }

    void ShowWindow(Net n){
        if(n.Win==null||n.Ctl==null||n.Wv==null)return;
        if(n.U.ResetOnOpen()){countsDirty=true;QueueSave();Changed();}
        if(n.Suspended){try{n.Wv.Resume();}catch{}n.Suspended=false;}
        n.Wv.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Normal;n.Wv.IsMuted=false;
        n.Win.ShowInTaskbar=true;n.Win.Show();
        if(n.Win.WindowState==System.Windows.WindowState.Minimized)n.Win.WindowState=System.Windows.WindowState.Normal;
        n.Ctl.IsVisible=true;SyncBounds(n);n.Win.Activate();
        try{n.Ctl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);}catch{}
    }

    void HideWindow(Net n){
        if(n.Win==null)return;
        n.Win.ShowInTaskbar=false;n.Win.Hide();
        try{if(n.Ctl!=null)n.Ctl.IsVisible=false;if(n.Wv!=null){n.Wv.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Low;n.Wv.IsMuted=true;}}catch{}
    }

    // Ferme la vue et sa fenêtre (désabonne tout, libère le contrôleur). Le profil sur disque reste sauf déconnexion.
    void CloseView(Net n){
        var wv=n.Wv;var ctl=n.Ctl;var win=n.Win;n.Wv=null;n.Ctl=null;n.Win=null;n.Suspended=false;n.Navigating=false;n.Armed=false;n.Arming=false;n.U.CancelZero();
        try{
            if(wv!=null){
                wv.NewWindowRequested-=OnNewWindow;wv.NavigationStarting-=OnNavigationStarting;wv.NavigationCompleted-=OnNavigationCompleted;wv.SourceChanged-=OnSourceChanged;
                wv.DocumentTitleChanged-=OnTitleChanged;wv.PermissionRequested-=OnPermission;wv.NotificationReceived-=OnNotification;wv.ProcessFailed-=OnProcessFailed;
            }
        }catch{}
        try{ctl?.Close();}catch{}
        if(win!=null){n.ForceClose=true;try{win.Close();}catch{}n.ForceClose=false;}
        if(nets.All(x=>x.Ctl==null))slowTimer.Stop();
    }

    // ---- Événements WebView2 ---------------------------------------------------------------------------------
    Net? Owner(object? sender)=>sender is CoreWebView2 wv?nets.FirstOrDefault(n=>ReferenceEquals(n.Wv,wv)):null;

    void OnNavigationStarting(object? sender,CoreWebView2NavigationStartingEventArgs e){
        var n=Owner(sender);if(n==null)return;
        if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)&&(uri.Scheme==Uri.UriSchemeHttp||uri.Scheme==Uri.UriSchemeHttps)&&!IsAllowed(n,uri)){e.Cancel=true;OpenExternal(uri);return;}
        n.Navigating=true;Changed();
    }

    void OnNewWindow(object? sender,CoreWebView2NewWindowRequestedEventArgs e){
        var n=Owner(sender);if(n==null)return;
        e.Handled=true;
        if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri))return;
        if(IsAllowed(n,uri)&&(uri.Scheme==Uri.UriSchemeHttp||uri.Scheme==Uri.UriSchemeHttps))n.Wv?.Navigate(uri.AbsoluteUri);else OpenExternal(uri);
    }

    static bool IsAllowed(Net n,Uri u)=>n.Allowed.Any(h=>u.Host.Equals(h,StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));

    static void OpenExternal(Uri uri){
        if(uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps)return;
        try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){Store.Log("Social lien externe: "+ex.Message);}
    }

    void OnNavigationCompleted(object? sender,CoreWebView2NavigationCompletedEventArgs e){
        var n=Owner(sender);if(n==null)return;
        n.Navigating=false;EvaluateUrl(n);MaybeArm(n);Changed();
        _=CheckLater(n,3000);
    }

    void OnSourceChanged(object? sender,CoreWebView2SourceChangedEventArgs e){var n=Owner(sender);if(n!=null)EvaluateUrl(n);}

    // null = titre non informatif (Gmail : mail ouvert, autre vue) : on ne touche à rien.
    static readonly HashSet<string> GmailLists=new(StringComparer.OrdinalIgnoreCase){"","inbox","starred","snoozed","sent","drafts","imp","chats","scheduled","all","spam","trash","category","label","search"};
    static readonly Regex GmailMessageId=new("^[A-Za-z0-9]{16,}$",RegexOptions.Compiled);

    // Vue de liste Gmail (#inbox, #category/..., #label/x, #search/...) sans identifiant de message en dernier segment.
    static bool GmailListView(string? source){
        if(!Uri.TryCreate(source,UriKind.Absolute,out var u))return false;
        string[] parts=u.Fragment.TrimStart('#').Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0)return true;
        if(!GmailLists.Contains(parts[0]))return false;
        return !(parts.Length>=2&&GmailMessageId.IsMatch(parts[^1]));
    }

    static int? ParseUnread(string id,string title,string? source=null){
        if(id=="gmail"){
            if(!GmailListView(source)||!title.TrimEnd().EndsWith("Gmail",StringComparison.Ordinal))return null;
            var g=TitleCountGmail.Match(title.Split(" - ")[0]);
            if(!g.Success)return 0;
            string gd=new string(g.Groups[1].Value.Where(char.IsDigit).ToArray());
            return int.TryParse(gd,out int gv)?Math.Min(gv,99999):null;
        }
        var m=TitleCount.Match(title);
        if(m.Success){string digits=new string(m.Groups[1].Value.Where(char.IsDigit).ToArray());if(int.TryParse(digits,out int v))return Math.Min(v,99999);return 1;}
        string t=title.TrimStart();
        return t.Length>0&&"•*●".IndexOf(t[0])>=0?1:0;
    }

    // Compteur = non-lus réels : pour un réseau dont le titre affiche un nombre, le titre fait foi (Count = titre).
    // Une chute à 0 n'efface le badge qu'après 5 s (une transition SPA ne compte pas). Son + historique seulement sur une vraie hausse.
    void OnTitleChanged(object? sender,object e){
        var n=Owner(sender);if(n==null||n.Wv==null)return;
        int? parsed=ParseUnread(n.Id,n.Wv.DocumentTitle??"",n.Wv.Source);var now=DateTime.UtcNow;
        n.TitleChangedAt=now;
        if(parsed==null)return;
        var r=n.U.OnTitle(parsed.Value,now);
        if(r.Changed){countsDirty=true;QueueSave();Changed();}
        if(r.Rise>0&&n.Armed&&!r.AfterNotif){int d=r.Rise;Chime(n);n.LastCountEntry=AddHistory(n,"count",d==1?"1 nouveau message":d+" nouveaux messages","");}
        if(r.ZeroToken>0)_=ResetLater(n,r.ZeroToken);
        if(n.Script&&now>=n.NextCheck)_=CheckLater(n,500);
    }

    async Task ResetLater(Net n,int token){
        try{await Task.Delay(5000);}catch{return;}
        if(!disposed&&n.U.ZeroExpired(token)){countsDirty=true;QueueSave();Changed();}
    }

    // Absorption des non-lus initiaux : au premier état connecté dont le titre est stable 8 s, ce qui est affiché devient le niveau de départ sans compter.
    void MaybeArm(Net n){if(n.Armed||n.Arming||!n.Connected||n.Wv==null||disposed)return;n.Arming=true;_=ArmAsync(n);}

    async Task ArmAsync(Net n){
        try{
            for(int i=0;i<6;i++){
                await Task.Delay(8000);
                if(disposed||n.Wv==null||!n.Connected)return;
                if((DateTime.UtcNow-n.TitleChangedAt).TotalSeconds>=7.5)break;
            }
            if(disposed||n.Wv==null||!n.Connected)return;
            if(n.U.Absorb()){countsDirty=true;QueueSave();Changed();}
            n.Armed=true;
        }finally{n.Arming=false;}
    }

    void OnPermission(object? sender,CoreWebView2PermissionRequestedEventArgs e){
        var n=Owner(sender);if(n==null||e.PermissionKind!=CoreWebView2PermissionKind.Notifications)return;
        if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)&&HostMatches(n,uri)){e.State=CoreWebView2PermissionState.Allow;e.Handled=true;}
    }

    void OnNotification(object? sender,CoreWebView2NotificationReceivedEventArgs e){
        var n=Owner(sender);if(n==null)return;
        e.Handled=true; // pas de toast Windows : seul le compteur et le son de Winlestia comptent
        var now=DateTime.UtcNow;n.LastNative=now;
        string title=Clip(e.Notification?.Title,120),body=Clip(e.Notification?.Body,140);
        // La hausse de titre (+d) a déjà compté d messages : chaque notification arrivée dans les 3 s consomme un de ces d crédits
        // (pas de double compte) ; au-delà, c'est une vraie notification en plus et elle compte normalement.
        if(n.U.ConsumeCredit(now)){
            var entry=n.LastCountEntry;n.LastCountEntry=null;
            if(entry!=null&&history.Contains(entry)){entry.Kind="notif";entry.Title=title;entry.Body=body;countsDirty=true;QueueSave();Changed();}
            else AddHistory(n,"notif",title,body);
            return;
        }
        n.U.NoteNotification(now);
        // Réseau dont le titre fait foi : la notification ne compte pas (le titre suit). Sinon +1.
        if(!n.U.TitleSeen)Add(n,1);
        AddHistory(n,"notif",title,body);
    }

    void OnProcessFailed(object? sender,CoreWebView2ProcessFailedEventArgs e){var n=Owner(sender);Store.Log("Social processus "+(n?.Id??"?")+": "+e.ProcessFailedKind);}

    static string Clip(string? text,int max){text=(text??"").Replace('\r',' ').Replace('\n',' ').Trim();return text.Length>max?text[..max]:text;}

    HistoryItem AddHistory(Net n,string kind,string title,string body){
        var item=new HistoryItem{Seq=nextSeq++,Net=n.Id,Title=title,Body=body,Kind=kind,At=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};
        history.Insert(0,item);if(history.Count>100)history.RemoveRange(100,history.Count-100);
        countsDirty=true;QueueSave();Changed();return item;
    }

    // ---- Comptage et son --------------------------------------------------------------------------------------
    void Add(Net n,int delta){
        if(delta<=0||disposed)return;
        n.Count=(int)Math.Min(999999L,(long)n.Count+delta);
        countsDirty=true;QueueSave();Changed();
        Chime(n);
    }

    void Chime(Net n){
        if(disposed)return;
        var s=Store.State;
        if(s.SocialSound&&!s.SocialMuted&&!s.SocialNetMuted.GetValueOrDefault(n.Id))SocialChime.Play(s.SocialVolume);
    }

    // ---- Connexion ---------------------------------------------------------------------------------------------
    static bool HostMatches(Net n,Uri? u)=>u!=null&&n.Hosts.Any(h=>u.Host.Equals(h,StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
    static bool OnService(Net n,string? source)=>Uri.TryCreate(source,UriKind.Absolute,out var u)&&HostMatches(n,u);
    static bool SamePath(string? source,string target)=>Uri.TryCreate(source,UriKind.Absolute,out var a)&&Uri.TryCreate(target,UriKind.Absolute,out var b)&&a.Host==b.Host&&a.AbsolutePath.TrimEnd('/')==b.AbsolutePath.TrimEnd('/');
    bool OnLoginPage(Net n)=>Uri.TryCreate(n.Wv?.Source,UriKind.Absolute,out var u)&&HostMatches(n,u)&&UrlSaysConnected(n,u)==false;

    // true/false si l'adresse suffit à conclure, null sinon (autre domaine, réseau décidé par script...).
    static bool? UrlSaysConnected(Net n,Uri u){
        if(n.Id=="gmail"&&u.Host.Equals("accounts.google.com",StringComparison.OrdinalIgnoreCase))return false;
        if(!HostMatches(n,u))return null;
        string path=u.AbsolutePath.ToLowerInvariant();
        switch(n.Id){
            case "discord":
                if(path.StartsWith("/login")||path.StartsWith("/register"))return false;
                return path.StartsWith("/channels")||path.StartsWith("/app")?true:null;
            case "instagram":
                if(path.StartsWith("/accounts/login")||path.StartsWith("/accounts/emailsignup")||path.StartsWith("/accounts/password"))return false;
                return true;
            case "snapchat":
                return u.Host.Equals("accounts.snapchat.com",StringComparison.OrdinalIgnoreCase)?false:true;
            case "gmail":return true;
            default:return null;
        }
    }

    void EvaluateUrl(Net n){
        if(n.Wv==null||!Uri.TryCreate(n.Wv.Source,UriKind.Absolute,out var u))return;
        var verdict=UrlSaysConnected(n,u);
        if(verdict.HasValue)SetConnected(n,verdict.Value);
    }

    void SetConnected(Net n,bool value){
        if(n.Connected==value)return;
        n.Connected=value;Store.State.SocialConnected[n.Id]=value;SavePrefsSoon();if(value)MaybeArm(n);Changed();
    }

    // Les préférences ne s'écrivent que sur le dispatcher (Store.Save n'est pas thread-safe), regroupées.
    void SavePrefsSoon(){
        if(prefsSaveQueued||disposed)return;prefsSaveQueued=true;
        dispatcher.BeginInvoke((Action)(()=>{prefsSaveQueued=false;if(!disposed)Store.Save();}),DispatcherPriority.Background);
    }

    async Task CheckLater(Net n,int delayMs){
        try{
            n.NextCheck=DateTime.UtcNow.AddSeconds(30);
            await Task.Delay(delayMs);
            if(disposed||n.Wv==null||n.Suspended)return;
            if(!n.Script&&n.Id!="instagram")return;
            if(n.Id=="instagram"&&!n.Connected)return;
            string script=n.Id switch{
                "whatsapp"=>"(()=>{if(document.querySelector('#pane-side,[data-testid=\"chat-list\"]'))return 1;if(document.querySelector('[data-ref],[data-testid=\"qrcode\"],canvas[aria-label]'))return 0;return -1;})()",
                "telegram"=>"(()=>{if(document.querySelector('.chatlist-container,.chatlist'))return 1;if(document.querySelector('.page-signQR,.page-sign,#auth-pages .tabs-container'))return 0;return -1;})()",
                _=>"(()=>document.querySelector('input[name=\"password\"]')?0:1)()"
            };
            string result=await n.Wv.ExecuteScriptAsync(script);
            if(disposed||n.Wv==null)return;
            if(result=="1")SetConnected(n,true);else if(result=="0")SetConnected(n,false);
        }catch(Exception ex){Store.Log("Social détection "+n.Id+": "+ex.Message);}
    }

    // Une fois toutes les 30 s, et seulement tant qu'une vue existe : réseaux à script non connectés, puis mémoire libre.
    void SlowTick(object? sender,EventArgs e){
        if(disposed)return;
        foreach(var n in nets)if(n.Script&&!n.Connected&&n.Wv!=null&&!n.Suspended&&!n.Navigating)_=CheckLater(n,0);
        try{
            var status=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
            if(!GlobalMemoryStatusEx(ref status))return;
            if(!lowMemory&&status.AvailPhys<LowMemory){
                lowMemory=true;
                foreach(var n in nets)if(n.Wv!=null&&n.Win is {IsVisible:false}&&!n.Suspended)_=SuspendAsync(n);
            }else if(lowMemory&&status.AvailPhys>RecoveredMemory){
                lowMemory=false;
                foreach(var n in nets)if(n.Wv!=null&&n.Suspended){try{n.Wv.Resume();}catch{}n.Suspended=false;}
            }
        }catch(Exception ex){Store.Log("Social mémoire: "+ex.Message);}
    }

    async Task SuspendAsync(Net n){
        try{
            var wv=n.Wv;if(wv==null)return;
            if(await wv.TrySuspendAsync()&&ReferenceEquals(n.Wv,wv))n.Suspended=true;
        }catch(Exception ex){Store.Log("Social veille "+n.Id+": "+ex.Message);}
    }

    // ---- Persistance des compteurs (session Windows) ----------------------------------------------------------
    string CountsFile=>Path.Combine(folder,"counts.json");

    sealed class CountsFile_{public long BootTicks{get;set;}public Dictionary<string,int> Counts{get;set;}=new();public List<string>? TitleNets{get;set;}public List<HistoryItem> History{get;set;}=new();public int NextSeq{get;set;}}

    CountsFile_ MakeFile()=>new(){NextSeq=nextSeq,BootTicks=bootUtc.Ticks,Counts=nets.ToDictionary(n=>n.Id,n=>n.Count),TitleNets=nets.Where(n=>n.U.TitleSeen).Select(n=>n.Id).ToList(),History=history.Select(h=>new HistoryItem{Seq=h.Seq,Net=h.Net,Title=h.Title,Body=h.Body,Kind=h.Kind,At=h.At}).ToList()};

    void LoadCounts(){
        try{
            if(!File.Exists(CountsFile))return;
            var data=JsonSerializer.Deserialize<CountsFile_>(File.ReadAllText(CountsFile));
            if(data==null)return;
            // Même session Windows = même heure de démarrage, à quelques secondes d'écart près.
            if(Math.Abs((new DateTime(data.BootTicks,DateTimeKind.Utc)-bootUtc).TotalSeconds)>90)return;
            // Migration : ancien fichier (sans TitleNets) = compteurs cumulés non fiables, plafonnés à 99 ; l'absorption/le premier titre les remplace.
            foreach(var n in nets)if(data.Counts.TryGetValue(n.Id,out int c)){
                n.Count=Math.Clamp(c,0,999999);
                if(data.TitleNets==null){n.Count=Math.Min(n.Count,99);n.U.Legacy=true;}
                else if(data.TitleNets.Contains(n.Id)){n.U.TitleSeen=true;n.Count=Math.Min(n.Count,99);}
            }
            if(data.History!=null){history.AddRange(data.History.Where(h=>h!=null&&nets.Any(x=>x.Id==h.Net)).Select(h=>new HistoryItem{Seq=h.Seq,Net=h.Net,Title=Clip(h.Title,120),Body=Clip(h.Body,140),Kind=h.Kind=="count"?"count":"notif",At=h.At}).OrderByDescending(h=>h.Seq).Take(100));nextSeq=Math.Max(Math.Max(1,data.NextSeq),history.Select(h=>h.Seq).DefaultIfEmpty(0).Max()+1);}
            Store.Log("Social historique chargé : "+history.Count+" entrée(s), prochain seq "+nextSeq+(data.History==null?" (fichier sans historique)":""));
        }catch(Exception ex){Store.Log("Social compteurs: "+ex.Message);}
    }

    // Écriture différée (~2 s) hors thread UI.
    void QueueSave(){
        if(saveQueued||disposed)return;saveQueued=true;
        _=Task.Run(async()=>{try{await Task.Delay(2000);if(disposed)return;dispatcher.Invoke(()=>saveQueued=false);WriteNow();}catch{}});
    }

    void WriteNow(){
        CountsFile_? snapshot=null;
        dispatcher.Invoke(()=>{if(countsDirty){snapshot=MakeFile();countsDirty=false;}});
        lock(saveLock){
            if(disposed)return; // Dispose écrit l'état final sous ce verrou : une écriture différée plus ancienne ne doit pas l'écraser
            try{
                if(snapshot!=null){
                    Directory.CreateDirectory(folder);
                    string temp=CountsFile+".tmp";
                    File.WriteAllText(temp,JsonSerializer.Serialize(snapshot));
                    File.Move(temp,CountsFile,true);
                }
            }catch(Exception ex){Store.Log("Social écriture: "+ex.Message);}
        }
    }

    public void Dispose(){
        if(disposed)return;disposed=true;
        notifyTimer.Stop();slowTimer.Stop();
        foreach(var n in nets)CloseView(n);
        lock(saveLock){
            try{
                Directory.CreateDirectory(folder);
                File.WriteAllText(CountsFile,JsonSerializer.Serialize(MakeFile()));
            }catch{}
        }
        environment=null;
    }
}

// Machine d'état pure du compteur (sans WebView2 ni horloge système : le temps est passé en paramètre) ; testée par --verify-social-count.
internal sealed class UnreadCounter {
    internal readonly record struct TitleResult(bool Changed,int Rise,int ZeroToken,bool AfterNotif=false);
    public int Count,Title,Credit;public bool TitleSeen,Legacy;
    int zeroToken;DateTime riseAt=DateTime.MinValue,notifAt=DateTime.MinValue;

    // Titre lu. Rise = hausse réelle par rapport au compteur affiché (son + historique). ZeroToken>0 = armer le délai de 5 s.
    public TitleResult OnTitle(int unread,DateTime now){
        Title=unread;zeroToken++;
        if(unread==0)return new(false,0,TitleSeen?zeroToken:0);
        TitleSeen=true;Legacy=false;
        int before=Count;Count=unread;
        int rise=Math.Max(0,unread-before);
        // Notification arrivée avant la hausse (<=3 s) : déjà chimée + historisée, la hausse ne doit pas doubler (AfterNotif).
        bool after=rise>0&&(now-notifAt).TotalSeconds is>=0 and<=3;
        if(rise>0){riseAt=now;Credit=after?Math.Max(0,rise-1):rise;}
        return new(unread!=before,rise,0,after);
    }
    public bool ZeroExpired(int token){
        if(token!=zeroToken||Title!=0||!TitleSeen||Count==0)return false;
        Count=0;return true;
    }
    public void CancelZero()=>zeroToken++;
    // Une notification arrivée dans les 3 s d'une hausse du titre consomme un crédit (déjà comptée par le titre).
    public void NoteNotification(DateTime now)=>notifAt=now;
    // Ouverture de la page : un réseau sans compteur de titre n'a pas d'autre moyen de revenir à 0.
    public bool ResetOnOpen(){if(TitleSeen||Count==0)return false;Count=0;Credit=0;return true;}
    public bool ConsumeCredit(DateTime now){
        if((now-riseAt).TotalSeconds<=3&&Credit>0){Credit--;return true;}
        return false;
    }
    // Absorption initiale silencieuse : le titre affiché devient le compteur (ancien fichier : même un titre à 0 remplace).
    public bool Absorb(){
        if(Title>0)TitleSeen=true;
        if(!TitleSeen)return false; // ancien fichier : un compteur de notifications valide n'est remplacé que si le réseau affiche un titre
        Legacy=false;bool changed=Count!=Title;Count=Title;return changed;
    }
    public void Reset(){Count=0;Title=0;Credit=0;TitleSeen=false;Legacy=false;zeroToken++;}
}
