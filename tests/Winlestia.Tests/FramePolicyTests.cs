// C#12 / FramePolicy: which window carries the accent border, per the AccentBorders and ActiveBorderOnly preferences (pure functions).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class FramePolicyTests
{
    [Fact]
    public void AllChecks()
    {
        IntPtr a = (IntPtr)10, b = (IntPtr)20, z = IntPtr.Zero;
        Assert.True(FramePolicy.Carries(true, false, a, b) && FramePolicy.Carries(true, false, b, b), "allWindowsKeepsEveryWindow");
        Assert.True(FramePolicy.Carries(true, true, b, b) && !FramePolicy.Carries(true, true, a, b), "activeOnlyColoursActiveOnly");
        Assert.True(!FramePolicy.Carries(false, false, a, a) && !FramePolicy.Carries(false, true, a, a), "bordersOffNeverColours");
        var t = FramePolicy.Foreground(true, true, true, b, a);
        Assert.True(t.colour == b && t.restore == a && t.tracked == b, "switchColoursNewRestoresOld");
        t = FramePolicy.Foreground(true, true, true, a, z);
        Assert.True(t.colour == a && t.restore == z && t.tracked == a, "firstWindowNothingToRestore");
        t = FramePolicy.Foreground(true, true, true, a, a);
        Assert.True(t.colour == z && t.restore == z && t.tracked == a, "sameWindowNoop");
        t = FramePolicy.Foreground(true, true, false, b, a);
        Assert.True(t.colour == z && t.restore == z && t.tracked == a, "ineligibleKeepsLast");
        t = FramePolicy.Foreground(true, true, true, z, a);
        Assert.True(t.colour == z && t.restore == z && t.tracked == a, "zeroForegroundKeepsLast");
        t = FramePolicy.Foreground(true, false, true, b, a);
        Assert.True(t.colour == z && t.restore == z && t.tracked == z, "allWindowsModeIgnoresEvent");
        t = FramePolicy.Foreground(false, true, true, b, a);
        Assert.True(t.colour == z && t.restore == z && t.tracked == z, "bordersOffIgnoresActiveOnly");
    }
}
