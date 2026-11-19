// Règles pures du service Social (titre -> non-lus, découpe de texte) et synthèse du carillon : aucun WebView2, aucun son joué.
using System;
using System.Collections.Generic;
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

public class SocialRestoreTests
{
    static SocialService.HistoryItem H(int seq, string net, string kind = "notif", string title = "t")
        => new() { Seq = seq, Net = net, Kind = kind, Title = title };

    [Fact]
    public void HistoryDropsUnknownNetworksAndNullsAndSortsNewestFirst()
    {
        var r = SocialRules.RestoreHistory(new SocialService.HistoryItem?[] { H(1, "a"), null, H(3, "zz"), H(2, "a") }, id => id == "a");
        Assert.Equal(new[] { 2, 1 }, r.Select(h => h.Seq).ToArray());
    }

    [Fact]
    public void HistoryClipsTextNormalizesKindAndKeepsHundred()
    {
        var items = Enumerable.Range(1, 130).Select(i => H(i, "a", i == 130 ? "weird" : "count", new string('x', 500)));
        var r = SocialRules.RestoreHistory(items, _ => true);
        Assert.Equal(100, r.Count);
        Assert.Equal(130, r[0].Seq);
        Assert.Equal("notif", r[0].Kind);
        Assert.Equal("count", r[1].Kind);
        Assert.Equal(120, r[0].Title.Length);
    }

    [Theory]
    [InlineData(0, new[] { 5, 9 }, 10)]
    [InlineData(50, new[] { 5 }, 50)]
    [InlineData(-3, new int[0], 1)]
    public void NextSeqExceedsEverySeen(int saved, int[] seqs, int expected)
        => Assert.Equal(expected, SocialRules.NextSeqAfter(saved, seqs));

    [Theory]
    [InlineData(-5, false, 0)]
    [InlineData(5000000, false, 999999)]
    [InlineData(500, true, 99)]
    [InlineData(7, true, 7)]
    public void CountIsBoundedAndCappedWhenUntrusted(int saved, bool capped, int expected)
        => Assert.Equal(expected, SocialRules.RestoreCount(saved, capped));
}

public class SocialHistoryMergeTests
{
    static readonly long Noon = new DateTimeOffset(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();
    const long Minute = 60_000;

    static SocialService.HistoryItem H(int seq, string net, string kind, long at, int n = 0, string title = "t")
        => new() { Seq = seq, Net = net, Kind = kind, At = at, N = n, Title = title };

    [Fact]
    public void RiseMergesIntoRecentCountRowAndMovesItToTheTop()
    {
        var list = new List<SocialService.HistoryItem> { H(5, "discord", "notif", Noon), H(4, "snap", "count", Noon - 5 * Minute, 2) };
        var m = SocialRules.MergeCount(list, "snap", 1, Noon, () => 9);
        Assert.Same(list[0], m);
        Assert.Equal(2, list.Count);
        Assert.Equal(3, m!.N);
        Assert.Equal("3 nouveaux messages", m.Title);
        Assert.Equal(Noon, m.At);
        Assert.Equal(9, m.Seq);
    }

    [Fact]
    public void ThreeSingleRisesBecomeOneRow()
    {
        var list = new List<SocialService.HistoryItem> { H(1, "snap", "count", Noon, 1, "1 nouveau message") };
        int seq = 1;
        for (int i = 1; i <= 2; i++)
            Assert.NotNull(SocialRules.MergeCount(list, "snap", 1, Noon + i * Minute, () => ++seq));
        Assert.Single(list);
        Assert.Equal("3 nouveaux messages", list[0].Title);
    }

    [Fact]
    public void LegacyRowWithoutNCountsAsOne()
    {
        var list = new List<SocialService.HistoryItem> { H(1, "snap", "count", Noon, 0, "1 nouveau message") };
        Assert.Equal(3, SocialRules.MergeCount(list, "snap", 2, Noon + Minute, () => 2)!.N);
    }

    [Theory]
    [InlineData("count", "snap", 30L * 60_000)]   // trop ancienne (borne exclue)
    [InlineData("count", "other", 60_000L)]       // autre réseau
    [InlineData("notif", "snap", 60_000L)]        // vraie notification : jamais fusionnée
    public void NoMergeWhenRowIsOldOtherNetworkOrReal(string kind, string net, long age)
    {
        var list = new List<SocialService.HistoryItem> { H(1, net, kind, Noon, 1) };
        Assert.Null(SocialRules.MergeCount(list, "snap", 1, Noon + age, () => 2));
        Assert.Equal(1, Assert.Single(list).Seq);
    }

    [Fact]
    public void NoMergeAcrossMidnight()
    {
        long late = new DateTimeOffset(new DateTime(2026, 11, 10, 23, 55, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        var list = new List<SocialService.HistoryItem> { H(1, "snap", "count", late, 1) };
        Assert.Null(SocialRules.MergeCount(list, "snap", 1, late + 10 * Minute, () => 2));
    }

    [Fact]
    public void NoMergeWhenNewestRowOfTheNetworkIsAlreadyUpgraded()
    {
        var list = new List<SocialService.HistoryItem> { H(3, "snap", "notif", Noon), H(2, "snap", "count", Noon - Minute, 4) };
        Assert.Null(SocialRules.MergeCount(list, "snap", 1, Noon + Minute, () => 4));
    }

    [Fact]
    public void RealNotificationTakesOneMessageFromAMergedRow()
    {
        var merged = H(1, "snap", "count", Noon, 3, "3 nouveaux messages");
        Assert.False(SocialRules.ReleaseOne(merged));
        Assert.Equal(2, merged.N);
        Assert.Equal("2 nouveaux messages", merged.Title);
        Assert.False(SocialRules.ReleaseOne(merged));
        Assert.Equal("1 nouveau message", merged.Title);
        Assert.True(SocialRules.ReleaseOne(merged)); // une seule restante : elle devient la notification
    }

    [Fact]
    public void RemoveBySeqRemovesOnlyThatEntry()
    {
        var list = new List<SocialService.HistoryItem> { H(3, "a", "notif", Noon), H(2, "a", "count", Noon), H(1, "b", "notif", Noon) };
        Assert.False(SocialRules.RemoveBySeq(list, 42, out var none));
        Assert.Null(none);
        Assert.True(SocialRules.RemoveBySeq(list, 2, out var gone));
        Assert.Equal(2, gone!.Seq);
        Assert.Equal(new[] { 3, 1 }, list.Select(h => h.Seq).ToArray());
    }

    [Fact]
    public void RestoreKeepsNOnlyForCountRows()
    {
        var r = SocialRules.RestoreHistory(new[] { H(2, "a", "count", Noon, 7), H(1, "a", "notif", Noon, 7) }, _ => true);
        Assert.Equal(new[] { 7, 0 }, r.Select(h => h.N).ToArray());
    }

    [Theory]
    [InlineData("5 nouveaux messages", 5)]
    [InlineData("1 nouveau message", 1)]
    [InlineData("0 nouveau message", 1)]
    [InlineData("messages", 1)]
    public void RestoreRecoversNFromLegacyCountTitle(string title, int expected)
    {
        var r = SocialRules.RestoreHistory(new[] { H(1, "a", "count", Noon, 0, title) }, _ => true);
        Assert.Equal(expected, r[0].N);
        var merged = SocialRules.MergeCount(r, "a", 2, Noon + 1000, () => 9);
        Assert.Equal(expected + 2, merged!.N);
    }
}
