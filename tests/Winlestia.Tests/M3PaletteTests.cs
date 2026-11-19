// Maths de couleur et palette M3 (Theme/Tokens.cs, Theme/Themes.cs) : luminance, melange, palette personnalisee, themes.
// Struct Color uniquement : aucun element WPF visuel n'est cree, donc pas de thread STA requis.
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using PanneauxGlass;
namespace Winlestia.Tests;
public class M3PaletteTests
{
    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    static double Contrast(Color a, Color b)
    {
        double la = M3Palette.Lum(a), lb = M3Palette.Lum(b);
        return (System.Math.Max(la, lb) + .05) / (System.Math.Min(la, lb) + .05);
    }

    [Fact]
    public void LuminanceBoundsAreBlackZeroAndWhiteOne()
    {
        Assert.Equal(0, M3Palette.Lum(Colors.Black), 9);
        Assert.Equal(1, M3Palette.Lum(Colors.White), 4);
    }

    [Fact]
    public void LuminanceWeighsGreenAboveRedAboveBlue()
    {
        double r = M3Palette.Lum(Rgb(255, 0, 0)), g = M3Palette.Lum(Rgb(0, 255, 0)), b = M3Palette.Lum(Rgb(0, 0, 255));
        Assert.True(g > r && r > b);
        Assert.Equal(.2126, r, 4);
        Assert.Equal(.7152, g, 4);
        Assert.Equal(.0722, b, 4);
    }

    [Fact]
    public void LuminanceUsesTheLinearSegmentBelowTheSrgbKnee()
    {
        // 10/255 est sous le seuil .04045 : branche lineaire d / 12.92.
        Assert.Equal(10 / 255.0 / 12.92 * (.2126 + .7152 + .0722), M3Palette.Lum(Rgb(10, 10, 10)), 9);
    }

    [Theory]
    [InlineData(0.0, 10, 10, 10)]
    [InlineData(1.0, 200, 100, 50)]
    [InlineData(.5, 105, 55, 30)]
    public void MixInterpolatesEachChannel(double t, int r, int g, int b)
    {
        var mixed = Theme.Mix(Rgb(10, 10, 10), Rgb(200, 100, 50), t);
        Assert.Equal(Rgb((byte)r, (byte)g, (byte)b), mixed);
    }

    [Fact]
    public void MixAtHalfBetweenBlackAndWhiteRoundsTo128()
    {
        // 127,5 : Math.Round arrondit au pair le plus proche (128), pas a 127.
        Assert.Equal(Rgb(128, 128, 128), Theme.Mix(Colors.Black, Colors.White, .5));
    }

    [Fact]
    public void CustomDarkBackgroundGivesDarkPaletteWithDarkErrorColours()
    {
        var p = M3Palette.Custom(new Dictionary<string, string> { ["background"] = "#101010", ["text"] = "#f0f0f0" });
        Assert.True(p.Dark);
        Assert.Equal(Rgb(0x10, 0x10, 0x10), p.Surface);
        Assert.Equal(Rgb(0xf0, 0xf0, 0xf0), p.OnSurface);
        Assert.Equal(Rgb(0xff, 0xb4, 0xab), p.Error);
        Assert.Equal(Rgb(0x69, 0x00, 0x05), p.OnError);
    }

    [Fact]
    public void CustomLightBackgroundGivesLightPalette()
    {
        var p = M3Palette.Custom(new Dictionary<string, string> { ["background"] = "#ffffff", ["text"] = "#000000" });
        Assert.False(p.Dark);
        Assert.Equal(Rgb(0xb3, 0x26, 0x1e), p.Error);
        Assert.Equal(Colors.White, p.OnError);
    }

    [Fact]
    public void CustomSurfaceLayersMoveMonotonicallyTowardsTheText()
    {
        var p = M3Palette.Custom(new Dictionary<string, string> { ["background"] = "#ffffff", ["text"] = "#000000" });
        var layers = new[] { p.Surface, p.SurfaceLow, p.SurfaceContainer, p.SurfaceHigh, p.SurfaceHighest };
        for (int i = 1; i < layers.Length; i++)
            Assert.True(layers[i].R < layers[i - 1].R, "couche " + i + " plus sombre que " + (i - 1));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("#fff")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void CustomInvalidColourFallsBackToTheDefaultWithoutThrowing(string bad)
    {
        var p = M3Palette.Custom(new Dictionary<string, string> { ["background"] = bad, ["primary"] = bad });
        Assert.Equal(Rgb(0xff, 0xf8, 0xf6), p.Surface);
        Assert.Equal(Rgb(0x89, 0x51, 0x43), p.Primary);
        Assert.False(p.Dark);
    }

    [Fact]
    public void CustomEmptyDictionaryEqualsAllDefaults()
    {
        var p = M3Palette.Custom(new Dictionary<string, string>());
        Assert.Equal(Rgb(0x26, 0x19, 0x16), p.OnSurface);
        Assert.Equal(Rgb(0x77, 0x5b, 0x52), p.Secondary);
    }

    [Fact]
    public void CustomDarkThresholdIsLuminanceBelow035()
    {
        // #808080 (lum ~ .216) est « sombre » ; #c0c0c0 (lum ~ .527) est « clair ».
        Assert.True(M3Palette.Custom(new Dictionary<string, string> { ["background"] = "#808080" }).Dark);
        Assert.False(M3Palette.Custom(new Dictionary<string, string> { ["background"] = "#c0c0c0" }).Dark);
    }

    [Fact]
    public void CustomForegroundOnPrimaryStaysReadable()
    {
        foreach (string primary in new[] { "#000000", "#ffffff", "#895143", "#ffd54f", "#1a237e", "#00e676", "#808080" })
        {
            var p = M3Palette.Custom(new Dictionary<string, string> { ["primary"] = primary });
            Assert.True(Contrast(p.Primary, p.OnPrimary) >= 4.5, "contraste insuffisant pour " + primary);
        }
    }

    [Fact]
    public void ThemeCatalogueIdsAreUniqueAndFindIsCaseInsensitive()
    {
        var ids = Theme.Catalogue.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        Assert.Same(Theme.Find("shell"), Theme.Find("SHELL"));
        Assert.Null(Theme.Find("n-existe-pas"));
        Assert.Null(Theme.Find(null));
    }

    [Fact]
    public void EveryCatalogueThemeBuildsAPaletteConsistentWithItsLightFlag()
    {
        foreach (var t in Theme.Catalogue)
        {
            var p = M3Palette.FromTheme(t);
            Assert.Equal(!t.Light, p.Dark);
            Assert.Equal(t.Surface, p.Surface);
            Assert.Equal(t.Text, p.OnSurface);
            var ratio = Contrast(p.Primary, p.OnPrimary);
            Assert.True(ratio >= 3, "OnPrimary illisible sur le theme " + t.Id + " : " + ratio);
        }
    }
}
