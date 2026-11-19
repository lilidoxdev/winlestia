// Machine d'etat du compteur de messages non lus (temps injecte) et geometrie des panneaux (Rect pur).
using System;
using System.Windows;
using PanneauxGlass;
namespace Winlestia.Tests;
public class UnreadCounterTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void RiseAboveDisplayedCountGrantsCreditForOneSoundWithinThreeSeconds()
    {
        var c = new UnreadCounter();
        var r = c.OnTitle(3, T0);
        Assert.True(r.Changed);
        Assert.Equal(3, r.Rise);
        Assert.Equal(3, c.Credit);
        Assert.True(c.ConsumeCredit(T0.AddSeconds(2)));
        Assert.False(c.ConsumeCredit(T0.AddSeconds(4)));      // trop tard
        Assert.Equal(2, c.Credit);
    }

    [Fact]
    public void NotificationJustBeforeRiseIsNotCountedTwice()
    {
        var c = new UnreadCounter();
        c.NoteNotification(T0);
        var r = c.OnTitle(1, T0.AddSeconds(1));
        Assert.True(r.AfterNotif);
        Assert.Equal(0, c.Credit);
        var late = new UnreadCounter();
        late.NoteNotification(T0);
        Assert.False(late.OnTitle(1, T0.AddSeconds(10)).AfterNotif);
    }

    [Fact]
    public void DecreaseOrSameTitleIsNotARise()
    {
        var c = new UnreadCounter();
        c.OnTitle(5, T0);
        var down = c.OnTitle(2, T0.AddSeconds(1));
        Assert.True(down.Changed);
        Assert.Equal(0, down.Rise);
        Assert.False(c.OnTitle(2, T0.AddSeconds(2)).Changed);
    }

    [Fact]
    public void ZeroTitleArmsTimerOnlyOnceATitleWasSeenAndLatestTokenWins()
    {
        var c = new UnreadCounter();
        Assert.Equal(0, c.OnTitle(0, T0).ZeroToken);          // titre jamais vu : on ne remet pas a zero
        c.OnTitle(4, T0);
        var zero = c.OnTitle(0, T0.AddSeconds(1));
        Assert.True(zero.ZeroToken > 0);
        Assert.False(c.ZeroExpired(zero.ZeroToken - 1));
        Assert.True(c.ZeroExpired(zero.ZeroToken));
        Assert.Equal(0, c.Count);
        Assert.False(c.ZeroExpired(zero.ZeroToken));           // deja a zero
    }

    [Fact]
    public void ZeroExpiryIsCancelledByNewTitleOrExplicitCancel()
    {
        var c = new UnreadCounter();
        c.OnTitle(4, T0);
        int token = c.OnTitle(0, T0).ZeroToken;
        c.OnTitle(2, T0.AddSeconds(1));
        Assert.False(c.ZeroExpired(token));
        token = c.OnTitle(0, T0.AddSeconds(2)).ZeroToken;
        c.CancelZero();
        Assert.False(c.ZeroExpired(token));
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void ResetOnOpenOnlyForNetworksWithoutTitleCounter()
    {
        var legacy = new UnreadCounter { Count = 7, Credit = 2 };
        Assert.True(legacy.ResetOnOpen());
        Assert.Equal(0, legacy.Count);
        Assert.Equal(0, legacy.Credit);
        Assert.False(legacy.ResetOnOpen());                     // deja a zero
        var titled = new UnreadCounter();
        titled.OnTitle(3, T0);
        Assert.False(titled.ResetOnOpen());
        Assert.Equal(3, titled.Count);
    }

    [Fact]
    public void AbsorbAdoptsDisplayedTitleButKeepsLegacyCountWhenNoTitleSeen()
    {
        var legacy = new UnreadCounter { Count = 5, Legacy = true };
        Assert.False(legacy.Absorb());
        Assert.Equal(5, legacy.Count);
        var seen = new UnreadCounter { Count = 5, Title = 2 };
        Assert.True(seen.Absorb());
        Assert.Equal(2, seen.Count);
        Assert.False(seen.Legacy);
        Assert.False(seen.Absorb());                            // plus rien a absorber
    }

    [Fact]
    public void ResetClearsEverythingAndInvalidatesPendingZero()
    {
        var c = new UnreadCounter();
        c.OnTitle(4, T0);
        int token = c.OnTitle(0, T0).ZeroToken;
        c.Reset();
        Assert.Equal(0, c.Count);
        Assert.False(c.TitleSeen);
        Assert.False(c.ZeroExpired(token));
    }
}

public class PanelLayoutTests
{
    static readonly Rect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void ClampKeepsPanelInsideBoundsAndEnforcesMinimumSize()
    {
        var r = PanelLayout.Clamp(new Rect(1800, 1000, 100, 50), Screen);
        Assert.Equal(480, r.Width);
        Assert.Equal(310, r.Height);
        Assert.Equal(1920 - 480, r.X);
        Assert.Equal(1080 - 310, r.Y);
        var huge = PanelLayout.Clamp(new Rect(-50, -50, 5000, 5000), Screen);
        Assert.Equal(Screen, huge);
    }

    [Fact]
    public void ClampMinimumShrinksOnTinyScreens()
    {
        var tiny = new Rect(0, 0, 300, 200);
        var r = PanelLayout.Clamp(new Rect(0, 0, 10, 10), tiny);
        Assert.Equal(300, r.Width);
        Assert.Equal(200, r.Height);
    }

    [Fact]
    public void DragMoveTranslatesThenClamps()
    {
        var start = new Rect(100, 100, 600, 400);
        Assert.Equal(new Rect(150, 130, 600, 400), PanelLayout.Drag(start, Screen, 50, 30, "move"));
        Assert.Equal(new Rect(0, 0, 600, 400), PanelLayout.Drag(start, Screen, -999, -999, "move"));
    }

    [Theory]
    [InlineData("e", 50, 0, 100, 100, 650, 400)]
    [InlineData("w", 50, 0, 150, 100, 550, 400)]
    [InlineData("s", 0, 50, 100, 100, 600, 450)]
    [InlineData("n", 0, 50, 100, 150, 600, 350)]
    [InlineData("se", 20, 20, 100, 100, 620, 420)]
    [InlineData("e", -500, 0, 100, 100, 480, 400)]      // largeur minimale
    [InlineData("s", 0, -500, 100, 100, 600, 310)]      // hauteur minimale
    public void DragEdgeResizesFromThatEdgeWithMinimumSize(string edge, double dx, double dy, double x, double y, double w, double h) =>
        Assert.Equal(new Rect(x, y, w, h), PanelLayout.Drag(new Rect(100, 100, 600, 400), Screen, dx, dy, edge));

    [Fact]
    public void PlacementCaptureAndResolveRoundTrip()
    {
        var rect = new Rect(192, 108, 960, 540);
        var placement = PanelPlacement.Capture(rect, Screen);
        Assert.True(placement.Valid);
        Assert.Equal(.1, placement.X, 6);
        Assert.Equal(.5, placement.Width, 6);
        Assert.Equal(rect, placement.Resolve(Screen));
        var other = new Rect(1920, 0, 2560, 1440);               // meme disposition relative sur un autre ecran
        Assert.Equal(new Rect(1920 + 256, 144, 1280, 720), placement.Resolve(other));
    }

    [Fact]
    public void PlacementIsInvalidForNonPositiveOrNonFiniteSizes()
    {
        Assert.False(new PanelPlacement { Width = 0, Height = .5 }.Valid);
        Assert.False(new PanelPlacement { Width = .5, Height = double.NaN }.Valid);
        Assert.False(new PanelPlacement { X = double.PositiveInfinity, Width = .5, Height = .5 }.Valid);
        Assert.True(new PanelPlacement { Width = .5, Height = .5 }.Valid);
    }
}
