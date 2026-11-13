// C#12 / Tests du validateur Winjet : paquet valide, rejets, paquets fournis avec l'application.
using System;
using System.IO;
using System.Text.Json.Nodes;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class WinjetValidatorTests
{
    static string Run(Pkg p, bool zip = true)
    {
        using var t = new TempDir();
        string path = zip ? p.WriteZip(t.Combine("p.zip")) : p.WriteFolder(Directory.CreateDirectory(t.Combine("d")).FullName);
        using var v = WinjetValidator.Validate(path, Pkg.Current, t.Combine("tmp"));
        return v.Ok ? "" : string.Join("\n", v.Errors);
    }

    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Winlestia.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    static JsonNode Child(JsonObject o, int i) => o["layouts"]![0]!["root"]!["children"]![i]!;

    [Fact]
    public void GoodPackageZipAndFolder()
    {
        Assert.Equal("", Run(Pkg.Default()));
        Assert.Equal("", Run(Pkg.Default(), false));
    }

    [Fact]
    public void UnknownPropertiesRejected()
    {
        Assert.Contains("extra", Run(Pkg.Default().Edit("manifest.json", o => o["extra"] = 1)));
        Assert.Contains("extra", Run(Pkg.Default().Edit("widget.json", o => o["extra"] = 1)));
        Assert.Contains("bogus", Run(Pkg.Default().Edit("widget.json", o => Child(o, 0)["bogus"] = 1)));
    }

    [Theory]
    [InlineData("Com.Test")]
    [InlineData("nodots")]
    [InlineData("com.test..x")]
    public void BadIdRejected(string id) => Assert.NotEqual("", Run(Pkg.Default(id)));

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("01.0.0")]
    public void BadVersionRejected(string v) => Assert.NotEqual("", Run(Pkg.Default("com.test.demo", v)));

    [Fact]
    public void IncompatibleVersionRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("manifest.json", o => o["compatibility"]!["winlestia"] = ">=99.0.0")));

    [Fact]
    public void LiteralColorRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("widget.json", o => Child(o, 3)["color"] = "#ff0000")));

    [Fact]
    public void ExpressionAcceptedInEnum()
        => Assert.Equal("", Run(Pkg.Default().Edit("widget.json", o => Child(o, 1)["variant"] = "{{ settings.range }}")));

    [Fact]
    public void BadEnumRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("widget.json", o => Child(o, 0)["style"] = "gigantic")));

    [Fact]
    public void UndeclaredHostRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("widget.json", o => o["data"]!["live"]!["url"] = "https://evil.example.org/x")));

    [Fact]
    public void HttpUrlRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("widget.json", o => o["data"]!["live"]!["url"] = "http://api.example.com/x")));

    [Fact]
    public void UndeclaredStateKeyInActionRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("widget.json", o => Child(o, 5)["action"]!["key"] = "nope")));

    [Fact]
    public void BadExtensionRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Set("assets/run.exe", "MZ")));

    [Fact]
    public void RootStrayFileRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Set("README.md", "x")));

    [Fact]
    public void FakeImageRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Set("assets/logo.png", "not a png")));

    [Fact]
    public void MissingImageAssetRejected()
    {
        var p = Pkg.Default();
        p.Files.Remove("assets/logo.png");
        Assert.NotEqual("", Run(p));
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("assets/../../evil.json")]
    [InlineData("C:/evil.json")]
    [InlineData("assets/con.json")]
    [InlineData("/abs.json")]
    public void PathTraversalRejected(string name) => Assert.NotEqual("", Run(Pkg.Default().Set(name, "{}")));

    [Fact]
    public void ReservedNameWithSpaceBeforeExtensionRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Set("assets/CON .txt", "x")));

    [Fact]
    public void PreviewDimensionsCheckedCaseInsensitively()
    {
        var p = Pkg.Default();
        p.Files.Remove("preview.png");
        Assert.NotEqual("", Run(p.Set("Preview.PNG", Pkg.Png(3000, 3000))));
    }

    [Fact]
    public void HugeAssetImageRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Set("assets/logo.png", Pkg.Png(5000, 5000))));

    [Fact]
    public void TooManyEntriesRejected()
    {
        var p = Pkg.Default();
        for (int i = 0; i < 210; i++)
            p.Files[$"assets/f{i}.json"] = new byte[] { (byte)'{', (byte)'}' };
        Assert.NotEqual("", Run(p));
    }

    [Fact]
    public void ZipBombRejected()
    {
        // 30 Mio de zéros compressent en quelques Kio : dépasse la limite décompressée (20 Mio).
        var p = Pkg.Default().Set("assets/big.json", new byte[30 * 1024 * 1024]);
        Assert.NotEqual("", Run(p));
    }

    [Fact]
    public void NotAZipRejected()
    {
        using var t = new TempDir();
        string f = t.Combine("x.zip");
        File.WriteAllText(f, "pas un zip");
        using var v = WinjetValidator.Validate(f, Pkg.Current, t.Combine("tmp"));
        Assert.False(v.Ok);
    }

    [Fact]
    public void ManifestMissingRejected()
    {
        var p = Pkg.Default();
        p.Files.Remove("manifest.json");
        Assert.NotEqual("", Run(p));
    }

    [Fact]
    public void SizeLimitsRejected()
        => Assert.NotEqual("", Run(Pkg.Default().Edit("widget.json", o => o["size"]!["min"] = new JsonArray(900, 900))));

    [Fact]
    public void ShippedPackagesAreValid()
    {
        string root = Root();
        string builtin = Path.Combine(root, "src", "winjets", "builtin");
        foreach (string id in new[] { "winlestia.lecteur", "winlestia.systeme", "winlestia.marches" })
        {
            string dir = Path.Combine(builtin, id);
            Assert.True(Directory.Exists(dir), "paquet fourni absent : " + id);
            using var v = WinjetValidator.ValidateDirectory(dir, WinjetValidator.CurrentVersion());
            Assert.True(v.Ok, id + " : " + string.Join(" | ", v.Errors));
        }
        string sample = Path.Combine(root, "docs", "winjets", "examples", "com.exemple.citation-1.0.0.zip");
        Assert.True(File.Exists(sample), "exemple absent : " + sample);
        using var t = new TempDir();
        using var vs = WinjetValidator.Validate(sample, WinjetValidator.CurrentVersion(), t.Combine("tmp"));
        Assert.True(vs.Ok, "citation : " + string.Join(" | ", vs.Errors));
    }
}
