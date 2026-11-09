// C# 12 / MediaIdentity: AUMID resolution, audio session matching, position extrapolation (pure functions).
using System;
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
using Xunit;

namespace Winlestia.Tests;

public class MediaIdentityTests
{
    static readonly Dictionary<string, bool> Results = new();

    static void Check(string name, bool condition)
    {
        Results[name] = condition;
    }

    static MediaSourceIdentity Identity(Dictionary<string, string>? registry = null, params string[] running)
    {
        var names = registry ?? new Dictionary<string, string>();
        return new MediaSourceIdentity(
            aumid => names.TryGetValue(aumid, out var name) ? name : null,
            stem => running.Contains(stem, StringComparer.OrdinalIgnoreCase),
            aumid => null
        );
    }

    static AudioSessionInfo Session(int pid, string stem, string[]? ancestors = null, string identifier = "")
    {
        return new AudioSessionInfo(pid, stem, ancestors ?? Array.Empty<string>(), identifier);
    }

    [Fact]
    public void AllChecks()
    {
        const string FirefoxHash = "308046B0AF4A39CB";

        // AUMID resolution.
        var viaRegistry = Identity(new() { [FirefoxHash] = "Firefox" }).Resolve(FirefoxHash);
        Check("hashViaRegistry", viaRegistry.DisplayName == "Firefox" && viaRegistry.ExeStem == "firefox");
        Check("hashKeepsRawAumid", viaRegistry.Aumid == FirefoxHash);
        var viaProcess = Identity(null, "explorer", "firefox").Resolve(FirefoxHash);
        Check("hashViaRunningProcess", viaProcess.DisplayName == "Firefox" && viaProcess.ExeStem == "firefox");
        var viaLibreWolf = Identity(null, "librewolf").Resolve(FirefoxHash);
        Check("hashLibreWolf", viaLibreWolf.ExeStem == "librewolf");
        var unknownHash = Identity().Resolve(FirefoxHash);
        Check(
            "unknownHashNotShown",
            unknownHash.DisplayName == "Navigateur" && !unknownHash.DisplayName.Contains("308046")
        );
        Check("unknownHashNoStem", unknownHash.ExeStem == "");
        var registryOther = Identity(new() { [FirefoxHash] = "Mon lecteur" }).Resolve(FirefoxHash);
        Check("hashRegistryOtherName", registryOther.DisplayName == "Mon lecteur");
        var hashNamedHash = Identity(new() { [FirefoxHash] = "AABBCCDDEEFF0011" }).Resolve(FirefoxHash);
        Check("hashNameNeverHashItself", hashNamedHash.DisplayName == "Navigateur");
        var chrome = Identity().Resolve("Chrome");
        Check("chrome", chrome.DisplayName == "Google Chrome" && chrome.ExeStem == "chrome");
        var edge = Identity().Resolve("Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App");
        Check("edgePackaged", edge.DisplayName == "Microsoft Edge" && edge.ExeStem == "msedge");
        Check("msedgeExe", Identity().Resolve("MSEdge").ExeStem == "msedge");
        var spotify = Identity().Resolve("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");
        Check("spotifyPackaged", spotify.DisplayName == "Spotify" && spotify.ExeStem == "spotify");
        Check("spotifyExe", Identity().Resolve("Spotify.exe").DisplayName == "Spotify");
        Check("brave", Identity().Resolve("BraveSoftware.Brave").ExeStem == "brave");
        Check("firefoxNamed", Identity().Resolve("Mozilla.Firefox.Stable").ExeStem == "firefox");
        var exe = Identity().Resolve(@"C:\Apps\vlc.exe");
        Check("exeAumid", exe.ExeStem == "vlc" && exe.DisplayName == "Vlc");
        var packaged = Identity().Resolve("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic");
        Check("packagedCleanName", packaged.DisplayName == "Zune Music" && packaged.ExeStem == "");
        var shell = new MediaSourceIdentity(a => null, s => false, a => "Groove Perso").Resolve("Some.App_abc123!App");
        Check("packagedShellName", shell.DisplayName == "Groove Perso");
        Check("emptyAumid", Identity().Resolve("").DisplayName == "Lecteur Windows");
        Check("nullAumid", Identity().Resolve(null).DisplayName == "Lecteur Windows");
        Check(
            "hexStyleDetection",
            MediaSourceIdentity.IsHashStyle(FirefoxHash) && !MediaSourceIdentity.IsHashStyle("Spotify.exe")
        );

        // Session matching.
        var firefox = new MediaTarget("firefox", FirefoxHash);
        Check("matchOwnExe", MediaSessionMatch.Matches(Session(10, "firefox"), firefox));
        Check("matchCaseInsensitive", MediaSessionMatch.Matches(Session(10, "Firefox"), firefox));
        var child = Session(11, "plugin-container", new[] { "firefox", "explorer" });
        Check("matchChildViaParentChain", MediaSessionMatch.Matches(child, firefox));
        Check("matchEmptyExeViaParent", MediaSessionMatch.Matches(Session(12, "", new[] { "firefox" }), firefox));
        var byIdentifier = Session(13, "", null, @"\Device\x|{0.0.0}\Program Files\Mozilla Firefox\firefox.exe%b1");
        Check("matchEmptyExeViaIdentifier", MediaSessionMatch.Matches(byIdentifier, firefox));
        var discord = Session(20, "Discord", new[] { "Discord", "explorer" }, @"...\Discord.exe%b2");
        Check("noMatchDiscord", !MediaSessionMatch.Matches(discord, firefox));
        Check("noMatchEmptyUnrelated", !MediaSessionMatch.Matches(Session(21, "", null, ""), firefox));
        Check("noMatchWhenNoTarget", !MediaSessionMatch.Matches(Session(10, "firefox"), new MediaTarget("", "")));
        var sessions = new[]
        {
            Session(10, "firefox"),
            Session(11, "", new[] { "firefox" }),
            Session(12, "firefox", new[] { "firefox" }),
            Session(20, "Discord", new[] { "explorer" }),
        };
        Check("multipleSessionsAllMatched", sessions.Count(s => MediaSessionMatch.Matches(s, firefox)) == 3);
        var packagedTarget = new MediaTarget("", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");
        var packagedSession = Session(30, "", null, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify|x");
        Check("matchPackageFamilyInIdentifier", MediaSessionMatch.Matches(packagedSession, packagedTarget));
        var hashOnly = new MediaTarget("", FirefoxHash);
        Check(
            "hashAumidNotUsedAsIdentifier",
            !MediaSessionMatch.Matches(Session(31, "x", null, FirefoxHash), hashOnly)
        );

        // Generic ancestors never match, direct exe still does.
        var explorerTarget = new MediaTarget("explorer", "Microsoft.Windows.Explorer");
        Check(
            "genericAncestorNoMatch",
            !MediaSessionMatch.Matches(Session(40, "x", new[] { "explorer" }), explorerTarget)
        );
        Check("genericDirectExeMatches", MediaSessionMatch.Matches(Session(41, "explorer"), explorerTarget));
        var discordViaShell = Session(42, "Discord", new[] { "Discord", "explorer", "cmd" });
        Check("discordNotFirefoxViaGenericAncestors", !MediaSessionMatch.Matches(discordViaShell, firefox));

        // Ambiguous browsers: no wrong guess, short TTL only.
        var ambiguous = Identity(null, "firefox", "zen").Resolve(FirefoxHash, out bool ambiguousStable);
        Check(
            "ambiguousBrowsersGenericName",
            ambiguous.DisplayName == "Navigateur" && ambiguous.ExeStem == "" && !ambiguousStable
        );
        Identity(null, "zen").Resolve(FirefoxHash, out bool singleStable);
        Check("singleBrowserStable", singleStable);
        Identity().Resolve(FirefoxHash, out bool noneStable);
        Check("noBrowserNotStable", !noneStable);
        Identity(new() { [FirefoxHash] = "Firefox" }, "firefox", "zen").Resolve(FirefoxHash, out bool registryStable);
        Check("registryNameStable", registryStable);

        // Process tree helpers.
        var parents = new Dictionary<int, int>
        {
            [11] = 10,
            [10] = 5,
            [5] = 1,
            [20] = 5,
        };
        var stems = new Dictionary<int, string>
        {
            [11] = "firefox",
            [10] = "firefox",
            [5] = "explorer",
            [1] = "System",
            [20] = "Discord",
        };
        Func<int, int> parentOf = pid => parents.TryGetValue(pid, out var p) ? p : 0;
        Func<int, string> stemOf = pid => stems.TryGetValue(pid, out var s) ? s : "";
        var chain = MediaSessionMatch.AncestorStems(11, parentOf, stemOf);
        Check("ancestorChain", chain.SequenceEqual(new[] { "firefox", "explorer", "System" }));
        Check("rootPidClimbsSameApp", MediaSessionMatch.RootPid(11, "firefox", parentOf, stemOf) == 10);
        Check("rootPidStopsAtOtherApp", MediaSessionMatch.RootPid(10, "firefox", parentOf, stemOf) == 10);
        Check("rootPidEmptyStem", MediaSessionMatch.RootPid(11, "", parentOf, stemOf) == 11);
        var loop = new Dictionary<int, int> { [1] = 2, [2] = 1 };
        Func<int, int> loopParent = pid => loop.TryGetValue(pid, out var q) ? q : 0;
        Check("parentCycleTerminates", MediaSessionMatch.AncestorStems(1, loopParent, pid => "a").Count <= 2);
        Check("rootPidCycleTerminates", MediaSessionMatch.RootPid(1, "a", loopParent, pid => "a") > 0);

        // Position extrapolation.
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var t5 = t0.AddSeconds(5);
        Check("playingAdvances", Math.Abs(MediaTimeline.ExtrapolatePosition(10, t0, t5, 1, true, 200) - 15) < 1e-9);
        Check("pausedFrozen", MediaTimeline.ExtrapolatePosition(10, t0, t5, 1, false, 200) == 10);
        Check("rateApplied", Math.Abs(MediaTimeline.ExtrapolatePosition(10, t0, t5, 2, true, 200) - 20) < 1e-9);
        Check(
            "zeroRateTreatedAsOne",
            Math.Abs(MediaTimeline.ExtrapolatePosition(10, t0, t5, 0, true, 200) - 15) < 1e-9
        );
        Check("clampsToDuration", MediaTimeline.ExtrapolatePosition(198, t0, t5, 1, true, 200) == 200);
        Check(
            "unknownDurationNoClamp",
            Math.Abs(MediaTimeline.ExtrapolatePosition(198, t0, t5, 1, true, 0) - 203) < 1e-9
        );
        Check("negativeFloorsAtZero", MediaTimeline.ExtrapolatePosition(-3, t0, t0, 1, false, 200) == 0);
        Check("noTimestampNoAdvance", MediaTimeline.ExtrapolatePosition(10, default, t5, 1, true, 200) == 10);
        Check("futureTimestampNoRewind", MediaTimeline.ExtrapolatePosition(10, t5, t0, 1, true, 200) == 10);
        Check(
            "durationKnownWhenEndAfterStart",
            MediaTimeline.IsDurationKnown(TimeSpan.Zero, TimeSpan.FromSeconds(200))
        );
        Check("durationUnknownWhenEndZero", !MediaTimeline.IsDurationKnown(TimeSpan.Zero, TimeSpan.Zero));
        Check(
            "durationUnknownWhenEndBeforeStart",
            !MediaTimeline.IsDurationKnown(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
        );

        // Resubscription: same AUMID with a new object must resubscribe; the same object must not.
        var firstSession = new object();
        var secondSession = new object();
        Check("resubscribeOnNewObject", MediaTimeline.NeedsResubscribe(firstSession, secondSession));
        Check("noResubscribeSameObject", !MediaTimeline.NeedsResubscribe(firstSession, firstSession));
        Check("resubscribeFromNone", MediaTimeline.NeedsResubscribe(null, firstSession));
        Check("noResubscribeNoneToNone", !MediaTimeline.NeedsResubscribe(null, null));

        Assert.True(Results.Values.All(v => v), string.Join(", ", Results.Where(x => !x.Value).Select(x => x.Key)));
    }
}
