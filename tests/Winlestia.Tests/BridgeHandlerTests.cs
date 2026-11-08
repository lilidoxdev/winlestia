// C#12 / Handlers du pont Qt via le dispatch réel (BridgeActionTable) : applications, thème, préférences, raccourcis, validation
// des arguments. Propriétaire factice (StubOwner), Store sur un dossier temporaire ; chaque test tourne sur son propre thread STA.
using System;
using System.IO;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BridgeHandlerTests
{
    internal static void With(Action<BridgeRig> body) => Sta.Run(() =>
    {
        using var rig = new BridgeRig();
        body(rig);
    });

    [Fact]
    public void RealTableCoversAtLeastOneHundredActions() => Assert.True(QtShellBridge.ActionNames.Count >= 100);

    [Fact]
    public void UnknownCommandLeavesTheStateReply() => With(r =>
    {
        var (raw, call, events) = r.Run("nope");
        Assert.Null(raw);
        Assert.Null(call.Notice);
        Assert.Empty(events);
    });

    [Fact]
    public void StateQueuesNothing() => With(r =>
    {
        var (raw, call, events) = r.Run("state");
        Assert.Null(raw);
        Assert.Null(call.Notice);
        Assert.Empty(events);
        Assert.Empty(r.Owner.Calls);
    });

    [Theory]
    [InlineData("palette")]
    [InlineData("tools")]
    [InlineData("clipboard")]
    [InlineData("capture")]
    [InlineData("audio")]
    [InlineData("microphone")]
    public void SimpleSignalsQueueTheirEvent(string command) => With(r =>
    {
        Assert.Equal(new[] { command }, r.Run(command).Events);
    });

    [Fact]
    public void PaletteSignalRefreshesTheCatalogueLater() => With(r =>
    {
        r.Run("palette");
        Assert.Empty(r.Owner.Calls);
        r.Pump();
        Assert.Equal(new[] { "RefreshCatalogueIfStale" }, r.Owner.Calls);
    });

    [Theory]
    [InlineData("{\"section\":\"social\"}", "settings,settings:social")]
    [InlineData("{\"section\":\"bookmarks\"}", "settings,settings:bookmarks")]
    [InlineData("{}", "settings")]
    [InlineData("{\"section\":\"autre\"}", "settings")]
    public void SettingsSignalFollowsTheSection(string args, string expected) => With(r =>
    {
        Assert.Equal(expected, string.Join(",", r.Run("settings", args).Events));
    });

    [Theory]
    [InlineData("apps", "workspace:apps")]
    [InlineData("documents", "workspace:documents")]
    [InlineData("n'importe quoi", "workspace:all")]
    [InlineData("", "workspace:all")]
    public void WorkspaceSignalRestrictsTheId(string id, string expected) => With(r =>
    {
        Assert.Equal(new[] { expected }, r.Run("workspace", "{\"id\":\"" + id + "\"}").Events);
    });

    [Fact]
    public void LaunchClosesThePanelAndLaunchesVisibleEntries() => With(r =>
    {
        r.AddEntry("a1");
        var (_, _, events) = r.Run("launch", "{\"id\":\"a1\"}");
        Assert.Equal(new[] { "close" }, events);
        Assert.Equal(new[] { "Launch:a1" }, r.Owner.Calls);
    });

    [Fact]
    public void LaunchIgnoresUnknownAndHiddenEntries() => With(r =>
    {
        r.AddEntry("h1");
        r.Prefs.Hidden.Add("h1");
        Assert.Empty(r.Run("launch", "{\"id\":\"zz\"}").Events);
        Assert.Empty(r.Run("launch", "{\"id\":\"h1\"}").Events);
        Assert.Empty(r.Run("launch").Events);
        Assert.Empty(r.Owner.Calls);
    });

    [Fact]
    public void PaletteRunRoutesEachKind() => With(r =>
    {
        r.AddEntry("a1");
        r.Prefs.QuickTabs.Add(new WindowsShortcut { Id = "q1", Target = "ms-settings:" });
        r.Prefs.Websites.Add(new WebShortcut { Id = "w1", Url = "https://example.org" });
        r.Run("paletteRun", "{\"kind\":\"app\",\"id\":\"a1\"}");
        r.Run("paletteRun", "{\"kind\":\"quickTab\",\"id\":\"q1\"}");
        r.Run("paletteRun", "{\"kind\":\"website\",\"id\":\"w1\"}");
        r.Run("paletteRun", "{\"kind\":\"web\",\"query\":\"chat noir\"}");
        r.Run("paletteRun", "{\"kind\":\"app\",\"id\":\"absent\"}");
        r.Run("paletteRun", "{\"kind\":\"systemRecent\",\"id\":\"absent\"}");
        r.Run("paletteRun", "{\"kind\":\"inconnu\",\"id\":\"a1\"}");
        Assert.Equal(new[] { "Launch:a1", "RunQuickTab:q1", "LaunchTarget:https://example.org", "WebSearch:chat noir" },
                     r.Owner.Calls);
    });

    [Theory]
    [InlineData("capture", "OpenCapture")]
    [InlineData("notes", "OpenNotes")]
    [InlineData("focus", "OpenFocus")]
    [InlineData("audio", "OpenAudio")]
    [InlineData("profiles", "OpenWorkspaces")]
    [InlineData("quicktabs", "EditQuickTabs")]
    [InlineData("tools", "OpenTools")]
    [InlineData("taskmgr", "TaskManager")]
    [InlineData("calculator", "LaunchTarget:calc.exe")]
    public void PaletteCommandsRunOnTheDispatcherInsideADialog(string id, string expected) => With(r =>
    {
        r.Run("paletteRun", "{\"kind\":\"command\",\"id\":\"" + id + "\"}");
        Assert.Empty(r.Owner.Calls);
        r.Pump();
        Assert.Equal(new[] { expected }, r.Owner.Calls);
        Assert.False(r.Bridge.DialogOpen);
    });

    [Fact]
    public void SimpleOwnerForwards() => With(r =>
    {
        r.Run("refreshCatalogue");
        r.Run("restoreTaskbar");
        Assert.Equal(new[] { "RefreshCatalogue", "SetTaskbar:False" }, r.Owner.Calls);
    });

    [Theory]
    [InlineData("addAccess", "{\"board\":\"games\"}", "AddAccess:games")]
    [InlineData("addAccess", "{\"board\":\"x\"}", "AddAccess:apps")]
    [InlineData("addFolder", "{\"board\":\"projects\"}", "AddFolder:projects")]
    [InlineData("addFolder", "{}", "AddFolder:documents")]
    [InlineData("profiles", "{}", "OpenWorkspaces")]
    [InlineData("websites", "{}", "AddWebsite")]
    public void DialogActionsCloseThePanelThenOpenTheDialog(string command, string args, string expected) => With(r =>
    {
        Assert.Equal(new[] { "close" }, r.Run(command, args).Events);
        r.Pump();
        Assert.Equal(new[] { expected }, r.Owner.Calls);
    });

    [Fact]
    public void MoveOnlyAcceptsKnownBoardsAndEntries() => With(r =>
    {
        r.AddEntry("a1");
        r.Run("move", "{\"id\":\"a1\",\"board\":\"games\"}");
        r.Run("move", "{\"id\":\"a1\",\"board\":\"pirate\"}");
        r.Run("move", "{\"id\":\"absent\",\"board\":\"apps\"}");
        Assert.Equal("games", Assert.Single(r.Prefs.BoardOverrides).Value);
        Assert.True(File.Exists(Path.Combine(r.Dir.Path, "preferences-glass.json")));
    });

    [Fact]
    public void EditEntryTrimsAndBoundsNames() => With(r =>
    {
        var e = r.AddEntry("a1", "Ancien");
        r.Run("editEntry", "{\"id\":\"a1\",\"name\":\"  Neuf  \",\"category\":\" Jeux \"}");
        Assert.Equal("Neuf", e.Name);
        Assert.Equal("Jeux", e.Category);
        r.Run("editEntry", "{\"id\":\"a1\",\"name\":\"" + new string('x', 121) + "\",\"category\":\"" + new string('c', 101) + "\"}");
        Assert.Equal("Neuf", e.Name);
        Assert.Equal("Jeux", e.Category);
        Assert.Equal("Neuf", r.Prefs.Names["a1"]);
        r.Run("editEntry", "{\"id\":\"absent\",\"name\":\"Z\"}");
        Assert.DoesNotContain("absent", r.Prefs.Names.Keys);
    });

    [Fact]
    public void EntryIconRejectsMissingFilesAndWrongExtensions() => With(r =>
    {
        var e = r.AddEntry("a1");
        string txt = r.Dir.Combine("icone.txt");
        File.WriteAllText(txt, "x");
        r.Run("entryIcon", "{\"id\":\"a1\",\"path\":\"" + txt.Replace("\\", "\\\\") + "\"}");
        r.Run("entryIcon", "{\"id\":\"a1\",\"path\":\"" + r.Dir.Combine("absent.png").Replace("\\", "\\\\") + "\"}");
        r.Run("entryIcon", "{\"id\":\"a1\"}");
        Assert.Equal("", e.IconPath);
        Assert.Empty(r.Prefs.Icons);
    });

    [Fact]
    public void IconForUnknownIdDoesNothing() => With(r =>
    {
        var (_, call, events) = r.Run("icon", "{\"id\":\"absent\"}");
        Assert.Null(call.Notice);
        Assert.Empty(events);
    });

    [Fact]
    public void FavoriteTogglesOnlyKnownEntries() => With(r =>
    {
        r.AddEntry("a1");
        r.Run("favorite", "{\"id\":\"a1\"}");
        Assert.Contains("a1", r.Prefs.Favorites);
        r.Run("favorite", "{\"id\":\"a1\"}");
        Assert.DoesNotContain("a1", r.Prefs.Favorites);
        r.Run("favorite", "{\"id\":\"absent\"}");
        Assert.Empty(r.Prefs.Favorites);
    });

    [Fact]
    public void RevealEditWebsiteAndRaiseMediaUseTheOwner() => With(r =>
    {
        r.AddEntry("a1");
        r.Prefs.Websites.Add(new WebShortcut { Id = "w1", Url = "https://example.org" });
        r.Run("reveal", "{\"id\":\"a1\"}");
        r.Run("reveal", "{\"id\":\"absent\"}");
        r.Run("website", "{\"id\":\"w1\"}");
        r.Run("website", "{\"id\":\"absent\"}");
        r.Run("raiseMedia");
        r.Run("edit", "{\"id\":\"a1\"}");
        r.Run("edit", "{\"id\":\"absent\"}");
        Assert.Equal(new[] { "Reveal:a1", "LaunchTarget:https://example.org" }, r.Owner.Calls);
        r.Pump();
        Assert.Equal("Edit:a1", r.Owner.Calls.Last());
        Assert.Equal(3, r.Owner.Calls.Count);
    });

    [Fact]
    public void CustomThemeRejectsMissingOrInvalidPalettes() => With(r =>
    {
        Assert.Equal("Palette invalide.", r.Run("customTheme").Call.Notice);
        Assert.Equal("Palette invalide.", r.Run("customTheme", "{\"colors\":3}").Call.Notice);
        var bad = r.Run("customTheme", "{\"colors\":{\"background\":\"#000000\",\"text\":\"rouge\"}}");
        Assert.Equal("Couleur invalide : text", bad.Call.Notice);
        Assert.Empty(r.Owner.Calls);
        Assert.NotEqual("custom", r.Prefs.Theme);
    });

    [Fact]
    public void CustomThemeStoresNormalisedColorsAndAppliesIt() => With(r =>
    {
        r.Prefs.AutoTheme = true;
        r.Run("customTheme", "{\"colors\":{\"background\":\"#AABBCC\",\"text\":\"#ffffff\",\"primary\":\"#112233\","
            + "\"secondary\":\"#445566\",\"tertiary\":\"#778899\"},\"glass\":true,\"opacity\":5}");
        Assert.Equal("#aabbcc", r.Prefs.CustomPalette["background"]);
        Assert.Equal(5, r.Prefs.CustomPalette.Count);
        Assert.True(r.Prefs.CustomGlass);
        Assert.Equal(.95, r.Prefs.CustomGlassOpacity);
        Assert.Equal("custom", r.Prefs.Theme);
        Assert.False(r.Prefs.AutoTheme);
        Assert.Equal(new[] { "SetTheme:custom" }, r.Owner.Calls);
    });

    [Theory]
    [InlineData("{\"mode\":\"custom\"}", "custom", false, "SetTheme:custom")]
    [InlineData("{\"mode\":\"manual\",\"dark\":true}", "nuit", false, "SetTheme:nuit")]
    [InlineData("{\"mode\":\"manual\"}", "shell", false, "SetTheme:shell")]
    [InlineData("{\"mode\":\"auto\"}", "ocean", true, "SetTheme:ocean")]
    public void ThemeSelectionPicksTheRightTheme(string args, string theme, bool auto, string call) => With(r =>
    {
        r.Prefs.Theme = "ocean";
        r.Run("theme", args);
        Assert.Equal(theme, r.Prefs.Theme);
        Assert.Equal(auto, r.Prefs.AutoTheme);
        Assert.Equal(new[] { call }, r.Owner.Calls);
    });

    [Fact]
    public void CaptureShortcutModeFollowsTheFlag() => With(r =>
    {
        r.Run("captureShortcutMode", "{\"enabled\":true}");
        Assert.True(r.Bridge.CapturingShortcut);
        r.Run("captureShortcutMode", "{\"enabled\":\"oui\"}");
        Assert.False(r.Bridge.CapturingShortcut);
    });

    [Theory]
    [InlineData("accentBorders", nameof(Preferences.AccentBorders))]
    [InlineData("desktopLogo", nameof(Preferences.DesktopLogo))]
    [InlineData("osd", nameof(Preferences.Osd))]
    [InlineData("quietFullscreen", nameof(Preferences.QuietFullscreen))]
    [InlineData("desktopIndicator", nameof(Preferences.DesktopIndicator))]
    [InlineData("dockDrawerOpen", nameof(Preferences.DockDrawerOpen))]
    [InlineData("bookmarksRailVisible", nameof(Preferences.BookmarksRailVisible))]
    [InlineData("socialRailVisible", nameof(Preferences.SocialRailVisible))]
    [InlineData("clipboardImages", nameof(Preferences.ClipboardImages))]
    [InlineData("clipboardHistory", nameof(Preferences.ClipboardHistory))]
    [InlineData("socialBackground", nameof(Preferences.SocialBackground))]
    public void BooleanPreferencesFollowTheFlagBothWays(string key, string property) => With(r =>
    {
        var prop = typeof(Preferences).GetProperty(property)!;
        r.Run("preference", "{\"key\":\"" + key + "\",\"enabled\":true}");
        Assert.True((bool)prop.GetValue(r.Prefs)!);
        r.Run("preference", "{\"key\":\"" + key + "\",\"enabled\":false}");
        Assert.False((bool)prop.GetValue(r.Prefs)!);
        r.Run("preference", "{\"key\":\"" + key + "\"}");
        Assert.False((bool)prop.GetValue(r.Prefs)!);
    });

    [Theory]
    [InlineData("topActivationMargin", 999.0, 160.0)]
    [InlineData("topActivationMargin", -5.0, 0.0)]
    [InlineData("dockWidth", 100.0, 560.0)]
    [InlineData("dockWidth", 9000.0, 4000.0)]
    [InlineData("frameThickness", 2.0, 4.0)]
    [InlineData("frameThickness", 22.4, 22.0)]
    [InlineData("glassOpacity", 0.0, .02)]
    [InlineData("glassOpacity", 3.0, .95)]
    [InlineData("motion", 9.0, 1.5)]
    [InlineData("iconSize", 10.0, 40.0)]
    [InlineData("iconSize", 70.0, 70.0)]
    [InlineData("clipboardLimit", 1.0, 8.0)]
    [InlineData("clipboardLimit", 500.0, 80.0)]
    [InlineData("customGlassOpacity", 0.5, .5)]
    public void NumericPreferencesAreClamped(string key, double sent, double expected) => With(r =>
    {
        r.Run("preference", "{\"key\":\"" + key + "\",\"value\":" + sent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        var prop = typeof(Preferences).GetProperty(char.ToUpperInvariant(key[0]) + key.Substring(1))!;
        Assert.Equal(expected, Convert.ToDouble(prop.GetValue(r.Prefs)));
    });

    [Fact]
    public void PreferencesTalkToTheOwnerWhenNeeded() => With(r =>
    {
        r.Run("preference", "{\"key\":\"taskbar\",\"enabled\":true}");
        r.Run("preference", "{\"key\":\"replaceTaskbar\",\"enabled\":false}");
        r.Run("preference", "{\"key\":\"clipboardImages\",\"enabled\":true}");
        r.Run("preference", "{\"key\":\"clipboardLimit\",\"value\":20}");
        r.Run("preference", "{\"key\":\"clipboardHistory\",\"enabled\":true}");
        r.Prefs.Theme = "nuit";
        r.Run("preference", "{\"key\":\"reducedEffects\",\"enabled\":true}");
        Assert.Equal(new[] { "SetTaskbar:True", "SetTaskbar:False", "ApplyClipboardSettings", "ApplyClipboardSettings",
            "ApplyClipboardSettings", "SetTheme:nuit" }, r.Owner.Calls);
        Assert.True(r.Prefs.ReducedEffects);
    });

    [Fact]
    public void HidingTheDockGivesTheWindowsTaskbarBack() => With(r =>
    {
        r.Owner.Taskbar = true;
        r.Run("preference", "{\"key\":\"dockVisible\",\"enabled\":false}");
        Assert.False(r.Prefs.DockVisible);
        Assert.Equal(new[] { "SetTaskbar:False" }, r.Owner.Calls);
        r.Owner.Calls.Clear();
        r.Owner.Taskbar = false;
        r.Run("preference", "{\"key\":\"dockVisible\",\"enabled\":false}");
        r.Run("preference", "{\"key\":\"dockVisible\",\"enabled\":true}");
        Assert.True(r.Prefs.DockVisible);
        Assert.Empty(r.Owner.Calls);
    });

    [Fact]
    public void ScreenFrameAndAutoTileOffNeedNoDesktop() => With(r =>
    {
        r.Run("preference", "{\"key\":\"screenFrame\",\"enabled\":false}");
        r.Run("preference", "{\"key\":\"autoTile\",\"enabled\":false}");
        Assert.False(r.Prefs.ScreenFrame);
        Assert.False(r.Prefs.AutoTile);
    });

    [Fact]
    public void UnknownPreferenceKeyChangesNothingButStillSaves() => With(r =>
    {
        var (raw, call, _) = r.Run("preference", "{\"key\":\"inexistante\",\"enabled\":true}");
        Assert.Null(raw);
        Assert.Null(call.Notice);
        Assert.Empty(r.Owner.Calls);
        Assert.True(File.Exists(Path.Combine(r.Dir.Path, "preferences-glass.json")));
    });

    [Theory]
    [InlineData("palette", "palette:3:75")]
    [InlineData("wallpaper", "wallpaper:3:75")]
    [InlineData("home", "home/home:3:75")]
    public void ShortcutRoutesToTheMatchingBinding(string kind, string call) => With(r =>
    {
        var (_, c, events) = r.Run("shortcut", "{\"kind\":\"" + kind + "\",\"modifiers\":3,\"key\":75}");
        Assert.Equal("Raccourci enregistré", c.Notice);
        Assert.Equal(new[] { call }, r.Owner.KeysStub.Calls);
        Assert.Equal(new[] { "shortcut-saved:" + kind }, events);
    });

    [Fact]
    public void ShortcutRefusedByWindowsReportsTheError() => With(r =>
    {
        r.Owner.KeysStub.Accept = false;
        var (_, c, events) = r.Run("shortcut", "{\"kind\":\"palette\",\"modifiers\":3,\"key\":75}");
        Assert.Equal("déjà pris", c.Notice);
        Assert.Empty(events);
    });

    [Theory]
    [InlineData("{\"modifiers\":0,\"key\":75}")]
    [InlineData("{\"modifiers\":16,\"key\":75}")]
    [InlineData("{\"modifiers\":3,\"key\":31}")]
    [InlineData("{\"modifiers\":3,\"key\":255}")]
    [InlineData("{}")]
    public void ShortcutOutOfRangeAsksForAValidCombination(string args) => With(r =>
    {
        var (_, c, events) = r.Run("shortcut", args);
        Assert.Equal("Choisis Ctrl, Alt ou Windows avec une autre touche.", c.Notice);
        Assert.Empty(r.Owner.KeysStub.Calls);
        Assert.Empty(events);
    });

    [Fact]
    public void ShortcutWithAnIdGoesThroughTheShortcutManager() => With(r =>
    {
        var registered = new System.Collections.Generic.Dictionary<int, (uint, uint)>();
        GlobalShortcut.TestRegister = (w, id, m, k) => registered.TryAdd(id, (m & ~0x4000u, k)) ? 0 : 1409;
        GlobalShortcut.TestUnregister = (w, id) => registered.Remove(id);
        try
        {
            var p = new Preferences();
            var g = new GlobalShortcut(IntPtr.Zero, p.Shortcut, p.PaletteShortcut, p.PinShortcut, p.CaptureShortcut,
                p.WallpaperShortcut, p.SettingsShortcut);
            r.Owner.Shortcuts = new ShortcutManager(g, () => p, () => { });
            string Combo() => ((dynamic)r.Owner.Shortcuts.List().First(o => (string)((dynamic)o).id == "palette")).combo;
            string before = Combo();
            r.Run("shortcut", "{\"id\":\"palette\",\"combo\":\"Ctrl+Alt+K\"}");
            Assert.Equal("Ctrl+Alt+K", Combo());
            r.Run("shortcut-reset", "{\"id\":\"palette\"}");
            Assert.Equal(before, Combo());
        }
        finally
        {
            GlobalShortcut.TestRegister = null;
            GlobalShortcut.TestUnregister = null;
        }
    });

    [Theory]
    [InlineData("explorerReport", "last-report.txt")]
    public void ExplorerShortcutsLaunchThroughTheOwner(string command, string suffix) => With(r =>
    {
        r.Run(command);
        Assert.EndsWith(suffix, Assert.Single(r.Owner.Calls));
        r.Owner.Calls.Clear();
        r.Run("explorerOpen");
        Assert.Equal("LaunchTarget:" + Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Assert.Single(r.Owner.Calls));
    });
}
