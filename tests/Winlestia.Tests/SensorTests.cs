// C# 12 / Assistant de capteurs : choix du capteur, lecture de la réponse, codes de sortie, état et temporisation (sans élévation ni tube
// réel)
using System;
using PanneauxGlass;
using WinlestiaSensors;

namespace Winlestia.Tests;

public class SensorTests
{
    [Fact]
    public void PackagePreferredOverCoreAverage() => Assert.Equal(60.0, SensorPick.Choose(new (string, double?)[]
        { ("Core Average", 50), ("CPU Package", 60), ("Core Max", 70) }));

    [Fact]
    public void PriorityOrderIsFollowed() => Assert.Equal(55.0, SensorPick.Choose(new (string, double?)[]
        { ("Core Average", 50), ("Core (Tctl/Tdie)", 55), ("Core Max", 70) }));

    [Fact]
    public void ImplausiblePriorityFallsThrough() => Assert.Equal(50.0, SensorPick.Choose(new (string, double?)[]
        { ("CPU Package", 0), ("Core Average", 50) }));

    [Fact]
    public void UnknownNameUsesFirstPlausible() => Assert.Equal(48.0, SensorPick.Choose(new (string, double?)[]
        { ("Foo", null), ("Bar", 48), ("Baz", 52) }));

    [Fact]
    public void NoSensorGivesNull() => Assert.Null(SensorPick.Choose(Array.Empty<(string, double?)>()));

    [Theory]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(130.0)]
    [InlineData(double.NaN)]
    public void ImplausibleValuesAreNull(double v) => Assert.Null(SensorPick.Plausible(v));

    [Fact]
    public void NullAndEdgesOfPlausibility()
    {
        Assert.Null(SensorPick.Plausible(null));
        Assert.Equal(125.0, SensorPick.Plausible(125));
    }

    [Fact]
    public void FormatUsesInvariantOneDecimal()
    {
        Assert.Equal("54.5", SensorPick.Format(54.46));
        Assert.Equal("null", SensorPick.Format(null));
    }

    [Theory]
    [InlineData("54.5", 54.5)]
    [InlineData("54.5\r\n", 54.5)]
    [InlineData("null", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("0.0", null)]
    [InlineData("250.0", null)]
    [InlineData("54,5", null)]
    public void PipeAnswerIsParsed(string? line, double? expected) => Assert.Equal(expected, SensorLogic.ParseAnswer(line));

    [Theory]
    [InlineData(0, "ok")]
    [InlineData(10, "reboot")]
    [InlineData(2, "failed")]
    [InlineData(20, "failed")]
    [InlineData(21, "failed")]
    [InlineData(30, "failed")]
    [InlineData(-1, "failed")]
    public void ExitCodeMapsToResult(int code, string expected) => Assert.Equal(expected, SensorLogic.ResultFor(code));

    [Theory]
    [InlineData(false, false, false, "absent")]
    [InlineData(false, true, false, "absent")]
    [InlineData(true, false, false, "noDriver")]
    [InlineData(true, true, false, "stopped")]
    [InlineData(true, true, true, "active")]
    public void StatusMapsToState(bool installed, bool driver, bool active, string expected)
        => Assert.Equal(expected, SensorLogic.State(installed, driver, active));

    [Fact]
    public void OutdatedComparesVersions()
    {
        Assert.True(SensorLogic.IsOutdated(new Version(1, 0, 0, 0), new Version(1, 1, 0, 0)));
        Assert.False(SensorLogic.IsOutdated(new Version(1, 1, 0, 0), new Version(1, 1, 0, 0)));
        Assert.False(SensorLogic.IsOutdated(null, new Version(1, 1, 0, 0)));
        Assert.False(SensorLogic.IsOutdated(new Version(1, 0, 0, 0), null));
    }

    [Fact]
    public void BackoffSkipsThirtySecondsAfterThreeFailures()
    {
        long now = 1000;
        var b = new PipeBackoff(() => now);
        b.Report(false); b.Report(false);
        Assert.False(b.ShouldSkip);
        b.Report(false);
        Assert.True(b.ShouldSkip);
        now += 29000;
        Assert.True(b.ShouldSkip);
        now += 1001;
        Assert.False(b.ShouldSkip);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(5L)]
    [InlineData(long.MaxValue / 2)]
    public void NotFreshAtStart(long clock)
    {
        // Premier appel : rien n'est « récent », donc le rafraîchissement s'exécute ; pas de débordement.
        var f = new Freshness(() => clock, 10000);
        Assert.False(f.IsFresh);
    }

    [Fact]
    public void FreshThenStaleAfterWindow()
    {
        long now = 100;
        var f = new Freshness(() => now, 10000);
        f.Mark();
        Assert.True(f.IsFresh);
        now += 9999;
        Assert.True(f.IsFresh);
        now += 1;
        Assert.False(f.IsFresh);
    }

    [Theory]
    [InlineData("https://github.com/a/b", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://foo.githubusercontent.com/x", true)]
    [InlineData("http://github.com/a", false)]
    [InlineData("https://evilgithubusercontent.com/x", false)]
    [InlineData("https://github.com.evil.example/x", false)]
    public void DownloadHostAllowList(string url, bool expected) => Assert.Equal(expected, Installer.HostAllowed(new Uri(url)));

    [Fact]
    public void RecentlyOkIsFalseAtStart() => Assert.False(SensorPipe.RecentlyOk);

    [Fact]
    public void SuccessResetsTheFailureCount()
    {
        long now = 0;
        var b = new PipeBackoff(() => now);
        b.Report(false); b.Report(false); b.Report(true);
        b.Report(false); b.Report(false);
        Assert.False(b.ShouldSkip);
    }
}
