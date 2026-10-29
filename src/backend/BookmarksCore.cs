// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Favoris des navigateurs : détection, lecture, favicons, suppression Chromium.
// Aucune dépendance au reste de l'application (testable seul). Ne journalise jamais d'URL ni de titre : seulement des comptes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace PanneauxGlass;

internal sealed class BrowserInfo {
    public string Id="",Name="",Exe="",UserData="",ProfileDir="",InstallDir="";
    public bool Chromium,Installed,IsDefault;
    public string[] Processes=Array.Empty<string>();
    public string BookmarksFile=>Chromium?Path.Combine(ProfileDir,"Bookmarks"):Path.Combine(ProfileDir,"places.sqlite");
}

internal sealed class BNode {
    public string Id="",Title="",Url="",Icon="";
    public bool Folder;
    public List<BNode> Children=new();
}

internal static class BookmarksCore {

    const int MaxNodes=30000,MaxDepth=24;

    static readonly string Local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Programs=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),ProgramsX86=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    internal static string WorkRoot=>Path.Combine(Local,"Winlestia","Bookmarks");

    // ---- Détection ---------------------------------------------------------------------------------------------
    static string FromAppPaths(string exe){
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{
                using var baseKey=RegistryKey.OpenBaseKey(hive,view);
                using var key=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+exe);
                if(key?.GetValue(null) is string path){path=path.Trim('"');if(File.Exists(path))return path;}
            }catch{}
        }
        return "";
    }

    static string First(params string[] paths)=>paths.FirstOrDefault(File.Exists)??"";

    public static List<BrowserInfo> Detect(){
        var list=new List<BrowserInfo>();
        void Add(string id,string name,string exe,bool chromium,string userData,params string[] procs){
            var b=new BrowserInfo{Id=id,Name=name,Exe=exe,Chromium=chromium,UserData=userData,Processes=procs};
            try{
                if(exe.Length>0)b.InstallDir=Path.GetDirectoryName(exe)??"";
                if(chromium&&Directory.Exists(userData)){
                    string profile="Default";
                    if(id is not ("opera" or "operagx")){
                        try{
                            string state=Path.Combine(userData,"Local State");
                            if(File.Exists(state)){using var doc=JsonDocument.Parse(ReadShared(state));if(doc.RootElement.TryGetProperty("profile",out var p)&&p.TryGetProperty("last_used",out var last)&&last.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(last.GetString()))profile=last.GetString()!;}
                        }catch{}
                        if(profile.IndexOfAny(new[]{'\\','/'})>=0||profile.Contains(".."))profile="Default";
                        b.ProfileDir=Path.Combine(userData,profile);
                    }else b.ProfileDir=userData;
                    b.Installed=exe.Length>0&&File.Exists(b.BookmarksFile);
                }else if(!chromium){
                    b.ProfileDir=FirefoxProfile();
                    b.Installed=exe.Length>0&&b.ProfileDir.Length>0&&File.Exists(b.BookmarksFile);
                }
            }catch{}
            list.Add(b);
        }
        Add("edge","Microsoft Edge",First(FromAppPaths("msedge.exe"),Path.Combine(ProgramsX86,@"Microsoft\Edge\Application\msedge.exe"),Path.Combine(Programs,@"Microsoft\Edge\Application\msedge.exe")),true,Path.Combine(Local,@"Microsoft\Edge\User Data"),"msedge");
        Add("chrome","Google Chrome",First(FromAppPaths("chrome.exe"),Path.Combine(Programs,@"Google\Chrome\Application\chrome.exe"),Path.Combine(ProgramsX86,@"Google\Chrome\Application\chrome.exe"),Path.Combine(Local,@"Google\Chrome\Application\chrome.exe")),true,Path.Combine(Local,@"Google\Chrome\User Data"),"chrome");
        Add("brave","Brave",First(FromAppPaths("brave.exe"),Path.Combine(Programs,@"BraveSoftware\Brave-Browser\Application\brave.exe"),Path.Combine(Local,@"BraveSoftware\Brave-Browser\Application\brave.exe")),true,Path.Combine(Local,@"BraveSoftware\Brave-Browser\User Data"),"brave");
        Add("opera","Opera",First(Path.Combine(Local,@"Programs\Opera\opera.exe"),Path.Combine(Programs,@"Opera\opera.exe"),Path.Combine(Local,@"Programs\Opera\launcher.exe")),true,Path.Combine(Roaming,@"Opera Software\Opera Stable"),"opera");
        Add("operagx","Opera GX",First(Path.Combine(Local,@"Programs\Opera GX\opera.exe"),Path.Combine(Programs,@"Opera GX\opera.exe"),Path.Combine(Local,@"Programs\Opera GX\launcher.exe")),true,Path.Combine(Roaming,@"Opera Software\Opera GX Stable"),"opera");
        Add("vivaldi","Vivaldi",First(FromAppPaths("vivaldi.exe"),Path.Combine(Local,@"Vivaldi\Application\vivaldi.exe"),Path.Combine(Programs,@"Vivaldi\Application\vivaldi.exe")),true,Path.Combine(Local,@"Vivaldi\User Data"),"vivaldi");
        Add("firefox","Mozilla Firefox",First(FromAppPaths("firefox.exe"),Path.Combine(Programs,@"Mozilla Firefox\firefox.exe"),Path.Combine(ProgramsX86,@"Mozilla Firefox\firefox.exe")),false,"","firefox");
        string def=DefaultBrowserId();
        foreach(var b in list)b.IsDefault=b.Id==def;
        return list;
    }

    static string DefaultBrowserId(){
        try{
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            string prog=(key?.GetValue("ProgId") as string)??"";
            if(prog.Contains("MSEdge",StringComparison.OrdinalIgnoreCase))return "edge";
            if(prog.Contains("Chrome",StringComparison.OrdinalIgnoreCase))return "chrome";
            if(prog.Contains("Brave",StringComparison.OrdinalIgnoreCase))return "brave";
            if(prog.Contains("Firefox",StringComparison.OrdinalIgnoreCase))return "firefox";
            if(prog.Contains("Vivaldi",StringComparison.OrdinalIgnoreCase))return "vivaldi";
            if(prog.Contains("OperaGX",StringComparison.OrdinalIgnoreCase))return "operagx";
            if(prog.Contains("Opera",StringComparison.OrdinalIgnoreCase))return "opera";
        }catch{}
        return "";
    }

    static string FirefoxProfile(){
        try{
            string root=Path.Combine(Roaming,@"Mozilla\Firefox"),ini=Path.Combine(root,"profiles.ini");
            if(!File.Exists(ini))return "";
            var sections=new List<(string name,Dictionary<string,string> values)>();
            foreach(string raw in File.ReadAllLines(ini)){
                string line=raw.Trim();if(line.Length==0||line[0]==';')continue;
                if(line[0]=='['&&line[^1]==']'){sections.Add((line[1..^1],new(StringComparer.OrdinalIgnoreCase)));continue;}
                int eq=line.IndexOf('=');if(eq>0&&sections.Count>0)sections[^1].values[line[..eq].Trim()]=line[(eq+1)..].Trim();
            }
            string Resolve(string path,bool relative)=>relative?Path.GetFullPath(Path.Combine(root,path.Replace('/','\\'))):path.Replace('/','\\');
            foreach(var s in sections.Where(s=>s.name.StartsWith("Install",StringComparison.OrdinalIgnoreCase)))
                if(s.values.TryGetValue("Default",out var d)&&d.Length>0){string dir=Resolve(d,!Path.IsPathRooted(d));if(Directory.Exists(dir))return dir;}
            foreach(var s in sections.Where(s=>s.name.StartsWith("Profile",StringComparison.OrdinalIgnoreCase)&&s.values.GetValueOrDefault("Default")=="1"))
                if(s.values.TryGetValue("Path",out var p)){string dir=Resolve(p,s.values.GetValueOrDefault("IsRelative","1")!="0");if(Directory.Exists(dir))return dir;}
            foreach(var s in sections.Where(s=>s.name.StartsWith("Profile",StringComparison.OrdinalIgnoreCase)))
                if(s.values.TryGetValue("Path",out var p)){string dir=Resolve(p,s.values.GetValueOrDefault("IsRelative","1")!="0");if(File.Exists(Path.Combine(dir,"places.sqlite")))return dir;}
        }catch{}
        return "";
    }

    internal static bool IsRunning(BrowserInfo b){
        foreach(string name in b.Processes){
            Process[] found;
            try{found=Process.GetProcessesByName(name);}catch{return true;}
            foreach(var p in found){
                try{
                    string? file=p.MainModule?.FileName;
                    if(file==null||b.InstallDir.Length==0||file.StartsWith(b.InstallDir,StringComparison.OrdinalIgnoreCase))return true;
                }catch{return true;} // illisible : par prudence on considère que le navigateur tourne
                finally{p.Dispose();}
            }
        }
        try{
            string lockfile=Path.Combine(b.UserData,"lockfile");
            if(File.Exists(lockfile)){using var fs=new FileStream(lockfile,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}
        }catch{return true;} // verrouillé (ou illisible) : le navigateur tourne
        return false;
    }

    // ---- Utilitaires ------------------------------------------------------------------------------------------
    static byte[] ReadShared(string path){
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var memory=new MemoryStream();input.CopyTo(memory);return memory.ToArray();
    }

    static bool CopyShared(string source,string target){
        try{
            if(!File.Exists(source))return false;
            using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var output=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.None);
            input.CopyTo(output);return true;
        }catch{return false;}
    }

    static string Hash(string text)=>Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    static SqliteConnection Open(string file){
        var connection=new SqliteConnection("Data Source="+file+";Mode=ReadWriteCreate;Pooling=False");
        connection.Open();return connection;
    }

    // Copie places.sqlite (+ -wal) dans un dossier de travail ; l'original n'est jamais ouvert en SQLite.
    static string? CopyDatabase(string source,string workDir){
        Directory.CreateDirectory(workDir);
        string name=Path.GetFileName(source),target=Path.Combine(workDir,name);
        if(!CopyShared(source,target))return null;
        CopyShared(source+"-wal",target+"-wal");
        return target;
    }

    internal static int CountUrls(IEnumerable<BNode> nodes){int n=0;foreach(var x in nodes)n+=x.Folder?CountUrls(x.Children):1;return n;}

    // ---- Lecture ---------------------------------------------------------------------------------------------
    public static List<BNode> Read(BrowserInfo b)=>b.Chromium?ReadChromium(b):ReadFirefox(b);

    static string NodeId(string browser,JsonNode node){
        string guid=node["guid"]?.GetValue<string>()??"";
        if(guid.Length>0)return browser+":"+guid;
        return browser+":u"+Hash((node["url"]?.GetValue<string>()??"")+"|"+(node["name"]?.GetValue<string>()??""))[..12];
    }

    static List<BNode> ReadChromium(BrowserInfo b){
        var root=JsonNode.Parse(ReadShared(b.BookmarksFile))?["roots"];
        var result=new List<BNode>();if(root==null)return result;
        int budget=MaxNodes;
        List<BNode> Children(JsonNode? folder,int depth){
            var list=new List<BNode>();
            if(folder?["children"] is not JsonArray array||depth>MaxDepth)return list;
            foreach(var child in array){
                if(child==null||budget--<=0)continue;
                string type=child["type"]?.GetValue<string>()??"";
                var node=new BNode{Id=NodeId(b.Id,child),Title=child["name"]?.GetValue<string>()??""};
                if(type=="folder"){node.Folder=true;node.Children=Children(child,depth+1);}
                else if(type=="url"){node.Url=child["url"]?.GetValue<string>()??"";if(node.Url.Length==0)continue;}
                else continue;
                list.Add(node);
            }
            return list;
        }
        result.AddRange(Children(root["bookmark_bar"],0));
        var other=Children(root["other"],0);
        if(other.Count>0)result.Add(new BNode{Id=b.Id+":other",Title="Autres favoris",Folder=true,Children=other});
        var mobile=Children(root["synced"],0);
        if(mobile.Count>0)result.Add(new BNode{Id=b.Id+":synced",Title="Favoris mobiles",Folder=true,Children=mobile});
        return result;
    }

    static List<BNode> ReadFirefox(BrowserInfo b){
        string work=Path.Combine(WorkRoot,"tmp",Guid.NewGuid().ToString("N"));
        try{
            string? copy=CopyDatabase(b.BookmarksFile,work);if(copy==null)throw new IOException("places.sqlite illisible");
            var rows=new Dictionary<int,(int type,int parent,string title,string guid,string url)>();var order=new List<int>();
            using(var connection=Open(copy)){
                using var command=connection.CreateCommand();
                command.CommandText="SELECT b.id,b.type,b.parent,COALESCE(b.title,''),b.guid,COALESCE(p.url,'') FROM moz_bookmarks b LEFT JOIN moz_places p ON p.id=b.fk ORDER BY b.parent,b.position";
                using var reader=command.ExecuteReader();
                while(reader.Read()){int id=reader.GetInt32(0);rows[id]=(reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3),reader.GetString(4),reader.GetString(5));order.Add(id);}
            }
            int budget=MaxNodes;
            List<BNode> Children(int parent,int depth){
                var list=new List<BNode>();if(depth>MaxDepth)return list;
                foreach(int id in order){
                    var r=rows[id];if(r.parent!=parent||budget--<=0)continue;
                    if(r.type==2){list.Add(new BNode{Id=b.Id+":"+r.guid,Title=r.title,Folder=true,Children=Children(id,depth+1)});}
                    else if(r.type==1&&r.url.Length>0&&!r.url.StartsWith("place:",StringComparison.OrdinalIgnoreCase))list.Add(new BNode{Id=b.Id+":"+r.guid,Title=r.title,Url=r.url});
                }
                return list;
            }
            int Find(string guid)=>rows.Where(kv=>kv.Value.guid==guid).Select(kv=>kv.Key).DefaultIfEmpty(-1).First();
            var result=new List<BNode>();
            int toolbar=Find("toolbar_____"),menu=Find("menu________"),unfiled=Find("unfiled_____");
            if(toolbar>=0)result.AddRange(Children(toolbar,0));
            var other=new List<BNode>();
            if(menu>=0)other.AddRange(Children(menu,0));
            if(unfiled>=0)other.AddRange(Children(unfiled,0));
            if(other.Count>0)result.Add(new BNode{Id=b.Id+":other",Title="Autres favoris",Folder=true,Children=other});
            return result;
        }
        finally{
            SqliteConnection.ClearAllPools();
            try{Directory.Delete(work,true);}catch{}
        }
    }

    // ---- Favicons (cache disque 32 px, aucune requête réseau) --------------------------------------------------
    internal static string IconsDir=>Path.Combine(WorkRoot,"icons");
    static readonly HashSet<string> NoIcon=new(); // hôtes sans icône trouvée : on ne recopie pas la base à chaque scan

    static string HostOf(string url){
        if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||(u.Scheme!=Uri.UriSchemeHttp&&u.Scheme!=Uri.UriSchemeHttps))return "";
        return u.Host.ToLowerInvariant();
    }

    static void Collect(IEnumerable<BNode> nodes,Dictionary<string,string> hosts){
        foreach(var n in nodes){
            if(n.Folder){Collect(n.Children,hosts);continue;}
            string host=HostOf(n.Url);if(host.Length>0&&!hosts.ContainsKey(host))hosts[host]=n.Url;
        }
    }

    static void Assign(IEnumerable<BNode> nodes){
        foreach(var n in nodes){
            if(n.Folder){Assign(n.Children);continue;}
            string host=HostOf(n.Url);if(host.Length==0)continue;
            string file=Path.Combine(IconsDir,Hash(host)+".png");
            if(File.Exists(file))n.Icon=new Uri(file).AbsoluteUri;
        }
    }

    public static int BuildIcons(BrowserInfo b,List<BNode> items){
        var hosts=new Dictionary<string,string>();Collect(items,hosts);
        Directory.CreateDirectory(IconsDir);
        var missing=hosts.Where(kv=>!File.Exists(Path.Combine(IconsDir,Hash(kv.Key)+".png"))&&!NoIcon.Contains(kv.Key)).ToList();
        int created=0;
        if(missing.Count>0){
            string work=Path.Combine(WorkRoot,"tmp",Guid.NewGuid().ToString("N"));
            try{
                string source=b.Chromium?Path.Combine(b.ProfileDir,"Favicons"):Path.Combine(b.ProfileDir,"favicons.sqlite");
                string? copy=CopyDatabase(source,work);
                if(copy!=null){
                    using var connection=Open(copy);
                    foreach(var (host,url) in missing){
                        byte[]? data=Query(connection,b.Chromium,url,host);
                        byte[]? png=data==null?null:ToPng32(data);
                        if(png==null){NoIcon.Add(host);continue;}
                        try{File.WriteAllBytes(Path.Combine(IconsDir,Hash(host)+".png"),png);created++;}catch{}
                    }
                }
            }catch(Exception ex){Log("Favoris icônes : "+ex.GetType().Name);}
            finally{SqliteConnection.ClearAllPools();try{Directory.Delete(work,true);}catch{}}
        }
        Assign(items);
        return created;
    }

    static void Log(string text){try{Store.Log(text);}catch{}}

    static byte[]? Query(SqliteConnection connection,bool chromium,string url,string host){
        string select=chromium
            ?"SELECT b.image_data FROM icon_mapping m JOIN favicon_bitmaps b ON b.icon_id=m.icon_id WHERE m.page_url{0} AND b.image_data IS NOT NULL ORDER BY ABS(b.width-32) LIMIT 1"
            :"SELECT i.data FROM moz_pages_w_icons p JOIN moz_icons_to_pages ip ON ip.page_id=p.id JOIN moz_icons i ON i.id=ip.icon_id WHERE p.page_url{0} AND i.data IS NOT NULL AND i.width<1024 ORDER BY ABS(i.width-32) LIMIT 1";
        foreach(var (clause,value) in new[]{("=$u",url),(" LIKE $u","https://"+host+"/%"),(" LIKE $u","http://"+host+"/%")}){
            try{
                using var command=connection.CreateCommand();
                command.CommandText=string.Format(select,clause);command.Parameters.AddWithValue("$u",value);
                if(command.ExecuteScalar() is byte[] data&&data.Length>0)return data;
            }catch{return null;}
        }
        return null;
    }

    static byte[]? ToPng32(byte[] data){
        try{
            using var stream=new MemoryStream(data);
            var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame=decoder.Frames.OrderBy(f=>Math.Abs(f.PixelWidth-32)).FirstOrDefault();
            if(frame==null||frame.PixelWidth<8||frame.PixelHeight<8||frame.PixelWidth>2048)return null;
            double scale=32.0/Math.Max(frame.PixelWidth,frame.PixelHeight);
            System.Windows.Media.Imaging.BitmapSource source=scale==1?frame:new System.Windows.Media.Imaging.TransformedBitmap(frame,new System.Windows.Media.ScaleTransform(scale,scale));
            source=new System.Windows.Media.Imaging.FormatConvertedBitmap(source,System.Windows.Media.PixelFormats.Bgra32,null,0);
            source.Freeze();
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
        }catch{return null;}
    }

    // ---- Suppression dans le navigateur (Chromium seulement) -------------------------------------------------
    // Renvoie null si réussi, sinon un code : unsupported, running, sync, notfound, error.
    static int backupCounter;
    public static string? DeleteChromium(BrowserInfo b,string id,bool confirmSync,string backupsDir,string? bookmarksOverride=null,bool skipChecks=false){
        if(!b.Chromium)return "unsupported";
        string file=bookmarksOverride??b.BookmarksFile;
        try{
            if(!skipChecks){
                if(IsRunning(b))return "running";
                if(!confirmSync&&SyncActive(b))return "sync";
            }
            if(!id.StartsWith(b.Id+":",StringComparison.Ordinal)||id.EndsWith(":other")||id.EndsWith(":synced"))return "notfound";
            var root=JsonNode.Parse(ReadShared(file));
            if(root?["roots"] is not JsonObject roots)return "error";
            int matches=0;bool hasGuid=false;
            void Count(JsonNode? folder){
                if(folder?["children"] is not JsonArray array)return;
                foreach(var child in array){
                    if(child==null)continue;
                    if(NodeId(b.Id,child)==id){matches++;if(child["guid"] is JsonValue g&&g.TryGetValue<string>(out var gs)&&gs.Length>0)hasGuid=true;}
                    Count(child);
                }
            }
            foreach(var kv in roots)Count(kv.Value);
            if(matches==0)return "notfound";
            if(matches>1||!hasGuid)return "ambiguous";
            Directory.CreateDirectory(backupsDir);
            string backup=Path.Combine(backupsDir,b.Id+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+"-"+Interlocked.Increment(ref backupCounter)+".json");
            File.Copy(file,backup,false);
            try{
                var old=new DirectoryInfo(backupsDir).GetFiles(b.Id+"-*.json").OrderByDescending(f=>f.LastWriteTimeUtc).ThenByDescending(f=>f.Name,StringComparer.Ordinal).Skip(20);
                foreach(var f in old){try{f.Delete();}catch{}}
            }catch{}
            bool removed=false;
            bool Remove(JsonNode? folder){
                if(folder?["children"] is not JsonArray array)return false;
                for(int i=0;i<array.Count;i++){
                    var child=array[i];if(child==null)continue;
                    if(NodeId(b.Id,child)==id){array.RemoveAt(i);return true;}
                    if(Remove(child))return true;
                }
                return false;
            }
            foreach(var kv in roots)if(Remove(kv.Value)){removed=true;break;}
            if(!removed)return "notfound";
            if(root is JsonObject obj)obj.Remove("checksum");
            string temp=file+".winlestia.tmp";
            File.WriteAllText(temp,root.ToJsonString(new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}),new UTF8Encoding(false));
            try{File.Replace(temp,file,null);}
            catch{try{File.Delete(temp);}catch{}throw;}
            return null;
        }catch(Exception ex){Log("Favoris suppression : "+ex.GetType().Name);return "error";}
    }

    internal static bool SyncActive(BrowserInfo b){
        try{
            string prefs=Path.Combine(b.ProfileDir,"Preferences");
            if(!File.Exists(prefs))return false;
            var root=JsonNode.Parse(ReadShared(prefs));
            if(root?["account_info"] is JsonArray accounts&&accounts.Count>0)return true;
            var sync=root?["sync"];
            if(sync is JsonObject s)foreach(var key in new[]{"has_setup_completed","requested","keep_everything_synced"})if(s[key] is JsonValue v&&v.TryGetValue<bool>(out var on)&&on)return true;
        }catch{return true;} // illisible : par prudence, on demande confirmation
        return false;
    }
}
