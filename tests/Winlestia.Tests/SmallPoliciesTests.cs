// Petites regles pures du backend, jusque-la sans test : options de lancement, mode silencieux, durees, pliage de texte,
// trame du tube nomme de Files, validation de cibles, noms d'applications multimedia.
using System;
using System.IO;
using PanneauxGlass;
using Winlestia.Shared;
namespace Winlestia.Tests;
public class SmallPoliciesTests
{
    [Theory]
    [InlineData(new string[0], "Normal")]
    [InlineData(new[] { "--qt-review" }, "Review")]
    [InlineData(new[] { "--qt-verify" }, "Verify")]
    [InlineData(new[] { "--qt-review", "--qt-verify" }, "Verify")]
    [InlineData(new[] { "--qt-verify", "--qt-review" }, "Verify")]
    [InlineData(new[] { "--QT-VERIFY" }, "Normal")]
    [InlineData(new[] { "--qt-verify=1" }, "Normal")]
    public void RunOptionsVerifyWinsOverReviewAndMatchingIsExact(string[] args, string expectedName)
    {
        var expected = Enum.Parse<LaunchMode>(expectedName);
        var options = RunOptions.FromArgs(args);
        Assert.Equal(expected, options.Mode);
        Assert.Equal(expected != LaunchMode.Normal, options.IsAutomated);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]
    [InlineData(-2, -2, 1922, 1082, true)]       // debordement tolere
    [InlineData(2, 2, 1918, 1078, true)]         // marge tolerée (slack = 2)
    [InlineData(3, 0, 1920, 1080, false)]
    [InlineData(0, 0, 1920, 1077, false)]
    [InlineData(0, 0, 1917, 1080, false)]
    public void QuietCoversToleratesTwoPixelsOnEveryEdge(int l, int t, int r, int b, bool expected) =>
        Assert.Equal(expected, QuietPolicy.Covers(l, t, r, b, 0, 0, 1920, 1080));

    [Fact]
    public void QuietCoversRejectsEmptyMonitorRectangles()
    {
        Assert.False(QuietPolicy.Covers(0, 0, 100, 100, 50, 50, 50, 50));
        Assert.False(QuietPolicy.Covers(0, 0, 100, 100, 50, 50, 40, 90));
    }

    [Fact]
    public void QuietCoversWorksOnSecondaryMonitorWithNegativeOrigin()
    {
        Assert.True(QuietPolicy.Covers(-1920, 0, 0, 1080, -1920, 0, 0, 1080));
        Assert.False(QuietPolicy.Covers(0, 0, 1920, 1080, -1920, 0, 0, 1080));
    }

    [Theory]
    [InlineData("On", false, true)]
    [InlineData("Off", true, false)]
    [InlineData("Auto", true, true)]
    [InlineData("Auto", false, false)]
    public void QuietForcedStateWinsOverDetection(string forced, bool detected, bool expected) =>
        Assert.Equal(expected, QuietPolicy.Effective(Enum.Parse<QuietForce>(forced), detected));

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 3, false)]
    public void QuietDetectionNeedsPreferenceAndAFullscreenMonitor(bool enabled, int monitors, bool expected) =>
        Assert.Equal(expected, QuietPolicy.Detected(enabled, monitors));

    [Theory]
    [InlineData("ON", "On", true)]
    [InlineData("Off", "Off", true)]
    [InlineData("auto", "Auto", true)]
    [InlineData(" on", "Auto", false)]
    [InlineData("", "Auto", false)]
    [InlineData(null, "Auto", false)]
    [InlineData("maybe", "Auto", false)]
    public void QuietParseIsCaseInsensitiveAndStrict(string? text, string expectedName, bool ok)
    {
        Assert.Equal(ok, QuietPolicy.TryParse(text, out var forced));
        Assert.Equal(Enum.Parse<QuietForce>(expectedName), forced);
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(-30, "0 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min 00 s")]
    [InlineData(125, "2 min 05 s")]
    [InlineData(3599, "59 min 59 s")]
    [InlineData(3600, "1 h 00 min")]
    [InlineData(5400, "1 h 30 min")]
    [InlineData(90000, "25 h 00 min")]
    public void FocusDurationTextFormatsSecondsMinutesAndHours(int seconds, string expected) =>
        Assert.Equal(expected, FocusTimer.DurationText(seconds));

    [Theory]
    [InlineData("Éléphant", "elephant")]
    [InlineData("ÇA va déjà", "ca va deja")]
    [InlineData("naïve Zoë", "naive zoe")]
    [InlineData("ÀÉÎÕÜ", "aeiou")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void StoreFoldRemovesAccentsAndLowercases(string? input, string expected) =>
        Assert.Equal(expected, Store.Fold(input));

    [Fact]
    public void StoreFoldKeepsNonLatinLettersAndDigits()
    {
        Assert.Equal("日本語 123", Store.Fold("日本語 123"));
        Assert.Equal("straße", Store.Fold("Straße"));
    }

    [Fact]
    public void FilesPipeBuildEscapesQuotesBackslashesAndControlCharacters()
    {
        string json = FilesPipe.Build(new[] { "a\"b", @"C:\x", "l1\nl2" }, @"C:\Users\x", null);
        Assert.Equal("{\"args\":[\"a\\\"b\",\"C:\\\\x\",\"l1\\u000al2\"],\"cwd\":\"C:\\\\Users\\\\x\"}\n", json);
    }

    [Fact]
    public void FilesPipeBuildEmitsCmdOnlyWhenGivenAndIsValidJson()
    {
        string withCmd = FilesPipe.Build(Array.Empty<string>(), "", "open\t\"x\"");
        string without = FilesPipe.Build(Array.Empty<string>(), "", null);
        Assert.DoesNotContain("\"cmd\"", without);
        Assert.EndsWith("\n", withCmd);
        using var doc = System.Text.Json.JsonDocument.Parse(withCmd);
        Assert.Equal("open\t\"x\"", doc.RootElement.GetProperty("cmd").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("args").GetArrayLength());
    }

    [Fact]
    public void FilesHandlerBackupOverrideIsHonouredOnlyUnderTemp()
    {
        Assert.True(HandlerOptions.UnderTemp(Path.Combine(Path.GetTempPath(), "x", "b.json")));
        Assert.True(HandlerOptions.UnderTemp(Path.Combine(Path.GetTempPath(), "x", "..", "y.json")));
        Assert.False(HandlerOptions.UnderTemp(Path.Combine(Path.GetTempPath(), "..", "elsewhere", "b.json")));
        Assert.False(HandlerOptions.UnderTemp(@"C:\Windows\System32\b.json"));
        Assert.False(HandlerOptions.UnderTemp("bad\0path"));
    }

    [Fact]
    public void FilesHandlerCommandQuotesExeAndPlaceholder()
    {
        Assert.Equal("\"C:\\Program Files\\W\\w.exe\" \"%1\"", FilesHandler.Command(@"C:\Program Files\W\w.exe"));
    }

    [Theory]
    [InlineData("https://example.com/a?b=c", true)]
    [InlineData(@"C:\Users\x\Documents", true)]
    [InlineData("notepad.exe", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JavaScript:alert(1)", false)]
    [InlineData("data:text/html,<b>x</b>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("https://a.test/\"onmouseover=1", false)]
    [InlineData("https://a.test/\nsecond-line", false)]
    [InlineData("https://a.test/\rsecond-line", false)]
    public void ShortcutEditorTargetValidationBlocksInjectionVectors(string value, bool expected) =>
        Assert.Equal(expected, WindowsShortcutEditor.ValidTarget(value));

    [Fact]
    public void ShortcutEditorTargetLengthLimitIs2047()
    {
        Assert.True(WindowsShortcutEditor.ValidTarget("https://a.test/" + new string('a', 2047 - 15)));
        Assert.False(WindowsShortcutEditor.ValidTarget("https://a.test/" + new string('a', 2048 - 15)));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SPOTIFY", "Spotify")]
    [InlineData("MSEdge", "Microsoft Edge")]
    [InlineData("Chrome.exe", "Google Chrome")]
    [InlineData("firefox.exe", "Firefox")]
    public void MediaAppNameRecognisesKnownPlayers(string id, string expected) =>
        Assert.Equal(expected, MediaController.AppName(id));

    [Theory]
    [InlineData("close", 1, 2, false)]
    [InlineData("close", 2, 2, true)]
    [InlineData("state", 1, 2, true)]
    [InlineData("", 0, 5, true)]
    public void BridgeKeepsOnlyCurrentEpochCloseEvents(string name, long queued, long current, bool expected) =>
        Assert.Equal(expected, QtShellBridge.KeepEvent(name, queued, current));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(double.NaN, "0:00")]
    [InlineData(double.PositiveInfinity, "0:00")]
    [InlineData(59.9, "0:59")]
    [InlineData(61, "1:01")]
    [InlineData(600, "10:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    public void MediaTrackTimeNeverShowsNegativeOrNonFiniteValues(double seconds, string expected) =>
        Assert.Equal(expected, ControlCenterWindow.Time(seconds));

    [Theory]
    [InlineData("connections", "Connexions")]
    [InlineData("media", "Musique")]
    [InlineData("levels", "Volume et luminosité")]
    [InlineData("actions", "Actions rapides")]
    [InlineData("inconnu", "Raccourcis Windows")]
    [InlineData("", "Raccourcis Windows")]
    public void ControlCenterModuleNamesFallBackToShortcuts(string id, string expected) =>
        Assert.Equal(expected, ControlCenterWindow.ModuleName(id));
}
