// Analyse pure du JSON Chromium et association des icônes du cache : aucun navigateur ni profil réel.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PanneauxGlass;
namespace Winlestia.Tests;
public class Bookmarks3Tests
{
    [Fact]
    public void ParseChromiumAddsOtherAndMobileFoldersOnlyWhenNotEmpty()
    {
        string json = "{\"roots\":{\"bookmark_bar\":{\"children\":[]},\"other\":{\"children\":["
            + "{\"type\":\"url\",\"name\":\"A\",\"url\":\"https://a.test\",\"guid\":\"g1\"}]},\"synced\":{\"children\":[]}}}";
        var r = BookmarksCore.ParseChromium(Encoding.UTF8.GetBytes(json), "edge");
        var only = Assert.Single(r);
        Assert.Equal("edge:other", only.Id);
        Assert.Equal("edge:g1", only.Children[0].Id);
    }

    [Fact]
    public void ParseChromiumWithoutRootsIsEmpty() => Assert.Empty(BookmarksCore.ParseChromium(Encoding.UTF8.GetBytes("{}"), "edge"));

    [Fact]
    public void ParseChromiumStopsBeyondMaxDepth()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 28; i++)
            sb.Append("{\"type\":\"folder\",\"name\":\"d\",\"guid\":\"f" + i + "\",\"children\":[");
        sb.Append(new string(']', 28).Replace("]", "]}"));
        var r = BookmarksCore.ParseChromium(Encoding.UTF8.GetBytes("{\"roots\":{\"bookmark_bar\":{\"children\":[" + sb + "]}}}"), "c");
        int depth = 0;
        for (var n = r.SingleOrDefault(); n != null && n.Children.Count > 0; n = n.Children[0])
            depth++;
        Assert.InRange(depth, 1, 26);
    }

    [Fact]
    public void AssignOnlyLinksIconsPresentInCache()
    {
        using var dir = new TempDir();
        string host = "a.test";
        File.WriteAllBytes(dir.Combine(BookmarksCore.Hash(host) + ".png"), new byte[] { 1 });
        var withIcon = new BNode { Url = "https://a.test/x" };
        var without = new BNode { Url = "https://b.test/" };
        var local = new BNode { Url = "file:///c:/x" };
        var folder = new BNode { Folder = true, Children = new List<BNode> { withIcon, without, local } };
        BookmarksCore.Assign(new[] { folder }, dir.Path);
        Assert.StartsWith("file:///", withIcon.Icon);
        Assert.Equal("", without.Icon);
        Assert.Equal("", local.Icon);
    }

    [Fact]
    public void CollectKeepsFirstUrlPerHostAndSkipsNonWeb()
    {
        var nodes = new[] { new BNode { Url = "https://A.test/1" }, new BNode { Url = "https://a.test/2" }, new BNode { Url = "ftp://x/" } };
        var hosts = new Dictionary<string, string>();
        BookmarksCore.Collect(nodes, hosts);
        Assert.Equal("https://A.test/1", Assert.Single(hosts).Value);
    }
}
