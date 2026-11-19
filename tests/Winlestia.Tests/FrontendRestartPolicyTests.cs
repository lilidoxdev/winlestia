// C#12 / FrontendRestartPolicy: backoff 1-2-4-8 s, abandon au-delà de 4 relances dans 2 min, fenêtre glissante (pas de bureau requis).
using System;
using PanneauxGlass;
namespace Winlestia.Tests;
public class FrontendRestartPolicyTests
{
    static readonly DateTime T = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DelaysDoubleThenGiveUp()
    {
        var p = Connected();
        Assert.Equal(TimeSpan.FromSeconds(1), p.RecordCrash(T));
        Assert.Equal(TimeSpan.FromSeconds(2), p.RecordCrash(T.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(4), p.RecordCrash(T.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(8), p.RecordCrash(T.AddSeconds(20)));
        Assert.Null(p.RecordCrash(T.AddSeconds(40)));
    }

    [Fact]
    public void OldCrashesLeaveTheWindow()
    {
        var p = Connected();
        for (int i = 0; i < 4; i++)
            p.RecordCrash(T.AddSeconds(i));
        Assert.Equal(TimeSpan.FromSeconds(1), p.RecordCrash(T.AddSeconds(3).Add(FrontendRestartPolicy.Window)));
    }

    [Fact]
    public void SpacedCrashesNeverGiveUp()
    {
        var p = Connected();
        for (int i = 0; i < 20; i++)
            Assert.Equal(TimeSpan.FromSeconds(1), p.RecordCrash(T.AddMinutes(3 * i)));
    }

    [Fact]
    public void NeverConnectedGivesUpAtOnce()
    {
        var p = new FrontendRestartPolicy();
        Assert.False(p.EverConnected);
        Assert.Null(p.RecordCrash(T));
    }

    [Fact]
    public void RetriesStartOnceConnected()
    {
        var p = new FrontendRestartPolicy();
        p.MarkConnected();
        Assert.True(p.EverConnected);
        Assert.Equal(TimeSpan.FromSeconds(1), p.RecordCrash(T));
    }

    static FrontendRestartPolicy Connected()
    {
        var p = new FrontendRestartPolicy();
        p.MarkConnected();
        return p;
    }
}
