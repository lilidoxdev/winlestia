// C#12 / DesktopReassertPolicy: debounce, rate cap, follow-up, mute after own run, class filter (no WinEvent).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class DesktopReassertPolicyTests
{
    [Fact]
    public void AllChecks()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var p = new DesktopReassertPolicy();
        Assert.True(p.Event(t), "firstEventAccepted");
        Assert.True(!p.Due(t.AddMilliseconds(399)), "notDueBeforeDebounce");
        Assert.True(p.Due(t.AddMilliseconds(400)), "dueAfterDebounce");
        p.Event(t.AddMilliseconds(300));
        Assert.True(!p.Due(t.AddMilliseconds(600)) && p.Due(t.AddMilliseconds(700)), "burstRestartsDebounce");
        var end = t.AddSeconds(1);
        p.Ran(end);
        Assert.True(!p.Due(end.AddMilliseconds(2999)) && p.Due(end.AddSeconds(3)), "followUpAt3s");
        Assert.True(!p.Event(end.AddMilliseconds(1000)) && p.Event(end.AddMilliseconds(1600)), "eventsMutedAfterRun");
        var q = new DesktopReassertPolicy();
        q.Event(t);
        q.Ran(t.AddSeconds(1));
        q.Event(t.AddMilliseconds(2600));
        Assert.True(q.Due(t.AddMilliseconds(3000)) && !q.Due(t.AddMilliseconds(2900)) || q.Delay(t.AddMilliseconds(2600)) >= TimeSpan.FromMilliseconds(400), "minGapTwoSeconds");
        var f = new DesktopReassertPolicy();
        f.Event(t);
        f.Ran(t);
        f.Ran(t.AddSeconds(3));
        Assert.True(f.Delay(t.AddSeconds(4)) == null, "followUpDoesNotChain");
        var m = new DesktopReassertPolicy();
        m.Event(t);
        m.Ran(t);
        Assert.True(m.Event(t.AddMilliseconds(100), true), "forceBypassesMute");
        var s = new DesktopReassertPolicy();
        s.Event(t);
        for (int i = 1; i < 20; i++)
            s.Event(t.AddMilliseconds(i * 300));
        Assert.True(s.Due(t.AddSeconds(2)), "stormCappedByMaxWait");
        Assert.True(DesktopEventFilter.Relevant("WorkerW", ""), "filterWorkerW");
        Assert.True(DesktopEventFilter.Relevant("SHELLDLL_DefView", "Progman") && DesktopEventFilter.Relevant("SHELLDLL_DefView", "WorkerW"), "filterDefViewOnDesktop");
        Assert.True(!DesktopEventFilter.Relevant("SHELLDLL_DefView", "CabinetWClass"), "filterDefViewInFolderWindow");
        Assert.True(!DesktopEventFilter.Relevant("Shell_TrayWnd", ""), "filterOtherClass");
        Assert.True(!DesktopReassertPolicy.StillWanted(0) && DesktopReassertPolicy.StillWanted(1), "hideIconsZeroStopsReassert");
    }
}
