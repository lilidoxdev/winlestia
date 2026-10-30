// C#12 / CliCommands: parsing and allow-list of the `winlestia` command line (pure functions).
using System;using System.Collections.Generic;using PanneauxGlass;
class Test{
static bool P(string a,out CliCommand? c){return CliCommands.TryParse(a.Length==0?new string[0]:a.Split(' '),out c,out _);}
static int Main(){var r=new Dictionary<string,bool>();CliCommand? c;
r["emptyIsHelp"]=P("",out c)&&c!.Kind==CliKind.Help;
r["status"]=P("status",out c)&&c!.Kind==CliKind.Status;
r["statusCaseInsensitive"]=P("STATUS",out c)&&c!.Kind==CliKind.Status;
r["statusExtraArgRefused"]=!P("status now",out _);
r["paletteCommand"]=P("palette",out c)&&c!.Kind==CliKind.Palette;
foreach(var n in CliCommands.Panels)r["panel_"+n]=P("panel "+n,out c)&&c!.Kind==CliKind.Panel&&c.Arg==n;
r["panelUnknownRefused"]=!P("panel shutdown",out _);
r["panelMissingRefused"]=!P("panel",out _);
r["desktopNext"]=P("desktop next",out c)&&c!.Kind==CliKind.DesktopNext;
r["desktopPrev"]=P("desktop prev",out c)&&c!.Kind==CliKind.DesktopPrev;
r["desktopNew"]=P("desktop new",out c)&&c!.Kind==CliKind.DesktopNew;
r["desktop3"]=P("desktop 3",out c)&&c!.Kind==CliKind.DesktopGo&&c.Number==3;
r["desktop0Refused"]=!P("desktop 0",out _);
r["desktop99Refused"]=!P("desktop 99",out _);
r["desktopMinusRefused"]=!P("desktop -1",out _);
r["desktopCloseRefused"]=!P("desktop close 1",out _)&&!P("desktop close",out _);
foreach(var bad in new[]{"quit","exit","shutdown","restart","logoff","signout","delete","reboot","desktopClose","explorerRestore","emptyRecycleBin","recycleFiles","paletteRun","preference"})r["refused_"+bad]=!P(bad,out _);
r["findWindows"]=P("find windows",out c)&&c!.Kind==CliKind.FindWindows;
r["findWindowsExtraRefused"]=!P("find windows all",out _)&&!P("find",out _)&&!P("find windows2",out _);
r["findFiles"]=P("find files rapport",out c)&&c!.Kind==CliKind.FindFiles&&c.Arg=="rapport";
r["findFilesTwoWords"]=P("find files mon rapport",out c)&&c!.Kind==CliKind.FindFiles&&c.Arg=="mon rapport";
r["findFilesCase"]=P("FIND Files abc",out c)&&c!.Kind==CliKind.FindFiles;
r["findFilesShortRefused"]=!P("find files a",out _)&&!P("find files",out _);
r["findFilesLongRefused"]=!P("find files "+new string('x',65),out _);
r["findOtherRefused"]=!P("find folders abc",out _)&&!P("find everything abc",out _);
r["findHelpListed"]=CliCommands.HelpText.Contains("find windows")&&CliCommands.HelpText.Contains("find files");
r["tooManyArgsRefused"]=!P("desktop 1 2 3 4 5",out _);
r["nullIsHelp"]=CliCommands.TryParse(null,out c,out _)&&c!.Kind==CliKind.Help;
Func<string,bool> yes=_=>true,no=_=>false;
r["wallpaperOk"]=CliCommands.ValidWallpaper(@"C:\Pictures\a.PNG",yes,out _);
r["wallpaperMissing"]=!CliCommands.ValidWallpaper(@"C:\Pictures\a.png",no,out _);
r["wallpaperRelativeRefused"]=!CliCommands.ValidWallpaper(@"a.png",yes,out _);
r["wallpaperUncRefused"]=!CliCommands.ValidWallpaper(@"\\host\share\a.png",yes,out _);
r["wallpaperDeviceRefused"]=!CliCommands.ValidWallpaper(@"\\?\C:\a.png",yes,out _);
r["wallpaperExeRefused"]=!CliCommands.ValidWallpaper(@"C:\Windows\notepad.exe",yes,out _);
r["wallpaperStreamRefused"]=!CliCommands.ValidWallpaper(@"C:\a.txt:x.png",yes,out _);
r["wallpaperEmptyRefused"]=!CliCommands.ValidWallpaper("",yes,out _);
r["wallpaperTooLongRefused"]=!CliCommands.ValidWallpaper(@"C:\"+new string('a',300)+".png",yes,out _);
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)if(!k.Value)Console.WriteLine("FAIL "+k.Key);Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
