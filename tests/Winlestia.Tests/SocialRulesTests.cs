// Règles pures du service Social (titre -> non-lus, découpe de texte) et synthèse du carillon : aucun WebView2, aucun son joué.
using System;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class SocialRulesTests
{
    [Theory]
    [InlineData("facebook", "(3) Facebook", 3)]
    [InlineData("facebook", "(99+) Facebook", 99)]
    [InlineData("facebook", "(1 234) Facebook", 1234)]
    [InlineData("facebook", "Facebook", 0)]
    [InlineData("facebook", "• Facebook", 1)]
    [InlineData("facebook", "   ", 0)]
    public void TitleBecomesUnreadCount(string id, string title, int expected)
        => Assert.Equal(expected, SocialRules.ParseUnread(id, title));

    [Fact]
    public void HugeCountIsCapped() => Assert.Equal(99999, SocialRules.ParseUnread("x", "(123456789) X"));

    [Fact]
    public void GmailCountsOnlyOnListViews()
    {
        const string inbox = "https://mail.google.com/mail/u/0/#inbox";
        Assert.Equal(5, SocialRules.ParseUnread("gmail", "Boîte de réception (5) - a@b.c - Gmail", inbox));
        Assert.Equal(0, SocialRules.ParseUnread("gmail", "Boîte de réception - a@b.c - Gmail", inbox));
        Assert.Null(SocialRules.ParseUnread("gmail", "Sujet - a@b.c - Gmail", inbox + "/FMfcgzGxRabcdefghijkl0123"));
        Assert.Null(SocialRules.ParseUnread("gmail", "(5) autre chose", inbox));
    }

    [Fact]
    public void GmailListViewRules()
    {
        Assert.False(SocialRules.GmailListView(null));
        Assert.False(SocialRules.GmailListView("pas une url"));
        Assert.True(SocialRules.GmailListView("https://mail.google.com/mail/u/0/"));
        Assert.True(SocialRules.GmailListView("https://mail.google.com/mail/#label/Work"));
        Assert.False(SocialRules.GmailListView("https://mail.google.com/mail/#settings"));
    }

    [Fact]
    public void ClipFlattensAndTruncates()
    {
        Assert.Equal("", SocialRules.Clip(null, 10));
        Assert.Equal("a  b", SocialRules.Clip("  a\r\nb  ", 10));
        Assert.Equal("abc", SocialRules.Clip("abcdef", 3));
    }

    [Fact]
    public void ChimeIsNormalisedStereoWithFadeOut()
    {
        var mix = SocialChime.Compose();
        Assert.Equal(0, mix.Length % 2);
        Assert.InRange(mix.Max(Math.Abs), 0.85f, 0.91f);
        Assert.True(Math.Abs(mix[^1]) < 0.01f && Math.Abs(mix[^2]) < 0.01f);
    }

    static int Peak(byte[] bytes)
    {
        int peak = 0;
        for (int i = 0; i < bytes.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((short)(bytes[i] | bytes[i + 1] << 8)));
        return peak;
    }

    [Fact]
    public void ChimeVolumeIsQuadraticAndClamped()
    {
        var full = SocialChime.Render(1);
        Assert.Equal(SocialChime.Compose().Length * 2, full.Length);
        Assert.Equal(0, Peak(SocialChime.Render(0)));
        Assert.Equal(Peak(full), Peak(SocialChime.Render(5)));
        double ratio = (double)Peak(SocialChime.Render(0.5)) / Peak(full);
        Assert.InRange(ratio, 0.24, 0.26);
    }
}
