// Preferences.Normalize (bornes, nettoyage de listes, reparation), regles de Store (pliage de texte, tableaux, favoris,
// recents, tri) et aller-retour disque sur un dossier temporaire (WINLESTIA_DATA_DIR) : aucune donnee utilisateur touchee.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class StorePreferencesTests
{
    static Entry E(string id, string name, string section = "Applications", string category = "", string target = "") =>
        new() { Id = id, Name = name, Section = section, Category = category, Target = target };

    // Isole l'etat statique de Store pour la duree d'un test.
    sealed class StoreScope : IDisposable
    {
        readonly Preferences state = Store.State;
        readonly List<Entry> entries = Store.Entries;
        readonly HashSet<string> missing = Store.Missing;
        readonly string? env = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");
        public StoreScope(string? dataDir = null)
        {
            Store.State = new Preferences();
            Store.Entries = new List<Entry>();
            Store.Missing = new HashSet<string>();
            if (dataDir != null)
                Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", dataDir);
        }
        public void Dispose()
        {
            Store.State = state;
            Store.Entries = entries;
            Store.Missing = missing;
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", env);
        }
    }

    [Theory]
    [InlineData("Éléphant ÀÇ", "elephant ac")]
    [InlineData("  MAJUSCULES ", "  majuscules ")]
    [InlineData(null, "")]
    public void FoldRemovesAccentsAndCase(string? input, string expected) => Assert.Equal(expected, Store.Fold(input));

    [Fact]
    public void NormalizeClampsNumericRanges()
    {
        var p = new Preferences { FrameThickness = 1000, GlassOpacity = double.NaN, Tint = -3, IconSize = 5, Gap = 0, Margin = 999,
                                  MotionLevel = 9, ClipboardLimit = 1, LeftRatio = 0.99, RailHeight = double.PositiveInfinity };
        p.Normalize();
        Assert.Equal(40, p.FrameThickness);
        Assert.Equal(.95, p.GlassOpacity);
        Assert.Equal(.05, p.Tint);
        Assert.Equal(40, p.IconSize);
        Assert.Equal(12, p.Gap);
        Assert.Equal(100, p.Margin);
        Assert.Equal(2, p.MotionLevel);
        Assert.Equal(8, p.ClipboardLimit);
        Assert.Equal(.7, p.LeftRatio);
        Assert.Equal(.94, p.RailHeight);
    }

    [Fact]
    public void NormalizeResetsUnknownThemeHomeScopeAndDictationKey()
    {
        var p = new Preferences { Theme = "inexistant", HomeScope = "autre", DictationKey = 999 };
        p.Normalize();
        Assert.Equal("nuit", p.Theme);
        Assert.Equal("Tous", p.HomeScope);
        Assert.Equal(0xA5, p.DictationKey);
    }

    [Fact]
    public void NormalizeKeepsCustomThemeAndValidScope()
    {
        var p = new Preferences { Theme = "custom", HomeScope = "games" };
        p.Normalize();
        Assert.Equal("custom", p.Theme);
        Assert.Equal("games", p.HomeScope);
    }

    [Fact]
    public void NormalizeTruncatesScratchAndCleansColorHistory()
    {
        var p = new Preferences { Scratch = new string('x', 25000) };
        p.ColorHistory = new List<string> { "#fff", "", "#fff", "  ", "#000" };
        p.Normalize();
        Assert.Equal(20000, p.Scratch.Length);
        Assert.Equal(new[] { "#fff", "#000" }, p.ColorHistory);
    }

    [Fact]
    public void NormalizeDropsInvalidPinsAndFixesTheKept()
    {
        var p = new Preferences();
        p.Pins = new List<PinNote>
        {
            new() { Kind = "image", ImagePath = "" },                                   // image sans fichier : retiree
            new() { Id = "", Kind = "bizarre", ImagePath = "b.png", Text = new string('a', 5000),
              Width = 1, Opacity = 0, Angle = 90, Left = double.NaN },
            new() { Kind = "image", ImagePath = "a.png", Width = double.NaN, Height = -5 },
        };
        p.Normalize();
        Assert.Equal(2, p.Pins.Count);
        var memo = p.Pins[0];
        Assert.Equal("memo", memo.Kind);
        Assert.False(string.IsNullOrWhiteSpace(memo.Id));
        Assert.Equal(4000, memo.Text.Length);
        Assert.Equal(160, memo.Width);
        Assert.Equal(.35, memo.Opacity);
        Assert.Equal(12, memo.Angle);
        Assert.Null(memo.Left);
        Assert.Equal(300, p.Pins[1].Width);
        Assert.Equal(0, p.Pins[1].Height);
    }

    [Fact]
    public void NormalizeFiltersWorkspacesAndDeduplicatesTargets()
    {
        var p = new Preferences();
        p.Workspaces = new List<WorkspaceProfile>
        {
            new() { Name = "  " },
            new() { Name = "Dev", Targets = new List<string> { "C:\\a.exe", "c:\\A.EXE", "", "b" } },
        };
        p.Normalize();
        var only = Assert.Single(p.Workspaces);
        Assert.Equal(new[] { "C:\\a.exe", "b" }, only.Targets);
    }

    [Fact]
    public void NormalizeRepairsQuickTabsAndDropsEmptyTargets()
    {
        var p = new Preferences();
        p.QuickTabs = new List<WindowsShortcut>
        {
            new() { Id = "", Name = "", Target = "glass:x", Glyph = "" },
            new() { Id = "z", Name = "Vide", Target = " " },
        };
        p.Normalize();
        var tab = Assert.Single(p.QuickTabs);
        Assert.False(string.IsNullOrEmpty(tab.Id));
        Assert.Equal("glass:x", tab.Name);
        Assert.Equal("\uE713", tab.Glyph);
    }

    [Fact]
    public void NormalizeRestoresDefaultBoardsWhenSetIsIncomplete()
    {
        var p = new Preferences();
        p.Boards = new List<BoardState> { new() { Id = "apps" } };
        p.Normalize();
        Assert.Equal(new[] { "apps", "games", "projects", "documents" }, p.Boards.Select(b => b.Id));
    }

    [Fact]
    public void NormalizeFixesBoardTitleAndInvalidAccent()
    {
        var p = new Preferences();
        p.Boards[1].Title = " ";
        p.Boards[1].Accent = "pas-une-couleur";
        p.Normalize();
        Assert.Equal("Jeux", p.Boards[1].Title);
        Assert.Equal("#91DFF5", p.Boards[1].Accent);
    }

    [Fact]
    public void NormalizeReplacesSettingsShortcutCollidingWithAnotherOne()
    {
        using var scope = new StoreScope();
        var p = new Preferences();
        p.CaptureShortcut = new ShortcutPreferences { Modifiers = 9, Key = 0x53 };
        p.Normalize();
        Assert.False(p.SettingsShortcut.Modifiers == p.CaptureShortcut.Modifiers && p.SettingsShortcut.Key == p.CaptureShortcut.Key);
    }

    [Fact]
    public void BoardForUsesOverrideThenSectionMapping()
    {
        using var scope = new StoreScope();
        Assert.Equal("apps", Store.BoardFor(E("a", "A", "Applications")));
        Assert.Equal("games", Store.BoardFor(E("b", "B", "Jeux")));
        Assert.Equal("projects", Store.BoardFor(E("c", "C", "Projets")));
        Assert.Equal("documents", Store.BoardFor(E("d", "D", "Autre")));
        Store.State.BoardOverrides["d"] = "games";
        Assert.Equal("games", Store.BoardFor(E("d", "D", "Autre")));
    }

    [Fact]
    public void VisibleFiltersHiddenMissingScopeAndAccentInsensitiveQuery()
    {
        using var scope = new StoreScope();
        Store.Entries = new List<Entry>
        {
            E("1", "Éditeur de texte", "Applications", "Outils"),
            E("2", "Jeu Cache", "Jeux"),
            E("3", "Jeu Disparu", "Jeux"),
            E("4", "Jeu Visible", "Jeux", target: "C:\\Games\\visible.exe"),
        };
        Store.State.Hidden.Add("2");
        Store.Missing.Add("3");
        Assert.Equal(new[] { "1", "4" }, Store.Visible().Select(e => e.Id));
        Assert.Equal(new[] { "4" }, Store.Visible("games").Select(e => e.Id));
        Assert.Equal(new[] { "1" }, Store.Visible(query: "EDITEUR outils").Select(e => e.Id));
        Assert.Equal(new[] { "4" }, Store.Visible(query: "visible.exe").Select(e => e.Id));
        Assert.Empty(Store.Visible(query: "editeur introuvable"));
    }

    [Fact]
    public void HomeFavoritesPutsRecentFirstThenFavoriteOrder()
    {
        using var scope = new StoreScope();
        Store.Entries = new List<Entry> { E("a", "A"), E("b", "B"), E("c", "C"), E("d", "D") };
        Store.State.Favorites = new List<string> { "a", "b", "c" };
        Store.State.Recent = new List<string> { "c", "a" };
        Assert.Equal(new[] { "c", "a", "b" }, Store.HomeFavorites().Select(e => e.Id));
    }

    [Fact]
    public void RecordLaunchMovesToFrontCapsRecentAndCountsLaunches()
    {
        using var scope = new StoreScope();
        for (int i = 0; i < 60; i++)
            Store.RecordLaunch("id" + i);
        Store.RecordLaunch("id30");
        Assert.Equal(48, Store.State.Recent.Count);
        Assert.Equal("id30", Store.State.Recent[0]);
        Assert.Equal(1, Store.State.Recent.Count(x => x == "id30"));
        Assert.Equal(2, Store.State.LaunchCounts["id30"]);
        Store.State.LaunchCounts["max"] = int.MaxValue;
        Store.RecordLaunch("max");
        Assert.Equal(int.MaxValue, Store.State.LaunchCounts["max"]);
    }

    [Fact]
    public void RecentEntriesFollowsRecentOrderAndSortedHonoursOrderFavoritesThenName()
    {
        using var scope = new StoreScope();
        var a = E("a", "Zed"); var b = E("b", "alpha"); var c = E("c", "Beta"); var d = E("d", "Gamma");
        Store.Entries = new List<Entry> { a, b, c, d };
        Store.State.Recent = new List<string> { "b", "a" };
        Assert.Equal(new[] { "b", "a" }, Store.RecentEntries().Select(e => e.Id));
        Store.State.Order = new List<string> { "d" };
        Store.State.Favorites = new List<string> { "c" };
        // d (ordre explicite) puis favori c, puis le reste par nom sans casse.
        Assert.Equal(new[] { "d", "c", "b", "a" }, Store.Sorted(Store.Entries).Select(e => e.Id));
    }

    [Fact]
    public void ReadReturnsDefaultForMissingOrCorruptFile()
    {
        using var dir = new TempDir();
        using var scope = new StoreScope(dir.Path);
        Assert.Empty(Store.Read<Catalogue>(dir.Combine("absent.json")).Entries);
        string bad = dir.Combine("bad.json");
        File.WriteAllText(bad, "{ pas du json");
        Assert.Empty(Store.Read<Catalogue>(bad).Entries);
        Assert.Contains("bad.json", File.ReadAllText(dir.Combine("glass.log")));
    }

    [Fact]
    public void SaveThenLoadRoundTripsPreferencesAndKeepsBackupOnSecondSave()
    {
        using var dir = new TempDir();
        using var scope = new StoreScope(dir.Path);
        Store.State.Favorites = new List<string> { "x", "y" };
        Store.State.Names["x"] = "Nom perso";
        Store.State.Theme = "custom";
        Store.State.GlassOpacity = .5;
        Store.Save();
        Store.State.Favorites.Add("z");
        Store.Save();
        Assert.True(File.Exists(dir.Combine("preferences-glass.json.bak")));
        Store.State = new Preferences();
        Store.Load();
        Assert.Equal(new[] { "x", "y", "z" }, Store.State.Favorites);
        Assert.Equal("Nom perso", Store.State.Names["x"]);
        Assert.Equal("custom", Store.State.Theme);
        Assert.Equal(.5, Store.State.GlassOpacity);
        Assert.Equal(Store.SettingsSchemaCurrent, Store.State.SettingsSchema);
    }

    [Fact]
    public void LoadClampsOutOfRangeValuesFromDisk()
    {
        using var dir = new TempDir();
        using var scope = new StoreScope(dir.Path);
        File.WriteAllText(dir.Combine("preferences-glass.json"), "{\"FrameThickness\": 500, \"Gap\": 1, \"Theme\": \"zzz\"}");
        Store.Load();
        Assert.Equal(40, Store.State.FrameThickness);
        Assert.Equal(12, Store.State.Gap);
        Assert.Equal("nuit", Store.State.Theme);
    }
}
