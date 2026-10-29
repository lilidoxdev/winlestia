// C#12 / FilesRouter fixture / only freshly created temporary files and folders are touched.
using System;using System.Collections.Generic;using System.IO;using System.Text.Json;using WinlestiaFiles;
class Test{
static int Main(){var results=new Dictionary<string,bool>();string root=Path.Combine(Path.GetTempPath(),"WinlestiaFiles-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
string dir=Path.Combine(root,"dossier");Directory.CreateDirectory(dir);string file=Path.Combine(root,"a.txt");File.WriteAllText(file,"x");string zip=Path.Combine(root,"b.zip");File.WriteAllText(zip,"x");
string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
bool IsExplorer(string a,string? expected=null){var r=PathRouter.Route(a);return !r.IsOwn&&r.Value==(expected??a);}
bool IsOwn(string? a,string expected){var r=PathRouter.Route(a);return r.IsOwn&&r.Value==expected;}
results["shellPrefixGoesToExplorer"]=IsExplorer("shell:Downloads");
results["shellPrefixIsCaseInsensitive"]=IsExplorer("SHELL:Downloads");
results["shellGuidGoesToExplorer"]=IsExplorer("::{645FF040-5081-101B-9F08-00AA002F954E}");
results["uncGoesToExplorer"]=IsExplorer(@"\\server\share");
results["extendedPrefixGoesToExplorer"]=IsExplorer(@"\\?\C:\");
results["existingFileGoesToExplorer"]=IsExplorer(file);
results["zipGoesToExplorer"]=IsExplorer(zip);
results["missingZipGoesToExplorer"]=IsExplorer(Path.Combine(root,"absent.zip"));
results["nonexistentFolderGoesToExplorer"]=IsExplorer(Path.Combine(root,"absent"));
results["existingFolderIsOwn"]=IsOwn(dir,dir);
results["driveLetterGetsRootSlash"]=IsOwn("C:",@"C:\");
results["driveRootStaysRoot"]=IsOwn(@"C:\",@"C:\");
results["quotedPathIsOwn"]=IsOwn("\""+dir+"\"",dir);
results["trailingSlashIsRemoved"]=IsOwn(dir+@"\",dir);
results["forwardSlashIsNormalized"]=IsOwn(dir.Replace('\\','/')+"/",dir);
results["emptyIsUserProfile"]=IsOwn("",profile);
results["nullIsUserProfile"]=IsOwn(null,profile);
results["originalArgIsKeptForExplorer"]=IsExplorer("\"shell:Downloads\"");
// Drive / share roots arrive with a dangling quote ("<exe>" "%1" with %1 = C:\)
results["rootCQuote"]=IsOwn("C:\"",@"C:\");
results["rootDQuoteCleaned"]=PathRouter.Clean("D:\"")==@"D:\";
results["rootUnusedDriveGoesToExplorerNormalised"]=Directory.Exists(@"Q:\")||IsExplorer("Q:\"",@"Q:\");
results["uncRootQuoteGetsBackslash"]=IsExplorer("\\\\srv\\share\"",@"\\srv\share\");
results["uncRootCleanDirect"]=PathRouter.Clean("\\\\srv\\share\"")==@"\\srv\share\"&&PathRouter.Clean(@"\\srv\share\sub\")==@"\\srv\share\sub\";
results["programFilesDanglingQuote"]=Directory.Exists(@"C:\Program Files")&&IsOwn("C:\\Program Files\"",@"C:\Program Files");
results["normalPathUnchanged"]=PathRouter.Clean(@"C:\Users\x")==@"C:\Users\x"&&PathRouter.Clean("\"C:\\a b\"")=="\"C:\\a b\""&&PathRouter.Clean(null)=="";
results["onlyOneQuoteStripped"]=PathRouter.Clean("a\"\"")=="a\"";
results["normalFolderStillOwn"]=IsOwn(dir,dir);
}finally{try{Directory.Delete(root,true);}catch{}}
int ok=0;foreach(var v in results.Values)if(v)ok++;foreach(var p in results)Console.WriteLine(p.Key+"="+(p.Value?"true":"false"));
Console.WriteLine("TOTAL "+ok+"/"+results.Count);return ok==results.Count?0:1;
}}
