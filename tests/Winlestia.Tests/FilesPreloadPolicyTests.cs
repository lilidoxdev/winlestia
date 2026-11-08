// C#12 / FilesPreloadPolicy: pure restart decisions of the resident Winlestia Fichiers (no process, no backend).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class FilesPreloadPolicyTests
{
    [Fact]
    public void AllChecks()
    {
        const int M = FilesPreloadPolicy.MinRecycleIntervalMs;
        FilesPreloadPolicy P() => new FilesPreloadPolicy();
        Assert.True(P().OnExit(75, 1000, false, true) == PreloadAction.Recycle, "recycleExitRelaunches");
        {
            var p = P();
            p.OnExit(75, 0, false, true);
            Assert.True(p.OnExit(75, M - 1, false, true) == PreloadAction.StopTooFast, "secondRecycleUnder60sStops");
        }
        {
            var p = P();
            p.OnExit(75, 0, false, true);
            Assert.True(p.OnExit(75, M, false, true) == PreloadAction.Recycle, "secondRecycleAt60sRelaunches");
        }
        {
            var p = P();
            bool g = true;
            long t = 0;
            for (int i = 0; i < 50; i++)
            {
                g &= p.OnExit(75, t, false, true) == PreloadAction.Recycle;
                t += M + 1;
            }
            Assert.True(g, "recyclesAreUnlimitedWhenSpaced");
        }
        {
            var p = P();
            p.OnExit(75, 0, false, true);
            p.OnExit(75, 10, false, true);
            Assert.True(p.OnExit(75, 10 + M, false, true) == PreloadAction.Recycle, "afterStopTheClockStillAdvances");
        }
        Assert.True(P().OnExit(75, 0, true, true) == PreloadAction.None && P().OnExit(1, 0, true, true) == PreloadAction.None,
            "quittingNeverRelaunches");
        Assert.True(P().OnExit(75, 0, false, false) == PreloadAction.None && P().OnExit(1, 0, false, false) == PreloadAction.None,
            "prefOffNeverRelaunches");
        Assert.True(P().OnExit(1, 0, false, true) == PreloadAction.Crash, "crashRestartsOnce");
        {
            var p = P();
            p.Starts = 2;
            Assert.True(p.OnExit(1, 0, false, true) == PreloadAction.None && p.OnExit(0, 0, false, true) == PreloadAction.None,
                "crashLimitReached");
        }
        {
            var p = P();
            p.Starts = 2;
            Assert.True(p.OnExit(75, 0, false, true) == PreloadAction.Recycle && p.MayLaunch(true, true, false)
                && !p.MayLaunch(false, true, false), "recycleNotCountedAgainstCrashLimit");
        }
        {
            var p = P();
            p.Starts = 1;
            p.OnExit(75, 0, false, true);
            Assert.True(p.Starts == 1 && p.OnExit(1, 5, false, true) == PreloadAction.Crash, "recycleLeavesStartsAlone");
        }
        Assert.True(!P().MayLaunch(true, false, false) && !P().MayLaunch(true, true, true) && !P().MayLaunch(false, false, false)
            && P().MayLaunch(false, true, false), "launchGateRespectsPrefAndQuit");
    }
}
