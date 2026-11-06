// C#12 / CliCommands: parsing and allow-list of the `winlestia` command line (pure functions).
using System;
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class CliCommandsTests
{
    static bool P(string a, out CliCommand? c)
    {
        return CliCommands.TryParse(a.Length == 0 ? new string[0] : a.Split(' '), out c, out _);
    }
    [Fact]
    public void AllChecks()
    {
        CliCommand? c;
        Assert.True(P("", out c) && c!.Kind == CliKind.Help, "emptyIsHelp");
        Assert.True(P("status", out c) && c!.Kind == CliKind.Status, "status");
        Assert.True(P("STATUS", out c) && c!.Kind == CliKind.Status, "statusCaseInsensitive");
        Assert.True(!P("status now", out _), "statusExtraArgRefused");
        Assert.True(P("palette", out c) && c!.Kind == CliKind.Palette, "paletteCommand");
        foreach (var n in CliCommands.Panels)
            Assert.True(P("panel " + n, out c) && c!.Kind == CliKind.Panel && c.Arg == n, "panel_" + n);
        Assert.True(!P("panel shutdown", out _), "panelUnknownRefused");
        Assert.True(!P("panel", out _), "panelMissingRefused");
        Assert.True(P("desktop next", out c) && c!.Kind == CliKind.DesktopNext, "desktopNext");
        Assert.True(P("desktop prev", out c) && c!.Kind == CliKind.DesktopPrev, "desktopPrev");
        Assert.True(P("desktop new", out c) && c!.Kind == CliKind.DesktopNew, "desktopNew");
        Assert.True(P("desktop 3", out c) && c!.Kind == CliKind.DesktopGo && c.Number == 3, "desktop3");
        Assert.True(!P("desktop 0", out _), "desktop0Refused");
        Assert.True(!P("desktop 99", out _), "desktop99Refused");
        Assert.True(!P("desktop -1", out _), "desktopMinusRefused");
        Assert.True(!P("desktop close 1", out _) && !P("desktop close", out _), "desktopCloseRefused");
        foreach (var bad in new[] { "quit", "exit", "shutdown", "restart", "logoff", "signout", "delete", "reboot", "desktopClose", "explorerRestore", "emptyRecycleBin", "recycleFiles", "paletteRun", "preference" })
            Assert.True(!P(bad, out _), "refused_" + bad);
        Assert.True(P("find windows", out c) && c!.Kind == CliKind.FindWindows, "findWindows");
        Assert.True(!P("find windows all", out _) && !P("find", out _) && !P("find windows2", out _), "findWindowsExtraRefused");
        Assert.True(P("find files rapport", out c) && c!.Kind == CliKind.FindFiles && c.Arg == "rapport", "findFiles");
        Assert.True(P("find files mon rapport", out c) && c!.Kind == CliKind.FindFiles && c.Arg == "mon rapport", "findFilesTwoWords");
        Assert.True(P("FIND Files abc", out c) && c!.Kind == CliKind.FindFiles, "findFilesCase");
        Assert.True(!P("find files a", out _) && !P("find files", out _), "findFilesShortRefused");
        Assert.True(!P("find files " + new string('x', 65), out _), "findFilesLongRefused");
        Assert.True(!P("find folders abc", out _) && !P("find everything abc", out _), "findOtherRefused");
        Assert.True(CliCommands.HelpText.Contains("find windows") && CliCommands.HelpText.Contains("find files"), "findHelpListed");
        Assert.True(P("brightness", out c) && c!.Kind == CliKind.Brightness, "brightnessList");
        Assert.True(P("brightness 2 40", out c) && c!.Kind == CliKind.BrightnessSet && c.Number == 2 && c.Value == 40, "brightnessSet");
        Assert.True(P("brightness 1 0", out c) && c!.Value == 0 && P("brightness 1 100", out c) && c!.Value == 100, "brightnessBounds");
        Assert.True(!P("brightness 1 101", out _) && !P("brightness 1 1000", out _), "brightnessOver100Refused");
        Assert.True(!P("brightness 1 -5", out _) && !P("brightness -1 5", out _), "brightnessNegativeRefused");
        Assert.True(!P("brightness 0 50", out _) && !P("brightness x 50", out _) && !P("brightness 100 50", out _), "brightnessIdRefused");
        Assert.True(!P("brightness 1 abc", out _) && !P("brightness 1 5.5", out _), "brightnessValueNotNumber");
        Assert.True(!P("brightness 1", out _) && !P("brightness 1 2 3", out _), "brightnessArityRefused");
        Assert.True(P("visualizer", out c) && c!.Kind == CliKind.Visualizer, "visualizerParses");
        Assert.True(P("VISUALIZER", out c) && c!.Kind == CliKind.Visualizer, "visualizerCaseInsensitive");
        Assert.True(!P("visualizer on", out _), "visualizerExtraArgRefused");
        Assert.True(CliCommands.HelpText.Contains("visualizer") && CliCommands.HelpText.Contains("frames"), "visualizerHelpListed");
        Assert.True(P("panel dashboard", out c) && c!.Kind == CliKind.Panel && c.Arg == "dashboard", "panelDashboardParses");
        Assert.True(CliCommands.HelpText.Contains("palette, dashboard"), "panelDashboardHelpListed");
        Assert.True(CliCommands.HelpText.Contains("brightness <id> <0-100>"), "brightnessHelpListed");
        Assert.True(!P("desktop 1 2 3 4 5", out _), "tooManyArgsRefused");
        Assert.True(CliCommands.TryParse(null, out c, out _) && c!.Kind == CliKind.Help, "nullIsHelp");
        Func<string, bool> yes = _ => true, no = _ => false;
        Assert.True(CliCommands.ValidWallpaper(@"C:\Pictures\a.PNG", yes, out _), "wallpaperOk");
        Assert.True(!CliCommands.ValidWallpaper(@"C:\Pictures\a.png", no, out _), "wallpaperMissing");
        Assert.True(!CliCommands.ValidWallpaper(@"a.png", yes, out _), "wallpaperRelativeRefused");
        Assert.True(!CliCommands.ValidWallpaper(@"\\host\share\a.png", yes, out _), "wallpaperUncRefused");
        Assert.True(!CliCommands.ValidWallpaper(@"\\?\C:\a.png", yes, out _), "wallpaperDeviceRefused");
        Assert.True(!CliCommands.ValidWallpaper(@"C:\Windows\notepad.exe", yes, out _), "wallpaperExeRefused");
        Assert.True(!CliCommands.ValidWallpaper(@"C:\a.txt:x.png", yes, out _), "wallpaperStreamRefused");
        Assert.True(!CliCommands.ValidWallpaper("", yes, out _), "wallpaperEmptyRefused");
        Assert.True(!CliCommands.ValidWallpaper(@"C:\" + new string('a', 300) + ".png", yes, out _), "wallpaperTooLongRefused");
        Assert.True(!CliCommands.TryParse("wallpaper C:\a.png --display".Split(' '), out _, out var e1) && e1.Contains("needs a display id"), "displayNoValueRefused");
        Assert.True(!CliCommands.TryParse("wallpaper C:\a.png --display x".Split(' '), out _, out var e2) && e2.Contains("number"), "displayNonNumericRefused");
        Assert.True(!CliCommands.TryParse("wallpaper --display 2 C:\a.png".Split(' '), out _, out var e3) && e3.Contains("after the image path"), "displayBeforePathRefused");
        Assert.True(!CliCommands.TryParse("wallpaper --display".Split(' '), out _, out var e4) && e4.Contains("--display"), "displayOnlyRefused");
        Assert.True(P("wallpaper list", out c) && c!.Kind == CliKind.WallpaperList, "wallpaperListParses");
        Assert.True(P("wallpaper C:\a.png", out c) && c!.Kind == CliKind.Wallpaper && c.Number == 0 && c.Arg == @"C:.png", "wallpaperPathAllDisplays");
        Assert.True(CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--display", "2" }, out c, out _) && c!.Kind == CliKind.Wallpaper && c.Number == 2 && c.Arg == @"C:.png", "wallpaperDisplayParses");
        Assert.True(!CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--display", "0" }, out _, out _), "wallpaperDisplayZeroRefused");
        Assert.True(!CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--display", "x" }, out _, out _), "wallpaperDisplayWordRefused");
        Assert.True(!CliCommands.TryParse(new[] { "wallpaper", @"C:.png", "--screen", "1" }, out _, out _), "wallpaperDisplayFlagTypoRefused");
        Assert.True(CliCommands.HelpText.Contains("wallpaper list") && CliCommands.HelpText.Contains("--display"), "wallpaperListHelpListed");
        Assert.True(WallpaperLogic.Animated(new string?[] { "explorer", "chrome", null, "" }).Length == 0, "animatedNone");
        Assert.True(WallpaperLogic.Animated(new string?[] { "Wallpaper64" }).SequenceEqual(new[] { "Wallpaper Engine" }), "animatedEngine");
        Assert.True(WallpaperLogic.Animated(new string?[] { "LIVELY.EXE", "wallpaper.service.exe" }).SequenceEqual(new[] { "Lively Wallpaper", "Wallpaper X" }), "animatedExeSuffixAndCase");
        Assert.True(WallpaperLogic.Animated(new string?[] { "DesktopHut Studio", "DesktopHut Studio", "wallpaper32" }).SequenceEqual(new[] { "DesktopHut", "Wallpaper Engine" }), "animatedDesktopHutDeduped");
        Assert.True(WallpaperLogic.Animated(new string?[] { "mylively", "wallpaper" }).Length == 0, "animatedNoPartialMatch");
        Assert.True(WallpaperLogic.DisplayName(1, 1920, 1080, true) == "Display 1 (1920x1080) primary", "displayNamePrimary");
        Assert.True(P("quiet", out c) && c!.Kind == CliKind.Quiet && c.Arg == "", "quietParses");
        Assert.True(P("quiet on", out c) && c!.Kind == CliKind.Quiet && c.Arg == "on", "quietOn");
        Assert.True(P("QUIET OFF", out c) && c!.Kind == CliKind.Quiet && c.Arg == "off", "quietOffCaseInsensitive");
        Assert.True(P("quiet auto", out c) && c!.Kind == CliKind.Quiet && c.Arg == "auto", "quietAuto");
        Assert.True(!P("quiet maybe", out _) && !P("quiet on off", out _), "quietBadArgRefused");
        Assert.True(CliCommands.HelpText.Contains("quiet on|off|auto") && CliCommands.HelpText.Contains("not saved"), "quietHelpListed");
        Assert.True(QuietPolicy.Covers(0, 0, 1920, 1080, 0, 0, 1920, 1080), "quietCoversExact");
        Assert.True(QuietPolicy.Covers(0, 0, 3440, 1439, 0, 0, 3440, 1440), "quietCoversSlack");
        Assert.True(QuietPolicy.Covers(-8, -8, 1928, 1088, 0, 0, 1920, 1080), "quietCoversOvershoot");
        Assert.True(!QuietPolicy.Covers(0, 0, 1920, 1040, 0, 0, 1920, 1080), "quietNotCoversMaximized");
        Assert.True(!QuietPolicy.Covers(0, 0, 1920, 1080, 1920, 0, 3840, 1080), "quietNotCoversSecondMonitor");
        Assert.True(!QuietPolicy.Covers(0, 0, 10, 10, 5, 5, 5, 5), "quietNotCoversEmptyMonitor");
        Assert.True(QuietPolicy.Effective(QuietForce.On, false), "quietForcedOnWins");
        Assert.True(!QuietPolicy.Effective(QuietForce.Off, true), "quietForcedOffWins");
        Assert.True(QuietPolicy.Effective(QuietForce.Auto, true) && !QuietPolicy.Effective(QuietForce.Auto, false), "quietAutoFollows");
        Assert.True(!QuietPolicy.Detected(false, 1) && !QuietPolicy.Detected(true, 0) && QuietPolicy.Detected(true, 2), "quietDetectedNeedsPref");
        Assert.True(QuietPolicy.TryParse("ON", out var qf) && qf == QuietForce.On && !QuietPolicy.TryParse("x", out _) && QuietPolicy.Label(QuietForce.Auto) == "auto", "quietParseForce");
    }
}
