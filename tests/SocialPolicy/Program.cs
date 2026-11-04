// C#12 / SocialPolicy: pure preload and idle-release decisions of messaging views (no WebView2).
using System;
using System.Collections.Generic;
using PanneauxGlass;

class Test
{
    static int Main()
    {
        var r = new Dictionary<string, bool>();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r["startCreatesNothingByDefault"] = !SocialPolicy.ShouldPreload(false, true, true);
        r["backgroundPreloadsConnectedEnabled"] = SocialPolicy.ShouldPreload(true, true, true);
        r["backgroundSkipsDisabledOrDisconnected"] =
            !SocialPolicy.ShouldPreload(true, false, true) && !SocialPolicy.ShouldPreload(true, true, false);
        r["hidden59sKept"] = !SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(59), false, true);
        r["hidden61sReleased"] = SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(61), false, true);
        r["background61sKept"] = !SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(61), true, true);
        r["notConnected61sKept"] = !SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(61), false, false);
        r["visibleNeverReleased"] = !SocialPolicy.ShouldRelease(true, t0, t0.AddHours(5), false, true);
        r["backgroundNeverReleased"] = !SocialPolicy.ShouldRelease(false, t0, t0.AddHours(5), true, true);
        r["notConnectedKept"] = !SocialPolicy.ShouldRelease(false, t0, t0.AddHours(5), false, false);
        r["unknownHiddenSinceKept"] = !SocialPolicy.ShouldRelease(false, DateTime.MinValue, t0, false, true);
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            Console.WriteLine(k.Key + "=" + (k.Value ? "true" : "false"));
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
