// C#12 / AppCatalogueWatchPolicy: shortcut filter, debounce, refresh gate, palette staleness, window grouping, icon retry, inventory naming
// rules.
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class AppCatalogueWatchPolicyTests
{
    [Fact]
    public void AllChecks()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\App.lnk"), "lnkRelevant");
        Assert.True(CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\App.URL"), "urlRelevant");
        Assert.True(CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\New folder"), "folderRelevant");
        Assert.True(!CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\a.tmp") && !CatalogueWatchRules.IsShortcutEvent(null),
            "tmpIgnored");
        var d = new BurstDebounce();
        Assert.True(d.Remaining(t) == null && !d.Active, "idleHasNoDeadline");
        d.Touch(t);
        Assert.True(d.Remaining(t.AddMilliseconds(2900)) is TimeSpan a && a > TimeSpan.Zero, "notDueBefore3s");
        Assert.True(d.Remaining(t.AddSeconds(3)) == TimeSpan.Zero, "dueAt3s");
        d.Touch(t.AddSeconds(2));
        Assert.True(d.Remaining(t.AddSeconds(3)) is TimeSpan b && b > TimeSpan.Zero && d.Remaining(t.AddSeconds(5)) == TimeSpan.Zero,
            "burstPushesDeadline");
        d.Reset();
        Assert.True(!d.Active, "resetClears");
        var g = new RefreshGate();
        Assert.True(g.Request(), "firstRequestStarts");
        Assert.True(!g.Request(), "secondWhileBusyRefused");
        Assert.True(g.Complete() && g.Request() && !g.Complete(), "completeReplaysOnce");
        Assert.True(!new RefreshGate().Complete(), "idleCompleteNoReplay");
        Assert.True(!CatalogueWatchRules.PaletteNeedsRefresh(t.AddMinutes(-4), null, t), "paletteFreshNoRefresh");
        Assert.True(CatalogueWatchRules.PaletteNeedsRefresh(t.AddMinutes(-5), null, t), "paletteStaleRefresh");
        Assert.True(CatalogueWatchRules.PaletteNeedsRefresh(null, null, t), "paletteMissingRefresh");
        Assert.True(!CatalogueWatchRules.PaletteNeedsRefresh(t.AddHours(-1), t.AddSeconds(-30), t)
            && CatalogueWatchRules.PaletteNeedsRefresh(t.AddHours(-1), t.AddSeconds(-61), t), "paletteRecentAttemptBlocks");
        Assert.True(CatalogueWatchRules.WindowGroupKey("", 42) == "hwnd:42" && CatalogueWatchRules.IsPseudoPath("hwnd:42"),
            "emptyPathGetsPseudo");
        Assert.True(CatalogueWatchRules.WindowGroupKey(@"C:\a\b.exe", 42) == @"C:\a\b.exe"
            && !CatalogueWatchRules.IsPseudoPath(@"C:\a\b.exe"), "realPathKept");
        Assert.True(CatalogueWatchRules.WindowGroupKey(null, 1) != CatalogueWatchRules.WindowGroupKey(null, 2),
            "twoElevatedWindowsDistinct");
        Assert.True(CatalogueWatchRules.IconRetryDelay(1) == TimeSpan.FromSeconds(5) && CatalogueWatchRules.IconRetryDelay(5) == null
            && CatalogueWatchRules.IconRetryDelay(0) == null, "iconRetryBounded");
        Assert.True(!CatalogueWatchRules.IconRetryDue(1, t, t.AddSeconds(2)) && CatalogueWatchRules.IconRetryDue(1, t, t.AddSeconds(5)),
            "iconRetryNotDueEarly");
        Assert.True(!CatalogueWatchRules.IconRetryDue(5, t, t.AddDays(1)), "iconRetryStopsAfterMax");
        Assert.True(CatalogueWatchRules.CleanName("Lancer Foo.exe - Raccourci") == "Foo" && CatalogueWatchRules.CleanName("Bar™") == "Bar"
            && CatalogueWatchRules.CleanName("crimsondesert") == "Crimson Desert", "cleanNameRaccourci");
        Assert.True(CatalogueWatchRules.IsExcludedName("Foo Uninstall") && CatalogueWatchRules.IsExcludedName("Panneaux Glass")
            && CatalogueWatchRules.IsExcludedName("Mises à jour Foo") && !CatalogueWatchRules.IsExcludedName("Foo Studio"),
            "excludedNames");
        Assert.True(CatalogueWatchRules.ShortcutId("Foo", @"C:\A\Foo.exe", "-x") == @"app:c:\a\foo.exe|-x", "idApp");
        Assert.True(CatalogueWatchRules.ShortcutId("Wand (WeMod)", @"C:\A\u.exe", "") == "named:wand"
            && CatalogueWatchRules.ShortcutId("Discord", @"C:\A\u.exe", "") == "named:discord", "idNamedSquirrel");
        Assert.True(CatalogueWatchRules.ShortcutId("Foo", "", "") == "name:foo", "idNoTarget");
        Assert.True(CatalogueWatchRules.IconSource(@"C:\A\foo.ico,0", @"C:\A\foo.exe", p => p == @"C:\A\foo.ico") == @"C:\A\foo.ico"
            && CatalogueWatchRules.IconSource(@"C:\gone\x.ico,2", @"C:\A\foo.exe", p => false) == @"C:\A\foo.exe"
            && CatalogueWatchRules.IconSource(",0", @"C:\A\foo.exe", p => true) == @"C:\A\foo.exe", "iconLocationThenTarget");
        Assert.True(CatalogueWatchRules.PreferTargetForIcon(@"C:\W\shell32.dll")
            && !CatalogueWatchRules.PreferTargetForIcon(@"C:\A\foo.exe"), "dllIconPrefersTarget");
        var th = new RefreshThrottle();
        Assert.True(th.Delay(t) == TimeSpan.Zero, "throttleFirstImmediate");
        th.Ran(t);
        Assert.True(th.Delay(t.AddSeconds(20)) == TimeSpan.FromSeconds(40) && th.Delay(t.AddMinutes(1)) == TimeSpan.Zero,
            "throttleWaitsGap");
    }
}
