// Favoris : lecture Firefox sur une base places.sqlite temporaire, requêtes de favicons, profil par défaut, détection du navigateur.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PanneauxGlass;
namespace Winlestia.Tests;
public class Bookmarks2Tests
{
    static void Exec(string file, params string[] sql)
    {
        using (var c = new SqliteConnection("Data Source=" + file + ";Pooling=False"))
        {
            c.Open();
            foreach (var s in sql)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = s;
                cmd.ExecuteNonQuery();
            }
        }
        SqliteConnection.ClearAllPools();
    }

    const string PlacesSchema1 = "CREATE TABLE moz_places(id INTEGER PRIMARY KEY, url TEXT)";
    const string PlacesSchema2 = "CREATE TABLE moz_bookmarks(id INTEGER PRIMARY KEY, type INT, parent INT, title TEXT, guid TEXT, "
        + "fk INT, position INT)";

    [Fact]
    public void FirefoxTreeFollowsToolbarMenuAndUnfiled()
    {
        using var dir = new TempDir();
        string db = dir.Combine("places.sqlite");
        Exec(db, PlacesSchema1, PlacesSchema2,
            "INSERT INTO moz_places VALUES(1,'https://a.test/'),(2,'place:sort=8'),(3,'https://b.test/')",
            "INSERT INTO moz_bookmarks VALUES(10,2,0,'','toolbar_____',NULL,0),(11,2,0,'','menu________',NULL,1),"
            + "(12,2,0,'','unfiled_____',NULL,2),(20,1,10,'A','ga',1,0),(21,1,10,'Smart','gs',2,1),(22,2,10,'Dossier','gd',NULL,2),"
            + "(23,1,22,'B','gb',3,0),(24,1,11,'M','gm',3,0)");
        var tree = BookmarksCore.ParseFirefox(db, "firefox");
        Assert.Equal(new[] { "A", "Dossier", "Autres favoris" }, tree.Select(n => n.Title));
        Assert.Equal("firefox:ga", tree[0].Id);
        Assert.Equal("B", tree[1].Children.Single().Title);
        Assert.Equal("firefox:other", tree[2].Id);
        Assert.Equal(3, BookmarksCore.CountUrls(tree));
    }

    [Fact]
    public void FirefoxWithoutKnownRootsIsEmpty()
    {
        using var dir = new TempDir();
        string db = dir.Combine("places.sqlite");
        Exec(db, PlacesSchema1, PlacesSchema2);
        Assert.Empty(BookmarksCore.ParseFirefox(db, "firefox"));
    }

    [Fact]
    public void HostOnlyForHttpUrls()
    {
        Assert.Equal("example.com", BookmarksCore.HostOf("https://Example.COM/x?y"));
        Assert.Equal("", BookmarksCore.HostOf("ftp://example.com"));
        Assert.Equal("", BookmarksCore.HostOf("pas une url"));
    }

    [Fact]
    public void CollectKeepsFirstUrlPerHostAcrossFolders()
    {
        var folder = new BNode { Folder = true };
        folder.Children.Add(new BNode { Url = "https://a.test/2" });
        folder.Children.Add(new BNode { Url = "https://b.test/" });
        folder.Children.Add(new BNode { Url = "file:///x" });
        var tree = new List<BNode> { new() { Url = "https://a.test/1" }, folder };
        var hosts = new Dictionary<string, string>();
        BookmarksCore.Collect(tree, hosts);
        Assert.Equal(2, hosts.Count);
        Assert.Equal("https://a.test/1", hosts["a.test"]);
    }

    [Fact]
    public void ChromiumIconQueryFallsBackToHostAndPicksClosestSize()
    {
        using var dir = new TempDir();
        string db = dir.Combine("Favicons");
        Exec(db,
            "CREATE TABLE icon_mapping(page_url TEXT, icon_id INT)",
            "CREATE TABLE favicon_bitmaps(icon_id INT, width INT, image_data BLOB)",
            "INSERT INTO icon_mapping VALUES('https://a.test/page',1)",
            "INSERT INTO favicon_bitmaps VALUES(1,16,x'0102'),(1,32,x'0304'),(1,256,x'0506')");
        using var c = new SqliteConnection("Data Source=" + db + ";Pooling=False");
        c.Open();
        Assert.Equal(new byte[] { 3, 4 }, BookmarksCore.Query(c, true, "https://a.test/page", "a.test"));
        Assert.Equal(new byte[] { 3, 4 }, BookmarksCore.Query(c, true, "https://a.test/autre", "a.test"));
        Assert.Null(BookmarksCore.Query(c, true, "https://zzz.test/", "zzz.test"));
        Assert.Null(BookmarksCore.Query(c, false, "https://a.test/", "a.test"));   // schéma Firefox absent : erreur avalée
    }

    [Fact]
    public void FirefoxIconQueryIgnoresHugeIcons()
    {
        using var dir = new TempDir();
        string db = dir.Combine("favicons.sqlite");
        Exec(db,
            "CREATE TABLE moz_pages_w_icons(id INTEGER PRIMARY KEY, page_url TEXT)",
            "CREATE TABLE moz_icons_to_pages(page_id INT, icon_id INT)",
            "CREATE TABLE moz_icons(id INTEGER PRIMARY KEY, data BLOB, width INT)",
            "INSERT INTO moz_pages_w_icons VALUES(1,'https://a.test/')",
            "INSERT INTO moz_icons_to_pages VALUES(1,1),(1,2)",
            "INSERT INTO moz_icons VALUES(1,x'AA',2048),(2,x'BB',32)");
        using var c = new SqliteConnection("Data Source=" + db + ";Pooling=False");
        c.Open();
        Assert.Equal(new byte[] { 0xBB }, BookmarksCore.Query(c, false, "https://a.test/", "a.test"));
    }

    static byte[] Png(int size)
    {
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
            new byte[size * size * 4], size * 4);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void PngIsRescaledTo32AndGarbageRefused()
    {
        var png = BookmarksCore.ToPng32(Png(64));
        Assert.NotNull(png);
        using var ms = new MemoryStream(png!);
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(32, frame.PixelWidth);
        Assert.Null(BookmarksCore.ToPng32(Png(4)));
        Assert.Null(BookmarksCore.ToPng32(new byte[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData("MSEdgeHTM", "edge")]
    [InlineData("ChromeHTML", "chrome")]
    [InlineData("BraveHTML", "brave")]
    [InlineData("FirefoxURL-308046B0AF4A39CB", "firefox")]
    [InlineData("VivaldiHTM.ABC", "vivaldi")]
    [InlineData("OperaGXStable", "operagx")]
    [InlineData("OperaStable", "opera")]
    [InlineData("AutreHTML", "")]
    public void ProgIdMapsToBrowser(string prog, string id) => Assert.Equal(id, BookmarksCore.BrowserIdFromProgId(prog));

    [Fact]
    public void FirefoxProfilePrefersInstallDefaultThenDefaultFlagThenAnyWithPlaces()
    {
        using var dir = new TempDir();
        Assert.Equal("", BookmarksCore.FirefoxProfileIn(dir.Path));             // pas de profiles.ini
        foreach (string name in new[] { "inst", "def", "any" })
            Directory.CreateDirectory(dir.Combine("Profiles", name));
        File.WriteAllText(dir.Combine("Profiles", "any", "places.sqlite"), "");
        File.WriteAllText(dir.Combine("profiles.ini"), "[Profile0]\nName=x\nIsRelative=1\nPath=Profiles/any\n");
        Assert.Equal(Path.GetFullPath(dir.Combine("Profiles", "any")), BookmarksCore.FirefoxProfileIn(dir.Path));
        File.WriteAllText(dir.Combine("profiles.ini"),
            "[Profile0]\nIsRelative=1\nPath=Profiles/def\nDefault=1\n[Profile1]\nIsRelative=1\nPath=Profiles/any\n");
        Assert.Equal(Path.GetFullPath(dir.Combine("Profiles", "def")), BookmarksCore.FirefoxProfileIn(dir.Path));
        File.WriteAllText(dir.Combine("profiles.ini"),
            "; commentaire\n[Install1234]\nDefault=Profiles/inst\n[Profile0]\nIsRelative=1\nPath=Profiles/def\nDefault=1\n");
        Assert.Equal(Path.GetFullPath(dir.Combine("Profiles", "inst")), BookmarksCore.FirefoxProfileIn(dir.Path));
    }
}
