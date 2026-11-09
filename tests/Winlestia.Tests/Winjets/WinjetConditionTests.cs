// C#12 / Tests de l'évaluateur de conditions (enabledWhen / visibleWhen) et de SemVer.
using System;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class WinjetConditionTests
{
    static object? R(string path) => path switch
    {
        "settings.source" => "live",
        "settings.on" => true,
        "settings.n" => 3.0,
        "state.mode" => "face",
        _ => null
    };

    [Theory]
    [InlineData("settings.source == 'live'", true)]
    [InlineData("settings.source != 'live'", false)]
    [InlineData("settings.on && settings.n >= 3", true)]
    [InlineData("settings.n < 3 || !settings.on", false)]
    [InlineData("(settings.n > 2) && state.mode == 'face'", true)]
    [InlineData("settings.missing", false)]
    public void Evaluates(string text, bool expected) => Assert.Equal(expected, WinjetCondition.Evaluate(text, R));

    [Theory]
    [InlineData("")]
    [InlineData("settings.a ==")]
    [InlineData("alert(1)")]
    [InlineData("((")]
    public void InvalidIsFalse(string text) => Assert.False(WinjetCondition.Evaluate(text, R));

    [Fact]
    public void TryParseReportsError()
    {
        Assert.False(WinjetCondition.TryParse("a &&", out _, out string err));
        Assert.NotEqual("", err);
        Assert.True(WinjetCondition.TryParse("a == 1", out var c, out _));
        Assert.NotNull(c);
    }

    [Theory]
    [InlineData(">=0.9.2", "1.0.0", true)]
    [InlineData(">=0.9.2 <2.0.0", "2.0.0", false)]
    [InlineData(">=0.9.2 <2.0.0", "1.5.0", true)]
    [InlineData("=1.0.0", "1.0.1", false)]
    [InlineData("1.0.0", "1.0.0", true)]
    public void SemVerRange(string range, string current, bool expected)
    {
        Assert.True(WinjetSemVer.TryMatches(range, Version.Parse(current), out bool ok));
        Assert.Equal(expected, ok);
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2", false)]
    public void SemVerCore(string s, bool ok) => Assert.Equal(ok, WinjetSemVer.TryParseCore(s, out _));
}
