// C#12 / ExplorerAppearance.cs / Windows11: reversible native folder icons and DWM colours, no shell injection.
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text.Json;using System.Security.Cryptography;using System.Windows.Threading;using D=System.Drawing;using G=System.Drawing.Drawing2D;
namespace PanneauxGlass;
internal sealed partial class ExplorerAppearance:IDisposable{
 internal sealed class Backup{public string Folder{get;set;}="";public string? Ini{get;set;}public int Attributes{get;set;}public int IniAttributes{get;set;}public string? AppliedHash{get;set;}public DateTime? OriginalWriteUtc{get;set;}public bool DateRepaired{get;set;}}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Settings{public uint Size,Mask;public IntPtr View,Template;public uint TemplateLength;public IntPtr Version,Tip;public uint TipLength;public IntPtr Class;public uint Flags;[MarshalAs(UnmanagedType.LPWStr)]public string Icon;public uint IconLength;public int Index;public IntPtr Logo;public uint LogoLength;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetSetFolderCustomSettings(ref Settings settings,string path,uint flags);
 [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]static extern bool PathMakeSystemFolderW(string path);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern void SHChangeNotify(uint kind,uint flags,string? first,IntPtr second);
 [DllImport("shell32.dll")]static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref uint value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out uint value,int size);
 readonly object gate=new();readonly string root;readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 internal uint ExcludedProcess;
 internal string Status {get;private set;}="Personnalisation inactive.";
 readonly Dictionary<IntPtr,uint[]> frames=new();readonly Dictionary<IntPtr,uint> frameOwners=new();readonly HashSet<IntPtr> captionFrames=new();
 readonly Dictionary<string,string> scanned=new(StringComparer.OrdinalIgnoreCase);bool scanning;DateTime scanAt;
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr Style(IntPtr window,int index);
 static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
 // Event-driven rescans: a window set/title change (seen by FrameTick, no I/O) or a sub-folder created/renamed/deleted in a visible folder (FileSystemWatcher) arms ONE scan 1.5 s later.
 readonly Dictionary<string,FileSystemWatcher> watchers=new(StringComparer.OrdinalIgnoreCase);string windowSignature="";DateTime scanDue=DateTime.MaxValue;
 internal void RequestScan(){scanDue=DateTime.UtcNow.AddSeconds(1.5);}
 void Watch(HashSet<string> visible){lock(watchers){foreach(var gone in watchers.Keys.Where(k=>!visible.Contains(k)).ToArray()){watchers[gone].Dispose();watchers.Remove(gone);}
  foreach(string path in visible){if(watchers.ContainsKey(path)||disposed||!Directory.Exists(path)||path.StartsWith(@"\\"))continue;try{var w=new FileSystemWatcher(path){NotifyFilter=NotifyFilters.DirectoryName,IncludeSubdirectories=false,EnableRaisingEvents=true};FileSystemEventHandler h=(_,_)=>RequestScan();w.Created+=h;w.Deleted+=h;w.Renamed+=(_,_)=>RequestScan();watchers[path]=w;}catch{}}}}
 // Per-folder notification debounce: at most one shell notification per folder per 10 s. Returns whether it was sent.
 readonly Dictionary<string,DateTime> notifiedAt=new(StringComparer.OrdinalIgnoreCase);internal int NotifyCount;
 internal bool Notify(string folder,DateTime? now=null){var t=now??DateTime.UtcNow;lock(notifiedAt){if(notifiedAt.TryGetValue(folder,out var last)&&(t-last).TotalSeconds<10)return false;notifiedAt[folder]=t;if(notifiedAt.Count>4096)notifiedAt.Clear();}NotifyCount++;SHChangeNotify(0x2000,5|0x3000,folder,IntPtr.Zero);return true;}
 // True when the folder's desktop.ini is exactly what we last wrote (hash) and already points at this (stable) icon: nothing to write. An ini that still points at one of our legacy <hex>-<kind>-v2.ico files is "ours" but returns false, so it is migrated (rewritten) by the next visit or by MigrateAsync.
 internal bool AlreadyThemed(string folder,string icon,List<Backup> data){try{var saved=data.FirstOrDefault(p=>p.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase));if(saved?.AppliedHash==null)return false;string ini=Path.Combine(folder,"desktop.ini");return File.Exists(ini)&&Hash(ini)==saved.AppliedHash&&File.ReadAllText(ini).Contains(icon,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 void ScanVisibleFolders(){if(scanning||DateTime.UtcNow<scanAt||!Store.State.ExplorerTheme)return;scanning=true;scanAt=DateTime.UtcNow.AddSeconds(4);var visible=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  _=System.Threading.Tasks.Task.Run(()=>{object? shell=null,windows=null;try{var type=Type.GetTypeFromProgID("Shell.Application");if(type==null)return;shell=Activator.CreateInstance(type);windows=((dynamic)shell!).Windows();int count=Math.Min(32,(int)((dynamic)windows).Count);var budget=new[]{1024};
   for(int i=0;i<count&&!disposed;i++){object? window=null,document=null,folder=null,self=null;try{window=((dynamic)windows).Item(i);if(window==null)continue;document=((dynamic)window).Document;folder=((dynamic)document).Folder;self=((dynamic)folder).Self;string path=(string)((dynamic)self).Path;if(path.StartsWith("::{"))continue;visible.Add(path);ColourVisibleTree(path,budget);}catch(Exception ex){Store.Log("Dossiers visibles: "+ex.Message);}finally{Release(self);Release(folder);Release(document);Release(window);}}
  }catch(Exception ex){Store.Log("Explorateur visible: "+ex.Message);}finally{Release(windows);Release(shell);try{Watch(visible);}catch{}scanning=false;}});
 }
 // Background-thread safe (the Files app will call it on navigation): per-folder 3 s debounce and a global budget live in ColourVisibleFolder (ExplorerAppearance.StableIcons.cs).
 static bool Protected(string folder){string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);var roots=new[]{windows,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}.Where(p=>!string.IsNullOrEmpty(p));return folder.Equals(Path.GetPathRoot(folder),StringComparison.OrdinalIgnoreCase)||roots.Any(p=>folder.Equals(p,StringComparison.OrdinalIgnoreCase)||folder.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));}
 // Themes the open folder (when under a user folder) and its sub-folders to depth 3; max 256 themed / 1024 examined per location, shared budget per pass.
 internal void ColourVisibleTree(string parent,int[] budget){if(!Directory.Exists(parent)||parent.StartsWith(@"\\"))return;lock(gate){if(disposed||!Store.State.ExplorerTheme)return;string full=Path.GetFullPath(parent);if(!Within(full,false)||Skipped(full))return;string icon=EnsureIcon("folder");
  var data=Read();var pending=new List<(string folder,Backup saved,string icon)>();bool flushed=false;var parents=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int applied=0,seen=0;bool stop=false;
  // Junctions / symlinks (ReparsePoint) are skipped on purpose: their target folder is themed when it is visited itself.
  bool Theme(string folder,string owner){try{var info=new DirectoryInfo(folder);if(disposed||Skipped(folder)||info.Name.StartsWith(".")||(info.Attributes&(FileAttributes.Hidden|FileAttributes.System|FileAttributes.ReparsePoint))!=0||Protected(folder))return false;if(scanned.TryGetValue(folder,out string? previous)&&previous==Scheme)return true;if(applied>=256||budget[0]<=0){stop=true;return false;}budget[0]--;if(AlreadyThemed(folder,icon,data)){scanned[folder]=Scheme;return true;}scanned[folder]=Scheme;var prep=Prepare(folder,data);pending.Add((prep.folder,prep.saved,icon));applied++;if(pending.Count>=32)Flush();parents.Add(owner);return true;}catch(Exception ex){Store.Log("Icône dossier ignorée: "+folder+": "+ex.Message);return true;}}
  // Backups of a chunk are persisted BEFORE that chunk's desktop.ini writes; one extra Save at the end records the applied hashes.
  void Flush(){if(pending.Count==0)return;Save(data);foreach(var item in pending){if(disposed)break;try{WriteFolder(item.folder,item.icon,item.saved);}catch(Exception ex){Store.Log("Icône dossier ignorée: "+item.folder+": "+ex.Message);}}pending.Clear();flushed=true;}
  if(Within(full,true)&&!Protected(full))Theme(full,Path.GetDirectoryName(full)??full);
  var level=new List<string>{full};
  for(int depth=0;depth<3&&!stop&&level.Count>0;depth++){var next=new List<string>();
   foreach(string dir in level){if(stop||disposed||Protected(dir)||Skipped(dir))continue;List<string> children;try{children=Directory.EnumerateDirectories(dir).Take(256).ToList();}catch{continue;}
    foreach(string child in children){if(stop||++seen>1024){stop=true;break;}if(Theme(child,dir))next.Add(child);}}
   level=next;}
  Flush();if(flushed)Save(data);
  if(flushed)Status=data.Count+" dossier(s) assorti(s), y compris les dossiers affichés.";
 }}string accent="#afd4f0",background="#101518",text="#e5ebef",signature="";volatile bool disposed;
 static string Real(string p){p=Path.GetFullPath(p);try{p=new DirectoryInfo(p).ResolveLinkTarget(true)?.FullName??p;}catch{}return p.TrimEnd(Path.DirectorySeparatorChar);}
 // Containment in one of the six personal folders (real paths); strict excludes the personal folder itself.
 static bool Within(string full,bool strict){string real=Real(full);return PersonalFolders().Any(p=>{if(p.path.Length==0)return false;string root=Real(p.path);return (!strict&&real.Equals(root,StringComparison.OrdinalIgnoreCase))||real.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);});}
 static readonly HashSet<string> DevDirs=new(StringComparer.OrdinalIgnoreCase){"node_modules","bin","obj",".venv","__pycache__","target","build","dist"};
 static bool Skipped(string folder){try{return DevDirs.Contains(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)))||Directory.Exists(Path.Combine(folder,".git"))||File.Exists(Path.Combine(folder,".git"));}catch{return true;}}
 internal ExplorerAppearance(string? directory=null){root=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Winlestia","Explorer");timer.Tick+=(_,_)=>FrameTick();timer.Start();}
 string Manifest=>Path.Combine(root,"manifest.json");
 List<Backup> Read()=>File.Exists(Manifest)?JsonSerializer.Deserialize<List<Backup>>(File.ReadAllText(Manifest))??new():new();
 void Save(List<Backup> data){Directory.CreateDirectory(root);string temp=Manifest+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(data));File.Move(temp,Manifest,true);}
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 internal static string Known(string guid){var id=new Guid(guid);if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out var pointer)!=0)return "";try{return Marshal.PtrToStringUni(pointer)??"";}finally{Marshal.FreeCoTaskMem(pointer);}}
 internal static (string path,string kind)[] PersonalFolders()=>new[]{(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"desktop"),(Known("374DE290-123F-4565-9164-39C4925E467B"),"download"),(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"document"),(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"picture"),(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),"music"),(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"video")};
 internal string Sync(string a,string bg,string fg,bool force=false){lock(gate){if(disposed)return "";foreach(string value in new[]{a,bg,fg})if(!System.Text.RegularExpressions.Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$"))throw new ArgumentException("Palette invalide");accent=a;background=bg;text=fg;
  if(!Store.State.ExplorerTheme)return "";if(!repairPassDone){repairPassDone=true;_=System.Threading.Tasks.Task.Run(RepairManifestDates);}StartIconMaintenance();string key=a+bg+fg+string.Join("|",Store.State.ExplorerFolders);if(!force&&key==signature)return "";
  var report=new List<string>();var folders=PersonalFolders().Concat(Store.State.ExplorerFolders.Select(p=>(p,"folder"))).DistinctBy(p=>p.Item1,StringComparer.OrdinalIgnoreCase).ToArray();
  foreach(var item in folders){try{if(!Directory.Exists(item.Item1))continue;string icon=EnsureIcon(item.Item2);ApplyFolder(item.Item1,icon);report.Add("OK "+item.Item1);}catch(Exception ex){report.Add("SKIPPED "+item.Item1+": "+ex.Message);}}
  Directory.CreateDirectory(root);File.WriteAllLines(Path.Combine(root,"last-report.txt"),report);signature=key;Status=report.Count(p=>p.StartsWith("OK "))+" dossier(s) assorti(s) · "+report.Count(p=>p.StartsWith("SKIPPED "))+" ignoré(s).";return string.Join(Environment.NewLine,report);
 }}
 internal void ApplyFolder(string folder,string icon){lock(gate){var data=Read();if(AlreadyThemed(folder,icon,data))return;int before=data.Count;var prep=Prepare(folder,data);if(data.Count>before)Save(data);WriteFolder(prep.folder,icon,prep.saved);Save(data);}}
 // Validates the folder and records its backup in memory (caller persists it before WriteFolder).
 (string folder,Backup saved) Prepare(string folder,List<Backup> data){folder=Path.GetFullPath(folder);if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)folder=new DirectoryInfo(folder).ResolveLinkTarget(true)?.FullName??throw new IOException("Cible du dossier introuvable.");if(folder.StartsWith(@"\\")||folder.Length>240||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Dossier distant, redirigé ou chemin non pris en charge.");
  if(Protected(folder))throw new IOException("Emplacement système exclu.");
  if(File.Exists(Path.Combine(folder,"desktop.ini"))&&(File.GetAttributes(Path.Combine(folder,"desktop.ini"))&FileAttributes.ReparsePoint)!=0)throw new IOException("desktop.ini redirigé : conservé.");
  var saved=data.FirstOrDefault(p=>p.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase));string ini=Path.Combine(folder,"desktop.ini");
  if(saved==null){saved=new(){Folder=folder,Ini=File.Exists(ini)?Convert.ToBase64String(File.ReadAllBytes(ini)):null,Attributes=(int)File.GetAttributes(folder),IniAttributes=File.Exists(ini)?(int)File.GetAttributes(ini):0,OriginalWriteUtc=Directory.GetLastWriteTimeUtc(folder)};data.Add(saved);}
  else if(saved.AppliedHash!=null&&(!File.Exists(ini)||Hash(ini)!=saved.AppliedHash))throw new IOException("desktop.ini modifié ailleurs : conservé.");
  return (folder,saved);}
 // Folder "Modified" date: kept across writes (stored original, else one-shot heuristic for entries themed before OriginalWriteUtc existed).
 DateTime? Stamp(Backup saved){try{if(saved.OriginalWriteUtc!=null)return saved.OriginalWriteUtc;if(!saved.DateRepaired){RepairDatesFromChildren(saved.Folder);saved.DateRepaired=true;}return Directory.GetLastWriteTimeUtc(saved.Folder);}catch{return null;}}
 static void SetStamp(string folder,DateTime? when){if(when==null)return;try{Directory.SetLastWriteTimeUtc(folder,when.Value);}catch{}}
 // Sets the folder mtime to the newest child (desktop.ini and our own icons excluded); unchanged when there is no such child.
 internal void RepairDatesFromChildren(string folder){try{string icons=Path.Combine(root,"icons");DateTime? newest=null;foreach(var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos()){if(entry.Name.Equals("desktop.ini",StringComparison.OrdinalIgnoreCase)||entry.FullName.StartsWith(icons,StringComparison.OrdinalIgnoreCase))continue;if(newest==null||entry.LastWriteTimeUtc>newest)newest=entry.LastWriteTimeUtc;}SetStamp(folder,newest);}catch{}}
 // One-shot (first Sync after startup): repairs themed folders that are never re-written (unchanged, not visited). Max 256 entries, one Save.
 bool repairPassDone;
 internal void RepairManifestDates(){lock(gate){if(disposed)return;try{var data=Read();int done=0;foreach(var saved in data){if(saved.DateRepaired||saved.OriginalWriteUtc!=null||!Directory.Exists(saved.Folder))continue;if(done++>=256)break;RepairDatesFromChildren(saved.Folder);saved.DateRepaired=true;}if(done>0)Save(data);}catch(Exception ex){Store.Log("Réparation des dates: "+ex.Message);}}}
 void WriteFolder(string folder,string icon,Backup saved){DateTime? stamp=Stamp(saved);try{WriteFolderCore(folder,icon,saved);}finally{SetStamp(folder,stamp);}}
 void WriteFolderCore(string folder,string icon,Backup saved){string ini=Path.Combine(folder,"desktop.ini");if(!PathMakeSystemFolderW(folder))throw new IOException("Windows refuse la personnalisation.");var settings=new Settings{Size=(uint)Marshal.SizeOf<Settings>(),Mask=0x10,Icon=icon};Marshal.ThrowExceptionForHR(SHGetSetFolderCustomSettings(ref settings,folder,2));saved.AppliedHash=Hash(ini);Notify(folder);}
 internal string Restore(){lock(gate){int navigationConflicts=ExplorerNavigationIcons.Restore(root);SHChangeNotify(0x08000000,0,null,IntPtr.Zero);var data=Read();var remaining=new List<Backup>();foreach(var saved in data){try{if(!Directory.Exists(saved.Folder))continue;DateTime? stamp=Stamp(saved);string ini=Path.Combine(saved.Folder,"desktop.ini");if(saved.AppliedHash!=null&&(!File.Exists(ini)||Hash(ini)!=saved.AppliedHash))throw new IOException("desktop.ini modifié ailleurs : restauration manuelle requise.");
   if(File.Exists(ini))File.SetAttributes(ini,FileAttributes.Normal);if(saved.Ini==null){if(File.Exists(ini))File.Delete(ini);}else{File.WriteAllBytes(ini,Convert.FromBase64String(saved.Ini));File.SetAttributes(ini,(FileAttributes)saved.IniAttributes);}File.SetAttributes(saved.Folder,(FileAttributes)saved.Attributes);SHChangeNotify(0x2000,5,saved.Folder,IntPtr.Zero);SetStamp(saved.Folder,stamp);
  }catch(Exception ex){remaining.Add(saved);Store.Log("Explorateur restauration: "+ex.Message);}}Save(remaining);scanned.Clear();signature="";if(navigationConflicts>0){Status=navigationConflicts+" réglage(s) de navigation modifié(s) ailleurs, conservé(s).";return Status;}Status=remaining.Count==0?"Icônes d’origine restaurées.":remaining.Count+" conflit(s) conservé(s).";return remaining.Count==0?"Icônes d’origine restaurées.":remaining.Count+" dossier(s) conservé(s), consulter le journal.";}}
 static uint Colour(string hex){var c=D.ColorTranslator.FromHtml(hex);return (uint)(c.R|c.G<<8|c.B<<16);}
 void FrameTick(){if(disposed)return;if(!Store.State.ExplorerTheme&&!Store.State.AccentBorders){RestoreFrames();return;}var alive=new HashSet<IntPtr>();var sig=new List<string>();TaskbarNative.EnumWindows((h,_)=>{
  bool explorer=TaskbarNative.ClassName(h)=="CabinetWClass";
  if(Native.Owns(h)||!TaskbarNative.IsWindowVisible(h)||TaskbarNative.IsIconic(h)||(Style(h,-16).ToInt64()&0x40000000)!=0||(Native.ExtendedStyle(h)&0x80)!=0||string.IsNullOrWhiteSpace(Native.Title(h))||(!explorer&&!Store.State.AccentBorders))return true;
  var style=Style(h,-16).ToInt64();if((style&0x00c40000)==0)return true;
  GetWindowThreadProcessId(h,out uint pid);if(pid==ExcludedProcess)return true;alive.Add(h);ColourFrame(h,explorer);if(explorer)sig.Add(h+"|"+Native.Title(h));
  return true;},IntPtr.Zero);foreach(var h in frames.Keys.Where(h=>!alive.Contains(h)).ToArray())RestoreFrame(h);
  sig.Sort(StringComparer.Ordinal);string now=string.Join("|;",sig);if(now!=windowSignature){windowSignature=now;if(now.Length>0)RequestScan();}
  if(DateTime.UtcNow>=scanDue){scanDue=DateTime.MaxValue;ScanVisibleFolders();}
 }
 internal bool ColourFrame(IntPtr h,bool explorer){
  GetWindowThreadProcessId(h,out uint pid);if(frames.ContainsKey(h)&&frameOwners[h]!=pid){frames.Remove(h);frameOwners.Remove(h);captionFrames.Remove(h);}
  if(!frames.ContainsKey(h)){var original=new uint[3];for(int i=0;i<3;i++)if(DwmGetWindowAttribute(h,34+i,out original[i],4)!=0)original[i]=0xffffffff;frames[h]=original;frameOwners[h]=pid;}
  uint border=Colour(accent);bool accepted=DwmSetWindowAttribute(h,34,ref border,4)>=0;
  if(explorer&&Store.State.ExplorerTheme){captionFrames.Add(h);uint bg=Colour(background),fg=Colour(text);DwmSetWindowAttribute(h,35,ref bg,4);DwmSetWindowAttribute(h,36,ref fg,4);}
  else if(captionFrames.Remove(h)){for(int i=1;i<3;i++){uint original=frames[h][i];DwmSetWindowAttribute(h,34+i,ref original,4);}}
  return accepted;
 }
 internal bool RestoreFrame(IntPtr h){if(!frames.Remove(h,out var original))return true;bool restored=true;GetWindowThreadProcessId(h,out uint pid);bool captions=captionFrames.Remove(h);if(frameOwners.Remove(h,out uint owner)&&pid==owner&&Native.IsWindow(h))for(int i=0;i<(captions?3:1);i++){uint value=original[i];restored&=DwmSetWindowAttribute(h,34+i,ref value,4)>=0;}return restored;}
 void RestoreFrames(){foreach(var h in frames.Keys.ToArray())RestoreFrame(h);}
 internal static void DrawIcon(string path,string hex,string kind){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var images=new List<byte[]>();int[] sizes={256,128,64,48,32,16};foreach(int size in sizes){using var bitmap=new D.Bitmap(size,size);using var g=D.Graphics.FromImage(bitmap);g.SmoothingMode=G.SmoothingMode.AntiAlias;g.ScaleTransform(size/256f,size/256f);var c=D.ColorTranslator.FromHtml(hex);D.Color Mix(int white)=>D.Color.FromArgb((c.R*(255-white)+255*white)/255,(c.G*(255-white)+255*white)/255,(c.B*(255-white)+255*white)/255);
  using var shadow=new D.SolidBrush(D.Color.FromArgb(30,0,0,0));g.FillRectangle(shadow,28,73,204,151);
  using var back=new G.GraphicsPath();back.AddLines(new[]{new D.PointF(23,81),new D.PointF(23,58),new D.PointF(33,46),new D.PointF(99,46),new D.PointF(121,66),new D.PointF(222,66),new D.PointF(234,80),new D.PointF(234,199),new D.PointF(222,212),new D.PointF(34,212)});back.CloseFigure();using var backFill=new D.SolidBrush(Mix(75));g.FillPath(backFill,back);
  using var paper=new D.SolidBrush(D.Color.FromArgb(220,255,255,255));g.FillRectangle(paper,44,77,169,34);
  using var front=new G.GraphicsPath();front.AddArc(18,89,28,28,180,90);front.AddArc(210,89,28,28,270,90);front.AddArc(210,191,28,28,0,90);front.AddArc(18,191,28,28,90,90);front.CloseFigure();using var fill=new G.LinearGradientBrush(new D.Rectangle(18,89,220,130),Mix(38),c,90);g.FillPath(fill,front);using var edge=new D.Pen(D.Color.FromArgb(85,255,255,255),2);g.DrawPath(edge,front);
  double luminance=.2126*c.R+.7152*c.G+.0722*c.B;using var pen=new D.Pen(luminance>170?D.Color.FromArgb(170,25,30,35):D.Color.FromArgb(230,255,255,255),7){StartCap=G.LineCap.Round,EndCap=G.LineCap.Round,LineJoin=G.LineJoin.Round};
  switch(kind){case "download":g.DrawLine(pen,128,125,128,175);g.DrawLines(pen,new[]{new D.Point(109,157),new D.Point(128,176),new D.Point(147,157)});g.DrawLine(pen,107,190,149,190);break;case "document":g.DrawRectangle(pen,108,123,42,63);g.DrawLine(pen,118,143,140,143);g.DrawLine(pen,118,158,140,158);break;case "picture":g.DrawRectangle(pen,96,128,66,48);g.DrawLines(pen,new[]{new D.Point(101,169),new D.Point(120,149),new D.Point(134,163),new D.Point(145,151),new D.Point(157,169)});break;case "music":g.DrawLines(pen,new[]{new D.Point(116,174),new D.Point(116,130),new D.Point(147,123),new D.Point(147,165)});g.DrawEllipse(pen,101,171,15,9);g.DrawEllipse(pen,132,163,15,9);break;case "video":g.DrawPolygon(pen,new[]{new D.Point(112,126),new D.Point(151,153),new D.Point(112,180)});break;case "desktop":g.DrawRectangle(pen,96,127,65,43);g.DrawLine(pen,128,173,128,188);g.DrawLine(pen,111,190,145,190);break;default:g.DrawLine(pen,110,152,146,152);g.DrawLine(pen,128,134,128,170);break;}
  using var memory=new MemoryStream();
  if(size==256)bitmap.Save(memory,D.Imaging.ImageFormat.Png);
  else{using var dib=new BinaryWriter(memory,System.Text.Encoding.UTF8,true);int maskStride=((size+31)/32)*4;dib.Write(40);dib.Write(size);dib.Write(size*2);dib.Write((ushort)1);dib.Write((ushort)32);dib.Write(0);dib.Write(size*size*4+maskStride*size);for(int i=0;i<4;i++)dib.Write(0);
   for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){var pixel=bitmap.GetPixel(x,y);dib.Write(pixel.B);dib.Write(pixel.G);dib.Write(pixel.R);dib.Write(pixel.A);}
   for(int y=size-1;y>=0;y--){var mask=new byte[maskStride];for(int x=0;x<size;x++)if(bitmap.GetPixel(x,y).A==0)mask[x/8]|=(byte)(128>>(x%8));dib.Write(mask);}
  }images.Add(memory.ToArray());}
  using var output=File.Create(path);using var writer=new BinaryWriter(output);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]%256));writer.Write((byte)(sizes[i]%256));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}foreach(var image in images)writer.Write(image);
 }
 public void Dispose(){disposed=true;timer.Stop();DisposeIcons();lock(watchers){foreach(var w in watchers.Values)w.Dispose();watchers.Clear();}RestoreFrames();}
}
