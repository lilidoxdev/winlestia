// Lecture et suppression de favoris Chromium sur un faux profil temporaire (fichier Bookmarks + Preferences) :
// arbre produit, identifiants stables, sauvegarde avant ecriture, refus des cas ambigus ou synchronises.
using System.IO;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BookmarksCoreTests
{
    const string Sample = """
    {
      "checksum": "abc",
      "roots": {
        "bookmark_bar": { "children": [
          { "type": "url", "name": "Rust", "url": "https://rust-lang.org", "guid": "g-rust" },
          { "type": "folder", "name": "Dev", "guid": "g-dev", "children": [
            { "type": "url", "name": "Doc", "url": "https://learn.microsoft.com", "guid": "g-doc" },
            { "type": "url", "name": "Vide", "url": "", "guid": "g-vide" },
            { "type": "separator", "name": "x" }
          ] }
        ] },
        "other": { "children": [ { "type": "url", "name": "Sans guid", "url": "https://example.org" } ] },
        "synced": { "children": [] }
      }
    }
    """;

    static BrowserInfo Fake(TempDir dir, string bookmarks = Sample)
    {
        string profile = dir.Combine("Profile");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "Bookmarks"), bookmarks);
        return new BrowserInfo { Id = "chrome", Name = "Chrome", Chromium = true, ProfileDir = profile };
    }

    [Fact]
    public void ReadChromiumBuildsTreeSkippingEmptyUrlsAndUnknownTypes()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        var tree = BookmarksCore.Read(b);
        Assert.Equal(new[] { "Rust", "Dev", "Autres favoris" }, tree.Select(n => n.Title));
        var dev = tree[1];
        Assert.True(dev.Folder);
        Assert.Equal("chrome:g-dev", dev.Id);
        Assert.Equal(new[] { "Doc" }, dev.Children.Select(n => n.Title));
        Assert.Equal("chrome:g-rust", tree[0].Id);
        Assert.Equal(2 + 1, BookmarksCore.CountUrls(tree));
        Assert.Equal("chrome:other", tree[2].Id);
    }

    [Fact]
    public void NodeWithoutGuidGetsStableContentDerivedId()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        var first = BookmarksCore.Read(b)[2].Children[0].Id;
        var second = BookmarksCore.Read(b)[2].Children[0].Id;
        Assert.StartsWith("chrome:u", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DeleteChromiumRemovesNodeBacksUpAndDropsChecksum()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        string backups = dir.Combine("backups");
        Assert.Null(BookmarksCore.DeleteChromium(b, "chrome:g-doc", true, backups, skipChecks: true));
        var tree = BookmarksCore.Read(b);
        Assert.Empty(tree[1].Children);
        Assert.Equal(2, BookmarksCore.CountUrls(tree));
        Assert.DoesNotContain("checksum", File.ReadAllText(b.BookmarksFile));
        var backup = Assert.Single(Directory.GetFiles(backups));
        Assert.Contains("g-doc", File.ReadAllText(backup));
    }

    [Theory]
    [InlineData("chrome:inconnu")]
    [InlineData("firefox:g-rust")]
    [InlineData("chrome:other")]
    [InlineData("chrome:synced")]
    public void DeleteChromiumReportsNotFoundForUnknownOrRootIds(string id)
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        string before = File.ReadAllText(b.BookmarksFile);
        Assert.Equal("notfound", BookmarksCore.DeleteChromium(b, id, true, dir.Combine("backups"), skipChecks: true));
        Assert.Equal(before, File.ReadAllText(b.BookmarksFile));
        Assert.False(Directory.Exists(dir.Combine("backups")));
    }

    [Fact]
    public void DeleteChromiumRefusesNodesWithoutGuidAsAmbiguous()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        string id = BookmarksCore.Read(b)[2].Children[0].Id;
        Assert.Equal("ambiguous", BookmarksCore.DeleteChromium(b, id, true, dir.Combine("backups"), skipChecks: true));
    }

    [Fact]
    public void DeleteChromiumRefusesDuplicatedGuids()
    {
        using var dir = new TempDir();
        const string dup = """
        {"roots":{"bookmark_bar":{"children":[
          {"type":"url","name":"A","url":"http://a","guid":"g"},{"type":"url","name":"B","url":"http://b","guid":"g"}]}}}
        """;
        var b = Fake(dir, dup);
        Assert.Equal("ambiguous", BookmarksCore.DeleteChromium(b, "chrome:g", true, dir.Combine("backups"), skipChecks: true));
    }

    [Fact]
    public void DeleteIsUnsupportedForFirefox()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        b.Chromium = false;
        Assert.Equal("unsupported", BookmarksCore.DeleteChromium(b, "chrome:g-rust", true, dir.Combine("backups")));
    }

    [Fact]
    public void DeleteAsksForConfirmationWhenSyncIsActive()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        File.WriteAllText(Path.Combine(b.ProfileDir, "Preferences"), """{"sync":{"has_setup_completed":true}}""");
        Assert.Equal("sync", BookmarksCore.DeleteChromium(b, "chrome:g-rust", false, dir.Combine("backups")));
        Assert.Null(BookmarksCore.DeleteChromium(b, "chrome:g-rust", true, dir.Combine("backups")));
    }

    [Fact]
    public void SyncActiveDetectsAccountsAndFlagsButNotOfflineProfiles()
    {
        using var dir = new TempDir();
        var b = Fake(dir);
        Assert.False(BookmarksCore.SyncActive(b));                         // pas de fichier Preferences
        string prefs = Path.Combine(b.ProfileDir, "Preferences");
        File.WriteAllText(prefs, """{"sync":{"requested":false}}""");
        Assert.False(BookmarksCore.SyncActive(b));
        File.WriteAllText(prefs, """{"account_info":[{"email":"a@b"}]}""");
        Assert.True(BookmarksCore.SyncActive(b));
        File.WriteAllText(prefs, "{ illisible");
        Assert.True(BookmarksCore.SyncActive(b));                          // prudence : on demande confirmation
    }
}
