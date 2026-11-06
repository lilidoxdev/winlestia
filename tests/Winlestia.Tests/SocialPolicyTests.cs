// C#12 / SocialPolicy: pure preload and idle-release decisions of messaging views (no WebView2).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class SocialPolicyTests
{
    [Fact]
    public void AllChecks()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(!SocialPolicy.ShouldPreload(false, true, true), "startCreatesNothingByDefault");
        Assert.True(SocialPolicy.ShouldPreload(true, true, true), "backgroundPreloadsConnectedEnabled");
        Assert.True(!SocialPolicy.ShouldPreload(true, false, true) && !SocialPolicy.ShouldPreload(true, true, false), "backgroundSkipsDisabledOrDisconnected");
        Assert.True(!SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(59), false, true), "hidden59sKept");
        Assert.True(SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(61), false, true), "hidden61sReleased");
        Assert.True(!SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(61), true, true), "background61sKept");
        Assert.True(!SocialPolicy.ShouldRelease(false, t0, t0.AddSeconds(61), false, false), "notConnected61sKept");
        Assert.True(!SocialPolicy.ShouldRelease(true, t0, t0.AddHours(5), false, true), "visibleNeverReleased");
        Assert.True(!SocialPolicy.ShouldRelease(false, t0, t0.AddHours(5), true, true), "backgroundNeverReleased");
        Assert.True(!SocialPolicy.ShouldRelease(false, t0, t0.AddHours(5), false, false), "notConnectedKept");
        Assert.True(!SocialPolicy.ShouldRelease(false, DateTime.MinValue, t0, false, true), "unknownHiddenSinceKept");
    }
}
