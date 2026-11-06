// C#12 / BrightnessPolicy: DDC/CI range conversion, write rate limit, read failure budget, display id (pure functions).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BrightnessPolicyTests
{
    [Fact]
    public void AllChecks()
    {
        Assert.True(BrightnessPolicy.ToRaw(0, 0, 100) == 0 && BrightnessPolicy.ToRaw(50, 0, 100) == 50 && BrightnessPolicy.ToRaw(100, 0, 100) == 100, "rawFullDefaultRange");
        Assert.True(BrightnessPolicy.ToRaw(0, 10, 90) == 10 && BrightnessPolicy.ToRaw(50, 10, 90) == 50 && BrightnessPolicy.ToRaw(100, 10, 90) == 90, "rawCustomRange");
        Assert.True(BrightnessPolicy.ToRaw(25, 0, 255) == 64, "rawLargeRange");
        Assert.True(BrightnessPolicy.ToRaw(-20, 0, 100) == 0 && BrightnessPolicy.ToRaw(250, 0, 100) == 100 && BrightnessPolicy.ToRaw(double.NaN, 5, 95) == 5, "rawClamps");
        Assert.True(BrightnessPolicy.ToRaw(50, 40, 40) == 40 && BrightnessPolicy.ToRaw(50, 60, 40) == 60, "rawDegenerateRange");
        Assert.True(BrightnessPolicy.ToPercent(10, 10, 90) == 0 && BrightnessPolicy.ToPercent(50, 10, 90) == 50 && BrightnessPolicy.ToPercent(90, 10, 90) == 100, "percentCustomRange");
        Assert.True(BrightnessPolicy.ToPercent(5, 10, 90) == 0 && BrightnessPolicy.ToPercent(200, 10, 90) == 100, "percentClampsOutOfRange");
        Assert.True(BrightnessPolicy.ToPercent(5, 7, 7) == 0, "percentDegenerateRange");
        bool round = true;
        for (int p = 0; p <= 100; p++)
            round &= Math.Round(BrightnessPolicy.ToPercent(BrightnessPolicy.ToRaw(p, 0, 100), 0, 100)) == p;
        Assert.True(round, "roundTripDefaultRange");
        Assert.True(BrightnessPolicy.RemainingDelay(1000, 1000) == BrightnessPolicy.WriteIntervalMs, "delayRightAfterWrite");
        Assert.True(BrightnessPolicy.RemainingDelay(1000, 1050) == BrightnessPolicy.WriteIntervalMs - 50, "delayPartial");
        Assert.True(BrightnessPolicy.RemainingDelay(1000, 1120) == 0 && BrightnessPolicy.RemainingDelay(1000, 5000) == 0, "delayElapsed");
        Assert.True(BrightnessPolicy.RemainingDelay(5000, 1000) == BrightnessPolicy.WriteIntervalMs, "delayClockBackwardsBounded");
        Assert.True(BrightnessPolicy.RemainingDelay(-1_000_000, 123456) == 0, "delayFirstWrite");
        Assert.True(!BrightnessPolicy.Unavailable(0) && !BrightnessPolicy.Unavailable(2) && BrightnessPolicy.Unavailable(3) && BrightnessPolicy.Unavailable(9), "unavailableAfterThree");
        Assert.True(BrightnessPolicy.CanRead(false, false, false), "readIdle");
        Assert.True(!BrightnessPolicy.CanRead(true, false, false) && !BrightnessPolicy.CanRead(false, true, false), "noReadDuringWrite");
        Assert.True(!BrightnessPolicy.CanRead(false, false, true), "noReadWhenUnavailable");
        Assert.True(BrightnessPolicy.IdFromDevice(@"\.\DISPLAY2") == 2 && BrightnessPolicy.IdFromDevice(@"\.\DISPLAY12") == 12, "idFromDevice");
        Assert.True(BrightnessPolicy.IdFromDevice("") == 0 && BrightnessPolicy.IdFromDevice(null) == 0 && BrightnessPolicy.IdFromDevice(@"\.\DISPLAY") == 0 && BrightnessPolicy.IdFromDevice("X12345") == 0, "idFromBadDevice");
        Assert.True(BrightnessPolicy.Matches(37, 4, 0, 10) && !BrightnessPolicy.Matches(37, 5, 0, 10) && BrightnessPolicy.ToPercent(4, 0, 10) == 40, "matchesCoarseRange");
    }
}
