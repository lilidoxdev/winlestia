using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PanneauxGlass;
using Xunit;

public class LyricsTests
{
    [Fact]
    public void ParsesTagsOffsetAndKeepsGaps()
    {
        var lines = LyricsText.ParseLrc("[ar:Artiste]\n[ti:Titre]\n[offset:500]\n[00:10.00]Bonjour\n[00:05.50][01:00.25]Refrain\n[00:20.00]\n[bad]x");
        Assert.Equal(new[] { 5.0, 9.5, 19.5, 59.75 }, lines.Select(l => Math.Round(l.Time, 2)));
        Assert.Equal(new[] { "Refrain", "Bonjour", "", "Refrain" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void DropsCreditLinesAtTheStartOnly()
    {
        var lines = LyricsText.ParseLrc("[00:01.00]Composer: Quelqu'un\n[00:30.00]Composer: dans la chanson, vraiment long");
        Assert.Single(lines);
        Assert.Equal(30, lines[0].Time);
    }

    [Fact]
    public void EmptyOrGarbageGivesNoLines()
    {
        Assert.Empty(LyricsText.ParseLrc(null));
        Assert.Empty(LyricsText.ParseLrc("juste du texte"));
    }

    [Theory]
    [InlineData("Song - 2011 Remaster", "Song")]
    [InlineData("Song - Remastered 2009", "Song")]
    [InlineData("Song (feat. Someone)", "Song")]
    [InlineData("Song [ft. Someone]", "Song")]
    [InlineData("Song (Remastered)", "Song")]
    [InlineData("Song - Mono Version", "Song")]
    [InlineData("Song (Live at Wembley)", "Song (Live at Wembley)")]
    [InlineData("Song - Radio Remix", "Song - Radio Remix")]
    [InlineData("Remaster", "Remaster")]
    public void CleanTitleIsConservative(string input, string expected) => Assert.Equal(expected, LyricsText.CleanTitle(input));

    [Theory]
    [InlineData("Song - Radio Remix", "Song")]
    [InlineData("Song (Acoustic Version)", "Song")]
    [InlineData("Song (Live at Wembley)", "Song")]
    public void VariantsAreStrippedForTheWideSearchOnly(string input, string expected)
        => Assert.Equal(expected, LyricsText.CleanTitle(input, true));

    [Theory]
    [InlineData("Daft Punk feat. Pharrell", "Daft Punk")]
    [InlineData("A, B", "A")]
    [InlineData("A & B", "A")]
    public void CleanArtistKeepsTheFirstArtist(string input, string expected) => Assert.Equal(expected, LyricsText.CleanArtist(input));

    [Fact]
    public void TrackKeyIgnoresNoiseCaseAndSubSecondDuration()
        => Assert.Equal(LyricsText.TrackKey("Daft Punk", "Get Lucky (feat. X)", 248.4), LyricsText.TrackKey("daft punk", "get lucky - 2013 remaster", 248.2));

    static LyricCandidate C(string? synced, string? plain, double duration, bool instrumental = false) => new(synced, plain, instrumental, duration);

    [Fact]
    public void SelectionPrefersSyncedWithinTolerance()
    {
        var chosen = LyricsText.Select(new[] { C(null, "p", 200), C("[00:01.00]a", null, 203), C("[00:01.00]b", null, 201.5) }, 200);
        Assert.Equal("[00:01.00]b", chosen!.Synced);
    }

    [Fact]
    public void SelectionRejectsFarDurationsAndFallsBackToPlain()
    {
        Assert.Null(LyricsText.Select(new[] { C("[00:01.00]a", null, 260) }, 200));
        Assert.Equal("p", LyricsText.Select(new[] { C("[00:01.00]a", null, 260), C(null, "p", 199) }, 200)!.Plain);
    }

    [Fact]
    public void SelectionWithUnknownDurationKeepsTheFirstSynced()
        => Assert.Equal("[00:01.00]s", LyricsText.Select(new[] { C(null, "p", 10), C("[00:01.00]s", null, 400) }, 0)!.Synced);

    [Fact]
    public void FromCandidateMapsStates()
    {
        Assert.Equal("synced", LyricsText.FromCandidate(C("[00:01.00]a", "plain", 1)).State);
        Assert.Equal("plain", LyricsText.FromCandidate(C(null, "plain", 1)).State);
        Assert.Equal("instrumental", LyricsText.FromCandidate(C(null, null, 1, true)).State);
        Assert.Equal("none", LyricsText.FromCandidate(C(null, null, 1)).State);
    }

    static string Temp() => Path.Combine(Path.GetTempPath(), "lyrics-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CacheRoundTripsAndNegativeExpiresAfterSevenDays()
    {
        string dir = Temp();
        try
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var cache = new LyricsCache(dir, () => now);
            cache.Put("k1", new LyricsResult("synced", new[] { new LyricLine(1.5, "a"), new LyricLine(2, "") }, ""));
            cache.Put("k2", LyricsResult.None);
            cache.Put("k3", LyricsResult.Loading);
            var back = cache.Get("k1")!;
            Assert.Equal("synced", back.State);
            Assert.Equal(new[] { "a", "" }, back.Lines.Select(l => l.Text));
            Assert.Null(cache.Get("k3"));
            now = now.AddDays(6);
            Assert.NotNull(cache.Get("k2"));
            now = now.AddDays(2);
            Assert.Null(cache.Get("k2"));
            Assert.NotNull(cache.Get("k1"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CacheKeepsAtMostTheCapDeletingOldest()
    {
        string dir = Temp();
        try
        {
            var cache = new LyricsCache(dir, null, 3);
            for (int i = 0; i < 5; i++)
            {
                cache.Put("k" + i, LyricsResult.Instrumental);
                File.SetLastWriteTimeUtc(Directory.GetFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).First(), DateTime.UtcNow.AddMinutes(i));
            }
            Assert.Equal(3, Directory.GetFiles(dir, "*.json").Length);
            Assert.NotNull(cache.Get("k4"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ServiceReturnsLoadingThenCachedResultWithoutRefetching()
    {
        string dir = Temp();
        try
        {
            int calls = 0;
            var done = new TaskCompletionSource();
            var service = new LyricsService(new LyricsCache(dir), (t, a, al, d, c) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<LyricsResult?>(new LyricsResult("plain", Array.Empty<LyricLine>(), "texte"));
            });
            Assert.Equal("loading", service.Request("Titre", "Artiste", "", 100, () => done.TrySetResult()).State);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("plain", service.Request("Titre", "Artiste", "", 100, () => { }).State);
            Assert.Equal(1, calls);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task TransientFailureIsNotWrittenToDisk()
    {
        string dir = Temp();
        try
        {
            var done = new TaskCompletionSource();
            var service = new LyricsService(new LyricsCache(dir), (t, a, al, d, c) => Task.FromResult<LyricsResult?>(null));
            service.Request("T", "A", "", 1, () => done.TrySetResult());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Directory.Exists(dir) && Directory.GetFiles(dir).Length > 0);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CorruptCacheEntryCountsAsMissing()
    {
        string dir = Temp();
        try
        {
            var cache = new LyricsCache(dir);
            cache.Put("k", LyricsResult.Instrumental);
            string file = Directory.GetFiles(dir, "*.json").Single();
            File.WriteAllText(file, "{\"state\":\"none\",\"at\":-1,\"lines\":[],\"plain\":\"\"}");
            Assert.Null(cache.Get("k"));
            File.WriteAllText(file, "{\"state\":\"synced\",\"at\":9223372036854775807,\"lines\":[{\"t\":1}]}");
            Assert.Null(cache.Get("k"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task TransientFailureIsRetriedOnlyAfterTheShortTtl()
    {
        string dir = Temp();
        try
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int calls = 0;
            TaskCompletionSource done = new();
            var service = new LyricsService(new LyricsCache(dir, () => now), (t, a, al, d, c) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<LyricsResult?>(null);
            }, () => now);
            service.Request("T", "A", "", 1, () => done.TrySetResult());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("none", service.Request("T", "A", "", 1, () => { }).State);
            now = now.AddSeconds(30);
            Assert.Equal("none", service.Request("T", "A", "", 1, () => { }).State);
            Assert.Equal(1, calls);
            now = now.AddSeconds(31);
            done = new();
            var d2 = done;
            Assert.Equal("loading", service.Request("T", "A", "", 1, () => d2.TrySetResult()).State);
            await d2.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, calls);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
