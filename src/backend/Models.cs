// language: C# 12, file: Models.cs, target: Windows 11 / .NET 8, persistent local workspace
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PanneauxGlass;
public sealed class Entry {
    public string Id {get;set;}="";public string Name {get;set;}="";public string Target {get;set;}="";
    public string Section {get;set;}="";public string Category {get;set;}="";public string IconPath {get;set;}="";
}
public sealed class Catalogue {public List<Entry> Entries {get;set;}=new();public string GeneratedAt {get;set;}="";public string[] Diagnostics {get;set;}=Array.Empty<string>();}
public sealed class BoardState {
    public bool FavoritesOnly {get;set;} public PanelPlacement? Placement {get;set;}
    public string Id {get;set;}="";public string Title {get;set;}="";public string Accent {get;set;}="#7ADCF4";public string Filter {get;set;}="Tous";
}
public sealed class WebShortcut {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Name {get;set;}="";
    public string Url {get;set;}="";public string IconPath {get;set;}="";public bool Fallback {get;set;}
    public string SourceImagePath {get;set;}="";public LogoCrop? Crop {get;set;}public bool ShowGlass {get;set;}=true;
}
public sealed class LogoCrop {public double Zoom {get;set;}=1;public double X {get;set;}public double Y {get;set;}public bool Centered {get;set;}=true;}
public sealed class WorkspaceProfile {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Name {get;set;}="";
    public List<string> Targets {get;set;}=new();public Dictionary<string,PanelPlacement?> Layout {get;set;}=new();
}
public sealed class ClipboardItem {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Kind {get;set;}="text";public string Text {get;set;}="";
    public string ImagePath {get;set;}="";public bool Pinned {get;set;}public string At {get;set;}="";
}
public sealed class LauncherTool {public string Id {get;set;}="";public string Kind {get;set;}="";public string Name {get;set;}="";}
// A sheet taped to the screen: a captured image or a handwritten memo. Everything that makes it
// feel placed — text, width, position, transparency, angle — is kept here and restored at startup.
public sealed class PinNote {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Kind {get;set;}="memo";
    public string Text {get;set;}="";public string ImagePath {get;set;}="";
    public double? Left {get;set;}public double? Top {get;set;}
    public double Width {get;set;}=300;public double Height {get;set;}=0;
    public double Opacity {get;set;}=1;public double Angle {get;set;}=-2.4;
    public string Created {get;set;}=DateTime.Now.ToString("O");
}
public sealed class Preferences {
    public Dictionary<string,string> CustomPalette {get;set;}=new(){{"background","#fff8f6"},{"text","#261916"},{"primary","#895143"},{"secondary","#775b52"},{"tertiary","#68613c"}};
    public bool CustomGlass {get;set;}=false;public double CustomGlassOpacity {get;set;}=.12;
    public bool AccentBorders {get;set;}=true;
    public bool ExplorerTheme {get;set;}=false;public List<string> ExplorerFolders {get;set;}=new();
    public bool FilesDefaultHandler {get;set;}=true;   // default on (one-time migration FilesDefaultsMigrated for existing installs): HKCU Directory/Drive shell handler -> Winlestia Fichiers (see FilesHandler.cs)
    public bool FilesDefaultsMigrated {get;set;}   // set once the default-on / default-pinned migration ran; a later opt-out is never undone
    public bool FilesPinRemoved {get;set;}   // set once the old "Winlestia Fichiers" dock pin was removed (the app has its own dock button now); see FilesPinMigration
    public bool FilesPreload {get;set;}=true;   // default on: keep a hidden Winlestia Fichiers resident so folders open instantly
    public bool AutoTile {get;set;}=false;public bool DesktopLogo {get;set;}=false;
    public bool? ReducedEffects {get;set;}
    public int TopActivationMargin {get;set;}=2; public double DockWidth {get;set;}=0;
    public int ShellRevision {get;set;}
    public bool ReplaceTaskbar {get;set;}=true;
    public string RailSide {get;set;}="left";
    public string PreviousTheme {get;set;}="";
    public int FrameThickness {get;set;}=16;public double GlassOpacity {get;set;}=.12;public bool AutoTheme {get;set;}=false;public bool ScreenFrame {get;set;}=true;
    public List<string> TaskbarPins {get;set;}=new();
    public ShortcutPreferences Shortcut {get;set;}=new();
    public ShortcutPreferences PaletteShortcut {get;set;}=new(){Modifiers=2,Key=0x71};
    public ControlPreferences Controls {get;set;}=new();
    public string HomeScope {get;set;}="Tous";public Dictionary<string,int> LaunchCounts {get;set;}=new();
    public double Tint {get;set;}=.20;public double Reflections {get;set;}=.80;public double IconSize {get;set;}=56;
    public double LeftRatio {get;set;}=.40;public double TopRatio {get;set;}=.50;public double Gap {get;set;}=24;public double Margin {get;set;}=52;
    public double Motion {get;set;}=1;public double RailWidth {get;set;}=62;public double RailHeight {get;set;}=.87;
    public bool AutoHide {get;set;}=true;public bool PauseFullscreen {get;set;}=true;public bool LightGlass {get;set;}=false;public string Theme {get;set;}="nuit";
    public int MotionLevel {get;set;}=1;public bool ClipboardHistory {get;set;}=true;public bool ClipboardImages {get;set;}=true;public int ClipboardLimit {get;set;}=30;
    public List<WorkspaceProfile> Workspaces {get;set;}=new();public List<string> ColorHistory {get;set;}=new();public bool TourDone {get;set;}
    public List<PinNote> Pins {get;set;}=new();
    public ShortcutPreferences PinShortcut {get;set;}=new(){Modifiers=2,Key=0x77};
    public ShortcutPreferences CaptureShortcut {get;set;}=new(){Modifiers=2,Key=0x72};
    public string Scratch {get;set;}="";
    public bool SocialMuted {get;set;}=false;public bool SocialSound {get;set;}=true;public int SocialVolume {get;set;}=60;
    public string BookmarksBrowser {get;set;}="";public Dictionary<string,List<string>> BookmarksHidden {get;set;}=new();
    public Dictionary<string,bool> SocialEnabled {get;set;}=new();public Dictionary<string,bool> SocialNetMuted {get;set;}=new();public Dictionary<string,bool> SocialConnected {get;set;}=new();
    public List<string> Favorites {get;set;}=new();public List<string> Recent {get;set;}=new();public List<string> Hidden {get;set;}=new();
    public List<string> Order {get;set;}=new();public List<Entry> Custom {get;set;}=new();
    public List<WebShortcut> Websites {get;set;}=new();public List<LauncherTool> LauncherTools {get;set;}=new();
    public List<WindowsShortcut> QuickTabs {get;set;}=DefaultQuickTabs();
    public static List<WindowsShortcut> DefaultQuickTabs()=>new(){
        new(){Id="tab-apps",Name="Applications",Target="glass:apps",Glyph="\uE71D"},new(){Id="tab-games",Name="Jeux",Target="glass:games",Glyph="\uE7FC"},
        new(){Id="tab-projects",Name="Projets",Target="glass:projects",Glyph="\uE943"},new(){Id="tab-documents",Name="Documents",Target="glass:documents",Glyph="\uE8B7"},
        new(){Id="tab-note",Name="Note",Target="glass:note",Glyph="\uE70B"},new(){Id="tab-focus",Name="Minuteur",Target="glass:focus",Glyph="\uE917"},
        new(){Id="tab-tools",Name="Atelier",Target="glass:tools",Glyph="\uE74C"},
        new(){Id="tab-capture",Name="Capture",Target="glass:capture",Glyph="\uE722"}
    };
    public Dictionary<string,string> BoardOverrides {get;set;}=new();public Dictionary<string,string> Names {get;set;}=new();
    public Dictionary<string,string> Categories {get;set;}=new();public Dictionary<string,string> Icons {get;set;}=new();
    public List<BoardState> Boards {get;set;}=Defaults();
    public static List<BoardState> Defaults()=>new(){new(){Id="apps",Title="Applications",Accent="#91DFF5",Filter="Sélection"},new(){Id="games",Title="Jeux",Accent="#ADA7FF"},new(){Id="projects",Title="Projets",Accent="#8BE6C5"},new(){Id="documents",Title="Documents & médias",Accent="#F0D195"}};
    public void Normalize(){FrameThickness=Math.Clamp(FrameThickness,4,40);GlassOpacity=double.IsFinite(GlassOpacity)?Math.Clamp(GlassOpacity,.02,.95):.12;Tint=double.IsFinite(Tint)?Math.Clamp(Tint,.05,.75):.2;Reflections=double.IsFinite(Reflections)?Math.Clamp(Reflections,0,1):.8;IconSize=double.IsFinite(IconSize)?Math.Clamp(IconSize,40,80):56;LeftRatio=Math.Clamp(LeftRatio,.3,.7);TopRatio=Math.Clamp(TopRatio,.3,.7);Gap=Math.Clamp(Gap,12,48);Margin=Math.Clamp(Margin,20,100);Motion=Math.Clamp(Motion,0,1.5);RailWidth=Math.Clamp(RailWidth,40,90);RailHeight=Math.Clamp(RailHeight,.45,.94);Controls??=new();Controls.Normalize();Shortcut??=new();Shortcut.Normalize();LaunchCounts??=new();if(HomeScope!="Tous"&&!new[]{"apps","games","projects","documents"}.Contains(HomeScope))HomeScope="Tous";Scratch??="";if(Scratch.Length>20000)Scratch=Scratch[..20000];if(Theme!="custom"&&PanneauxGlass.Theme.Find(Theme)==null)Theme="nuit";CustomGlassOpacity=double.IsFinite(CustomGlassOpacity)?Math.Clamp(CustomGlassOpacity,.02,.95):.12;CustomPalette??=new();Websites??=new();LauncherTools??=new();
        MotionLevel=Math.Clamp(MotionLevel,0,2);ClipboardLimit=Math.Clamp(ClipboardLimit,8,80);Workspaces??=new();Workspaces=Workspaces.Where(w=>w!=null&&!string.IsNullOrWhiteSpace(w.Name)).Take(24).ToList();foreach(var workspace in Workspaces){workspace.Targets??=new();workspace.Layout??=new();workspace.Targets=workspace.Targets.Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToList();}        ColorHistory??=new();ColorHistory=ColorHistory.Where(c=>!string.IsNullOrWhiteSpace(c)).Distinct().Take(24).ToList();
        Pins??=new();Pins=Pins.Where(p=>p!=null&&(p.Kind=="memo"||!string.IsNullOrWhiteSpace(p.ImagePath))).Take(24).ToList();
        foreach(var pin in Pins){if(string.IsNullOrWhiteSpace(pin.Id))pin.Id=Guid.NewGuid().ToString("N");if(pin.Kind!="image")pin.Kind="memo";
            pin.Text??="";if(pin.Text.Length>4000)pin.Text=pin.Text[..4000];pin.ImagePath??="";
            pin.Width=double.IsFinite(pin.Width)?Math.Clamp(pin.Width,160,1400):300;pin.Height=double.IsFinite(pin.Height)?Math.Clamp(pin.Height,0,1600):0;
            pin.Opacity=double.IsFinite(pin.Opacity)?Math.Clamp(pin.Opacity,.35,1):1;pin.Angle=double.IsFinite(pin.Angle)?Math.Clamp(pin.Angle,-12,12):-2.4;
            if(pin.Left is double left&&!double.IsFinite(left))pin.Left=null;if(pin.Top is double top&&!double.IsFinite(top))pin.Top=null;}
        PinShortcut??=new(){Modifiers=2,Key=0x77};PinShortcut.Normalize();
        CaptureShortcut??=new(){Modifiers=2,Key=0x72};CaptureShortcut.Normalize();
        PaletteShortcut??=new(){Modifiers=2,Key=0x71};PaletteShortcut.Normalize();QuickTabs??=DefaultQuickTabs();
        QuickTabs=QuickTabs.Where(t=>t!=null&&!string.IsNullOrWhiteSpace(t.Target)).Take(12).ToList();foreach(var tab in QuickTabs){if(string.IsNullOrWhiteSpace(tab.Id))tab.Id=Guid.NewGuid().ToString("N");if(string.IsNullOrWhiteSpace(tab.Name))tab.Name=tab.Target;if(string.IsNullOrWhiteSpace(tab.Glyph))tab.Glyph="\uE713";tab.Arguments??="";}Favorites??=new();Recent??=new();Hidden??=new();Order??=new();Custom??=new();BoardOverrides??=new();Names??=new();Categories??=new();Icons??=new();if(Boards==null||Boards.Count!=4||!Boards.Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(new[]{"apps","documents","games","projects"}))Boards=Defaults();foreach(var b in Boards){if(string.IsNullOrWhiteSpace(b.Title))b.Title=Defaults().First(x=>x.Id==b.Id).Title;try{_ =System.Windows.Media.ColorConverter.ConvertFromString(b.Accent);}catch{b.Accent="#91DFF5";}b.Filter??="Tous";if(b.Placement?.Valid==false)b.Placement=null;}}
}
public static class Store {
    public static string Root=AppDomain.CurrentDomain.BaseDirectory;
    public static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    public static Preferences State=new();public static List<Entry> Entries=new();
    public static string Fold(string? value){var b=new StringBuilder();foreach(char c in (value??"").ToLowerInvariant().Normalize(NormalizationForm.FormD))if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)b.Append(c);return b.ToString();}
    public static void Log(string text){try{File.AppendAllText(Path.Combine(Root,"glass.log"),DateTimeOffset.Now.ToString("O")+" "+text+Environment.NewLine,Encoding.UTF8);}catch{}}
    public static T Read<T>(string path) where T:new(){if(!File.Exists(path))return new T();try{return JsonSerializer.Deserialize<T>(File.ReadAllText(path,Encoding.UTF8),Json)??new T();}catch(Exception ex){Log(Path.GetFileName(path)+": "+ex.Message);return new T();}}
    public static void Save(){try{string path=Path.Combine(Root,"preferences-glass.json"),temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(State,Json),Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}catch(Exception ex){Log("Enregistrement: "+ex.Message);}}
    public static void Load(){State=Read<Preferences>(Path.Combine(Root,"preferences-glass.json"));if(!File.Exists(Path.Combine(Root,"preferences-glass.json"))){string old=Path.GetFullPath(Path.Combine(Root,"..","Workspace-v4","preferences.json"));if(File.Exists(old)){try{using var doc=JsonDocument.Parse(File.ReadAllText(old));if(doc.RootElement.TryGetProperty("Favorites",out var f))State.Favorites=f.Deserialize<List<string>>()??new();}catch{}}}State.Normalize();LoadCatalogue();}
    public static void LoadCatalogue(){Entries=Read<Catalogue>(Path.Combine(Root,"catalog.json")).Entries;foreach(var e in State.Custom)if(!Entries.Any(x=>x.Id==e.Id))Entries.Add(e);bool healed=false;foreach(var e in Entries){if(HealDockTarget(e))healed=true;RepairMovedTarget(e);if(State.Names.TryGetValue(e.Id,out var name))e.Name=name;if(State.Categories.TryGetValue(e.Id,out var cat))e.Category=cat;if(State.Icons.TryGetValue(e.Id,out var icon))e.IconPath=icon;}if(healed)Save();}
    // Self-heal for "dock:" entries whose exe vanished (Squirrel apps move app-1.0.9265 -> app-1.0.9266). The id is kept (favourites/pins reference it); only Target/IconPath change.
    // Returns true only when the entry was healed AND belongs to State.Custom (the persisted store): catalog.json entries are healed in memory only, so no prefs rewrite per start.
    static bool HealDockTarget(Entry entry){if(!entry.Id.StartsWith("dock:",StringComparison.Ordinal)||!SafeToProbe(entry.Target)||File.Exists(entry.Target)||Directory.Exists(entry.Target))return false;string? fixedPath=HealStaleExe(entry.Target);if(fixedPath==null)return false;if(string.IsNullOrEmpty(entry.IconPath)||entry.IconPath.Equals(entry.Target,StringComparison.OrdinalIgnoreCase)||(SafeToProbe(entry.IconPath)&&!File.Exists(entry.IconPath)))entry.IconPath=fixedPath;entry.Target=fixedPath;return State.Custom.Any(c=>ReferenceEquals(c,entry));}
    // Never touch UNC paths or drives that are missing / not ready (a File.Exists on those can stall the UI thread).
    static bool SafeToProbe(string? path){try{if(string.IsNullOrWhiteSpace(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||!Path.IsPathRooted(path))return false;string? root=Path.GetPathRoot(path);if(string.IsNullOrEmpty(root)||root.StartsWith(@"\\",StringComparison.Ordinal))return false;var drive=new DriveInfo(root);return drive.IsReady&&Directory.Exists(root);}catch{return false;}}
    // Squirrel only: root/app-X/name.exe -> newest sibling root/app-Y/name.exe. Numeric version order, "-suffix" stripped; on a tie the build without suffix wins.
    public static string? HealStaleExe(string path){try{if(!SafeToProbe(path))return null;string name=Path.GetFileName(path);string? parentDir=Path.GetDirectoryName(path);string? root=parentDir==null?null:Path.GetDirectoryName(parentDir);if(string.IsNullOrEmpty(name)||root==null||!Path.GetFileName(parentDir)!.StartsWith("app-",StringComparison.OrdinalIgnoreCase)||!Directory.Exists(root))return null;string? best=null;Version? bestVersion=null;bool bestSuffix=false;foreach(string dir in Directory.GetDirectories(root,"app-*")){if(!File.Exists(Path.Combine(dir,name)))continue;string tail=Path.GetFileName(dir)[4..];int dash=tail.IndexOf('-');bool suffix=dash>=0;Version.TryParse(suffix?tail[..dash]:tail,out var version);int cmp=best==null?1:version==null?(bestVersion==null?0:-1):bestVersion==null?1:version.CompareTo(bestVersion);if(cmp>0||(cmp==0&&bestSuffix&&!suffix)){best=dir;bestVersion=version;bestSuffix=suffix;}}return best==null?null:Path.Combine(best,name);}catch{}return null;}
    static void RepairMovedTarget(Entry entry){if(File.Exists(entry.Target)||Directory.Exists(entry.Target))return;string documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);foreach(string folder in new[]{"02_Logiciels_et_Outils","04_Documents_Administratifs"}){string prefix=Path.Combine(documents,folder);if(!entry.Target.Equals(prefix,StringComparison.OrdinalIgnoreCase)&&!entry.Target.StartsWith(prefix+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;string replacement=Path.Combine(desktop,folder)+entry.Target[prefix.Length..];if(!Directory.Exists(replacement)&&!File.Exists(replacement))continue;if(entry.IconPath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))entry.IconPath=Path.Combine(desktop,folder)+entry.IconPath[prefix.Length..];entry.Target=replacement;}}
    public static string BoardFor(Entry e){if(State.BoardOverrides.TryGetValue(e.Id,out var board))return board;return e.Section switch{"Applications"=>"apps","Jeux"=>"games","Projets"=>"projects",_=>"documents"};}
    internal static IEnumerable<Entry> Visible(string scope="Tous",string query=""){var words=Fold(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);return Entries.Where(e=>!State.Hidden.Contains(e.Id)&&(scope=="Tous"||BoardFor(e)==scope)&&words.All(w=>Fold(e.Name+" "+e.Category+" "+e.Target).Contains(w)));}
    internal static IEnumerable<Entry> HomeFavorites(string scope="Tous",string query="")=>Visible(scope,query).Where(e=>State.Favorites.Contains(e.Id)).OrderBy(e=>{int i=State.Recent.IndexOf(e.Id);return i<0?int.MaxValue:i;}).ThenBy(e=>{int i=State.Favorites.IndexOf(e.Id);return i<0?int.MaxValue:i;});
    internal static IEnumerable<Entry> RecentEntries(string scope="Tous",string query="")=>Visible(scope,query).Where(e=>State.Recent.Contains(e.Id)).OrderBy(e=>State.Recent.IndexOf(e.Id));
    internal static void RecordLaunch(string id){State.Recent.RemoveAll(x=>x==id);State.Recent.Insert(0,id);State.Recent=State.Recent.Take(48).ToList();State.LaunchCounts.TryGetValue(id,out int count);State.LaunchCounts[id]=count==int.MaxValue?count:count+1;}
    public static IEnumerable<Entry> Sorted(IEnumerable<Entry> source)=>source.OrderBy(e=>{int n=State.Order.IndexOf(e.Id);return n<0?100000:n;}).ThenByDescending(e=>State.Favorites.Contains(e.Id)).ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase);
}
