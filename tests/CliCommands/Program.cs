// C#12 / CliCommands: parsing and allow-list of the `winlestia` command line (pure functions).
using System;
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
class Test
{
    static bool P(string a, out CliCommand? c)
    {
        return CliCommands.TryParse(a.Length == 0 ? new string[0] : a.Split(' '), out c, out _);
    }
    static int Main()
    {
        var r = new Dictionary<string, bool>();
        CliCommand? c;
        r["emptyIsHelp"] = P("", out c) && c!.Kind == CliKind.Help;
        r["status"] = P("status", out c) && c!.Kind == CliKind.Status;
        r["statusCaseInsensitive"] = P("STATUS", out c) && c!.Kind == CliKind.Status;
        r["statusExtraArgRefused"] = !P("status now", out _);
        r["paletteCommand"] = P("palette", out c) && c!.Kind == CliKind.Palette;
        foreach (var n in CliCommands.Panels)
            r["panel_" + n] = P("panel " + n, out c) && c!.Kind == CliKind.Panel && c.Arg == n;
        r["panelUnknownRefused"] = !P("panel shutdown", out _);
        r["panelMissingRefused"] = !P("panel", out _);
        r["desktopNext"] = P("desktop next", out c) && c!.Kind == CliKind.DesktopNext;
        r["desktopPrev"] = P("desktop prev", out c) && c!.Kind == CliKind.DesktopPrev;
        r["desktopNew"] = P("desktop new", out c) && c!.Kind == CliKind.DesktopNew;
        r["desktop3"] = P("desktop 3", out c) && c!.Kind == CliKind.DesktopGo && c.Number == 3;
        r["desktop0Refused"] = !P("desktop 0", out _);
        r["desktop99Refused"] = !P("desktop 99", out _);
        r["desktopMinusRefused"] = !P("desktop -1", out _);
        r["desktopCloseRefused"] = !P("desktop close 1", out _) && !P("desktop close", out _);
        foreach (var bad in new[] { "quit", "exit", "shutdown", "restart", "logoff", "signout", "delete", "reboot", "desktopClose", "explorerRestore", "emptyRecycleBin", "recycleFiles", "paletteRun", "preference" })
            r["refused_" + bad] = !P(bad, out _);
        r["findWindows"] = P("find windows", out c) && c!.Kind == CliKind.FindWindows;
        r["findWindowsExtraRefused"] = !P("find windows all", out _) && !P("find", out _) && !P("find windows2", out _);
        r["findFiles"] = P("find files rapport", out c) && c!.Kind == CliKind.FindFiles && c.Arg == "rapport";
        r["findFilesTwoWords"] = P("find files mon rapport", out c) && c!.Kind == CliKind.FindFiles && c.Arg == "mon rapport";
        r["findFilesCase"] = P("FIND Files abc", out c) && c!.Kind == CliKind.FindFiles;
        r["findFilesShortRefused"] = !P("find files a", out _) && !P("find files", out _);
        r["findFilesLongRefused"] = !P("find files " + new string('x', 65), out _);
        r["findOtherRefused"] = !P("find folders abc", out _) && !P("find everything abc", out _);
        r["findHelpListed"] = CliCommands.HelpText.Contains("find windows") && CliCommands.HelpText.Contains("find files");
        r["brightnessList"] = P("brightness", out c) && c!.Kind == CliKind.Brightness;
        r["brightnessSet"] = P("brightness 2 40", out c) && c!.Kind == CliKind.BrightnessSet && c.Number == 2 && c.Value == 40;
        r["brightnessBounds"] = P("brightness 1 0", out c) && c!.Value == 0 && P("brightness 1 100", out c) && c!.Value == 100;
        r["brightnessOver100Refused"] = !P("brightness 1 101", out _) && !P("brightness 1 1000", out _);
        r["brightnessNegativeRefused"] = !P("brightness 1 -5", out _) && !P("brightness -1 5", out _);
        r["brightnessIdRefused"] = !P("brightness 0 50", out _) && !P("brightness x 50", out _) && !P("brightness 100 50", out _);
        r["brightnessValueNotNumber"] = !P("brightness 1 abc", out _) && !P("brightness 1 5.5", out _);
        r["brightnessArityRefused"] = !P("brightness 1", out _) && !P("brightness 1 2 3", out _);
        r["visualizerParses"] = P("visualizer", out c) && c!.Kind == CliKind.Visualizer;
        r["visualizerCaseInsensitive"] = P("VISUALIZER", out c) && c!.Kind == CliKind.Visualizer;
        r["visualizerExtraArgRefused"] = !P("visualizer on", out _);
        r["visualizerHelpListed"] = CliCommands.HelpText.Contains("visualizer") && CliCommands.HelpText.Contains("frames");
        r["panelDashboardParses"] = P("panel dashboard", out c) && c!.Kind == CliKind.Panel && c.Arg == "dashboard";
        r["panelDashboardHelpListed"] = CliCommands.HelpText.Contains("palette, dashboard");
        r["brightnessHelpListed"] = CliCommands.HelpText.Contains("brightness <id> <0-100>");
        r["tooManyArgsRefused"] = !P("desktop 1 2 3 4 5", out _);
        r["nullIsHelp"] = CliCommands.TryParse(null, out c, out _) && c!.Kind == CliKind.Help;
        Func<string, bool> yes = _ => true, no = _ => false;
        r["wallpaperOk"] = CliCommands.ValidWallpaper(@"C:\Pictures\a.PNG", yes, out _);
        r["wallpaperMissing"] = !CliCommands.ValidWallpaper(@"C:\Pictures\a.png", no, out _);
        r["wallpaperRelativeRefused"] = !CliCommands.ValidWallpaper(@"a.png", yes, out _);
        r["wallpaperUncRefused"] = !CliCommands.ValidWallpaper(@"\\host\share\a.png", yes, out _);
        r["wallpaperDeviceRefused"] = !CliCommands.ValidWallpaper(@"\\?\C:\a.png", yes, out _);
        r["wallpaperExeRefused"] = !CliCommands.ValidWallpaper(@"C:\Windows\notepad.exe", yes, out _);
        r["wallpaperStreamRefused"] = !CliCommands.ValidWallpaper(@"C:\a.txt:x.png", yes, out _);
        r["wallpaperEmptyRefused"] = !CliCommands.ValidWallpaper("", yes, out _);
        r["wallpaperTooLongRefused"] = !CliCommands.ValidWallpaper(@"C:\" + new string('a', 300) + ".png", yes, out _);
        r["displayNoValueRefused"] = !CliCommands.TryParse("wallpaper C:\a.png --display".Split(' '), out _, out var e1) && e1.Contains("needs a display id");
        r["displayNonNumericRefused"] = !CliCommands.TryParse("wallpaper C:\a.png --display x".Split(' '), out _, out var e2) && e2.Contains("number");
        r["displayBeforePathRefused"] = !CliCommands.TryParse("wallpaper --display 2 C:\a.png".Split(' '), out _, out var e3) && e3.Contains("after the image path");
        r["displayOnlyRefused"] = !CliCommands.TryParse("wallpaper --display".Split(' '), out _, out var e4) && e4.Contains("--display");
        r["wallpaperListParses"] = P("wallpaper list", out c) && c!.Kind == CliKind.WallpaperList;
        r["wallpaperPathAllDisplays"] = P("wallpaper C:\a.png", out c) && c!.Kind == CliKind.Wallpaper && c.Number == 0 && c.Arg == @"C:.png";
        r["wallpaperDisplayParses"] = CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--display", "2" }, out c, out _) && c!.Kind == CliKind.Wallpaper && c.Number == 2 && c.Arg == @"C:.png";
        r["wallpaperDisplayZeroRefused"] = !CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--display", "0" }, out _, out _);
        r["wallpaperDisplayWordRefused"] = !CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--display", "x" }, out _, out _);
        r["wallpaperDisplayFlagTypoRefused"] = !CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--screen", "1" }, out _, out _);
        r["wallpaperListHelpListed"] = CliCommands.HelpText.Contains("wallpaper list") && CliCommands.HelpText.Contains("--display");
        r["animatedNone"] = WallpaperLogic.Animated(new string?[] { "explorer", "chrome", null, "" }).Length == 0;
        r["animatedEngine"] = WallpaperLogic.Animated(new string?[] { "Wallpaper64" }).SequenceEqual(new[] { "Wallpaper Engine" });
        r["animatedExeSuffixAndCase"] = WallpaperLogic.Animated(new string?[] { "LIVELY.EXE", "wallpaper.service.exe" }).SequenceEqual(new[] { "Lively Wallpaper", "Wallpaper X" });
        r["animatedDesktopHutDeduped"] = WallpaperLogic.Animated(new string?[] { "DesktopHut Studio", "DesktopHut Studio", "wallpaper32" }).SequenceEqual(new[] { "DesktopHut", "Wallpaper Engine" });
        r["animatedNoPartialMatch"] = WallpaperLogic.Animated(new string?[] { "mylively", "wallpaper" }).Length == 0;
        r["displayNamePrimary"] = WallpaperLogic.DisplayName(1, 1920, 1080, true) == "Display 1 (1920x1080) primary";
        r["quietParses"] = P("quiet", out c) && c!.Kind == CliKind.Quiet && c.Arg == "";
        r["quietOn"] = P("quiet on", out c) && c!.Kind == CliKind.Quiet && c.Arg == "on";
        r["quietOffCaseInsensitive"] = P("QUIET OFF", out c) && c!.Kind == CliKind.Quiet && c.Arg == "off";
        r["quietAuto"] = P("quiet auto", out c) && c!.Kind == CliKind.Quiet && c.Arg == "auto";
        r["quietBadArgRefused"] = !P("quiet maybe", out _) && !P("quiet on off", out _);
        r["quietHelpListed"] = CliCommands.HelpText.Contains("quiet on|off|auto") && CliCommands.HelpText.Contains("not saved");
        r["quietCoversExact"] = QuietPolicy.Covers(0, 0, 1920, 1080, 0, 0, 1920, 1080);
        r["quietCoversSlack"] = QuietPolicy.Covers(0, 0, 3440, 1439, 0, 0, 3440, 1440);
        r["quietCoversOvershoot"] = QuietPolicy.Covers(-8, -8, 1928, 1088, 0, 0, 1920, 1080);
        r["quietNotCoversMaximized"] = !QuietPolicy.Covers(0, 0, 1920, 1040, 0, 0, 1920, 1080);
        r["quietNotCoversSecondMonitor"] = !QuietPolicy.Covers(0, 0, 1920, 1080, 1920, 0, 3840, 1080);
        r["quietNotCoversEmptyMonitor"] = !QuietPolicy.Covers(0, 0, 10, 10, 5, 5, 5, 5);
        r["quietForcedOnWins"] = QuietPolicy.Effective(QuietForce.On, false);
        r["quietForcedOffWins"] = !QuietPolicy.Effective(QuietForce.Off, true);
        r["quietAutoFollows"] = QuietPolicy.Effective(QuietForce.Auto, true) && !QuietPolicy.Effective(QuietForce.Auto, false);
        r["quietDetectedNeedsPref"] = !QuietPolicy.Detected(false, 1) && !QuietPolicy.Detected(true, 0) && QuietPolicy.Detected(true, 2);
        r["quietParseForce"] = QuietPolicy.TryParse("ON", out var qf) && qf == QuietForce.On && !QuietPolicy.TryParse("x", out _) && QuietPolicy.Label(QuietForce.Auto) == "auto";
        int ok = 0;
        foreach (var v in r.Values)
        if (v)
            ok++;
        foreach (var k in r)
        if (!k.Value)
            Console.WriteLine("FAIL " + k.Key);
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
