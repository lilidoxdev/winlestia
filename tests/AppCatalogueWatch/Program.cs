// C#12 / AppCatalogueWatchPolicy: shortcut filter, debounce, refresh gate, palette staleness, window grouping, icon retry, inventory naming rules.
using System;
using System.Collections.Generic;
using PanneauxGlass;
using PanneauxGlass.Platform;

class Test
{
    static int Main()
    {
        var r = new Dictionary<string, bool>();
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r["lnkRelevant"] = CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\App.lnk");
        r["urlRelevant"] = CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\App.URL");
        r["folderRelevant"] = CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\New folder");
        r["tmpIgnored"] =
            !CatalogueWatchRules.IsShortcutEvent(@"C:\SM\Programs\a.tmp") && !CatalogueWatchRules.IsShortcutEvent(null);
        var d = new BurstDebounce();
        r["idleHasNoDeadline"] = d.Remaining(t) == null && !d.Active;
        d.Touch(t);
        r["notDueBefore3s"] = d.Remaining(t.AddMilliseconds(2900)) is TimeSpan a && a > TimeSpan.Zero;
        r["dueAt3s"] = d.Remaining(t.AddSeconds(3)) == TimeSpan.Zero;
        d.Touch(t.AddSeconds(2));
        r["burstPushesDeadline"] =
            d.Remaining(t.AddSeconds(3)) is TimeSpan b
            && b > TimeSpan.Zero
            && d.Remaining(t.AddSeconds(5)) == TimeSpan.Zero;
        d.Reset();
        r["resetClears"] = !d.Active;
        var g = new RefreshGate();
        r["firstRequestStarts"] = g.Request();
        r["secondWhileBusyRefused"] = !g.Request();
        r["completeReplaysOnce"] = g.Complete() && g.Request() && !g.Complete();
        r["idleCompleteNoReplay"] = !new RefreshGate().Complete();
        r["paletteFreshNoRefresh"] = !CatalogueWatchRules.PaletteNeedsRefresh(t.AddMinutes(-4), null, t);
        r["paletteStaleRefresh"] = CatalogueWatchRules.PaletteNeedsRefresh(t.AddMinutes(-5), null, t);
        r["paletteMissingRefresh"] = CatalogueWatchRules.PaletteNeedsRefresh(null, null, t);
        r["paletteRecentAttemptBlocks"] =
            !CatalogueWatchRules.PaletteNeedsRefresh(t.AddHours(-1), t.AddSeconds(-30), t)
            && CatalogueWatchRules.PaletteNeedsRefresh(t.AddHours(-1), t.AddSeconds(-61), t);
        r["emptyPathGetsPseudo"] =
            CatalogueWatchRules.WindowGroupKey("", 42) == "hwnd:42" && CatalogueWatchRules.IsPseudoPath("hwnd:42");
        r["realPathKept"] =
            CatalogueWatchRules.WindowGroupKey(@"C:\a\b.exe", 42) == @"C:\a\b.exe"
            && !CatalogueWatchRules.IsPseudoPath(@"C:\a\b.exe");
        r["twoElevatedWindowsDistinct"] =
            CatalogueWatchRules.WindowGroupKey(null, 1) != CatalogueWatchRules.WindowGroupKey(null, 2);
        r["iconRetryBounded"] =
            CatalogueWatchRules.IconRetryDelay(1) == TimeSpan.FromSeconds(5)
            && CatalogueWatchRules.IconRetryDelay(5) == null
            && CatalogueWatchRules.IconRetryDelay(0) == null;
        r["iconRetryNotDueEarly"] =
            !CatalogueWatchRules.IconRetryDue(1, t, t.AddSeconds(2))
            && CatalogueWatchRules.IconRetryDue(1, t, t.AddSeconds(5));
        r["iconRetryStopsAfterMax"] = !CatalogueWatchRules.IconRetryDue(5, t, t.AddDays(1));
        r["cleanNameRaccourci"] =
            CatalogueWatchRules.CleanName("Lancer Foo.exe - Raccourci") == "Foo"
            && CatalogueWatchRules.CleanName("Bar™") == "Bar"
            && CatalogueWatchRules.CleanName("crimsondesert") == "Crimson Desert";
        r["excludedNames"] =
            CatalogueWatchRules.IsExcludedName("Foo Uninstall")
            && CatalogueWatchRules.IsExcludedName("Panneaux Glass")
            && CatalogueWatchRules.IsExcludedName("Mises à jour Foo")
            && !CatalogueWatchRules.IsExcludedName("Foo Studio");
        r["idApp"] = CatalogueWatchRules.ShortcutId("Foo", @"C:\A\Foo.exe", "-x") == @"app:c:\a\foo.exe|-x";
        r["idNamedSquirrel"] =
            CatalogueWatchRules.ShortcutId("Wand (WeMod)", @"C:\A\u.exe", "") == "named:wand"
            && CatalogueWatchRules.ShortcutId("Discord", @"C:\A\u.exe", "") == "named:discord";
        r["idNoTarget"] = CatalogueWatchRules.ShortcutId("Foo", "", "") == "name:foo";
        r["iconLocationThenTarget"] =
            CatalogueWatchRules.IconSource(@"C:\A\foo.ico,0", @"C:\A\foo.exe", p => p == @"C:\A\foo.ico")
                == @"C:\A\foo.ico"
            && CatalogueWatchRules.IconSource(@"C:\gone\x.ico,2", @"C:\A\foo.exe", p => false) == @"C:\A\foo.exe"
            && CatalogueWatchRules.IconSource(",0", @"C:\A\foo.exe", p => true) == @"C:\A\foo.exe";
        r["dllIconPrefersTarget"] =
            CatalogueWatchRules.PreferTargetForIcon(@"C:\W\shell32.dll")
            && !CatalogueWatchRules.PreferTargetForIcon(@"C:\A\foo.exe");
        var th = new RefreshThrottle();
        r["throttleFirstImmediate"] = th.Delay(t) == TimeSpan.Zero;
        th.Ran(t);
        r["throttleWaitsGap"] =
            th.Delay(t.AddSeconds(20)) == TimeSpan.FromSeconds(40) && th.Delay(t.AddMinutes(1)) == TimeSpan.Zero;
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            if (!k.Value)
                Console.WriteLine(k.Key + "=false");
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
