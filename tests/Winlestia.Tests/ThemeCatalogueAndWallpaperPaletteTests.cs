// Catalogue de themes (identifiants, roles, familles), palette derivee d'un theme et palette extraite d'un fond d'ecran
// (images PNG synthetiques ecrites dans un dossier temporaire) : tout est pur, aucun bureau requis.
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanneauxGlass;
namespace Winlestia.Tests;
public class ThemeCatalogueTests
{
    [Fact]
    public void CatalogueIdsAreUniqueAndFindIsCaseInsensitive()
    {
        var ids = Theme.Catalogue.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("nuit", ids);
        Assert.Same(Theme.Find("nuit"), Theme.Find("NUIT"));
        Assert.Null(Theme.Find("n-existe-pas"));
        Assert.Null(Theme.Find(null));
    }

    [Fact]
    public void EveryThemeHasReadableTextOnItsSurface()
    {
        foreach (var t in Theme.Catalogue)
        {
            double a = M3Palette.Lum(t.Text), b = M3Palette.Lum(t.Surface);
            double ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            Assert.True(ratio >= 4.5, $"{t.Id} : contraste texte/surface {ratio:0.0}");
        }
    }

    [Fact]
    public void ThemePaletteFollowsLightFlagAndRoleLookupMatchesPaletteFields()
    {
        foreach (var t in Theme.Catalogue)
        {
            var p = t.P;
            Assert.Equal(!t.Light, p.Dark);
            Assert.Equal(t.Surface, p.Surface);
            Assert.Equal(t.Accent, p.Primary);
            Assert.Equal(p.Surface, t.Role(M3Role.Surface));
            Assert.Equal(p.OnSurface, t.Role(M3Role.OnSurface));
            Assert.Equal(p.Primary, t.Role(M3Role.Primary));
            Assert.Equal(p.OnError, t.Role(M3Role.OnError));
            Assert.Equal(p.Error, t.Role(M3Role.Error));
            Assert.Equal(p.OutlineVariant, t.Role(M3Role.OutlineVariant));
        }
    }

    [Fact]
    public void EveryRoleResolvesToADistinctPaletteSlotForTheDefaultDarkPalette()
    {
        var t = Theme.Find("nuit")!;
        foreach (var role in Enum.GetValues<M3Role>())
            _ = t.Role(role);                                    // aucun role sans correspondance
        Assert.NotEqual(t.Role(M3Role.Surface), t.Role(M3Role.SurfaceHighest));
        Assert.NotEqual(t.Role(M3Role.Primary), t.Role(M3Role.OnPrimary));
    }

    [Theory]
    [InlineData("Glass", "Verre")]
    [InlineData("Paper", "Papier")]
    [InlineData("Ink", "Encre")]
    [InlineData("Poster", "Affiche")]
    [InlineData("Soft", "Brume")]
    [InlineData("Bento", "Bento")]
    [InlineData("Neon", "Néon")]
    [InlineData("Retro", "Rétro")]
    [InlineData("Swiss", "Suisse")]
    [InlineData("Zen", "Zen")]
    public void FamilyNameFollowsSurfaceKindAndOnlyGlassIsTranslucent(string kindName, string family)
    {
        var kind = Enum.Parse<SurfaceKind>(kindName);
        var t = new ThemeDefinition { Kind = kind };
        Assert.Equal(family, t.Family);
        Assert.Equal(kind != SurfaceKind.Glass, t.Opaque);
        Assert.False(t.Plated);
        t.Light = true;
        Assert.Equal(kind != SurfaceKind.Glass, t.Plated);       // plaque = opaque ET clair
    }

    [Fact]
    public void HaloAppliesGainClampedToByteRange()
    {
        var t = new ThemeDefinition { HaloA = Color.FromRgb(1, 2, 3), HaloB = Color.FromRgb(4, 5, 6), HaloGain = 2 };
        Assert.Equal(Color.FromArgb(100, 1, 2, 3), t.Halo(50, 0));
        Assert.Equal(Color.FromArgb(255, 4, 5, 6), t.Halo(200, 1));
        t.HaloGain = 0;
        Assert.Equal(0, t.Halo(200, 0).A);
    }

    [Fact]
    public void M3TokensMapLegacyRadiiToThreeSteps()
    {
        Assert.Equal(12, M3.Round(10));
        Assert.Equal(12, M3.Round(13));
        Assert.Equal(17, M3.Round(14));
        Assert.Equal(17, M3.Round(19));
        Assert.Equal(25, M3.Round(20));
        Assert.Equal(16, M3.Pt(12), 6);
    }
}

public class WallpaperPaletteTests
{
    static string Png(TempDir dir, string name, Func<int, int, Color> pixel)
    {
        const int size = 32;
        var pixels = new byte[size * size * 3];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = pixel(x, y);
                int i = (y * size + x) * 3;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R;
            }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgr24, null, pixels, size * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        string path = dir.Combine(name);
        using (var fs = File.Create(path))
            encoder.Save(fs);
        return path;
    }

    static M3Palette From(string path) { M3Palette? p = null; Sta.Run(() => p = M3Palette.FromWallpaper(path, "#000000")); return p!; }

    static double Ratio(Color a, Color b)
    {
        double x = M3Palette.Lum(a) * 100 + 5, y = M3Palette.Lum(b) * 100 + 5;
        return Math.Max(x, y) / Math.Min(x, y);
    }

    [Fact]
    public void MissingOrUnreadableFileGivesNeutralDarkGreyPalette()
    {
        using var dir = new TempDir();
        string broken = dir.Combine("casse.png");
        File.WriteAllText(broken, "pas une image");
        foreach (var path in new[] { "", dir.Combine("absent.png"), broken })
        {
            var p = From(path);
            Assert.True(p.Dark);
            Assert.Equal(Color.FromRgb(0xd0, 0xd0, 0xd0), p.Primary);
            Assert.Equal(p.Surface.R, p.Surface.G);              // surfaces sans teinte
            Assert.Equal(p.Surface.G, p.Surface.B);
        }
        // Le decodeur WPF garde le fichier ouvert jusqu'a la finalisation : on la force avant de supprimer le dossier.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Fact]
    public void GreyscaleImageIsTreatedAsMonochrome()
    {
        using var dir = new TempDir();
        var p = From(Png(dir, "gris.png", (x, y) => Color.FromRgb(120, 120, 120)));
        Assert.Equal(Color.FromRgb(0xd0, 0xd0, 0xd0), p.Primary);
    }

    [Theory]
    [InlineData(220, 30, 30, "R")]
    [InlineData(30, 200, 40, "G")]
    [InlineData(30, 60, 230, "B")]
    public void DominantHueOfTheImageDrivesAccentAndTintedSurfaces(byte r, byte g, byte b, string channel)
    {
        using var dir = new TempDir();
        var p = From(Png(dir, "teinte.png", (x, y) => Color.FromRgb(r, g, b)));
        byte Pick(Color c) => channel switch { "R" => c.R, "G" => c.G, _ => c.B };
        Assert.True(p.Dark);
        Assert.Equal(Pick(p.Primary), new[] { p.Primary.R, p.Primary.G, p.Primary.B }.Max());
        Assert.Equal(Pick(p.Surface), new[] { p.Surface.R, p.Surface.G, p.Surface.B }.Max());
        Assert.True(Ratio(p.Primary, Color.FromRgb(0x13, 0x0d, 0x0e)) >= 4.8);
        Assert.True(M3Palette.Lum(p.Surface) < M3Palette.Lum(p.SurfaceLow));
        Assert.True(M3Palette.Lum(p.SurfaceLow) < M3Palette.Lum(p.SurfaceContainer));
        Assert.True(M3Palette.Lum(p.SurfaceContainer) < M3Palette.Lum(p.SurfaceHigh));
        Assert.True(M3Palette.Lum(p.SurfaceHigh) < M3Palette.Lum(p.SurfaceHighest));
    }

    [Fact]
    public void MostlyDarkOrDesaturatedPixelsAreIgnoredWhenPickingTheHue()
    {
        using var dir = new TempDir();
        // Deux tiers de noir et de gris, un tiers de vert vif : la teinte retenue reste le vert.
        var p = From(Png(dir, "mixte.png", (x, y) =>
            x % 3 == 0 ? Color.FromRgb(20, 220, 60) : x % 3 == 1 ? Color.FromRgb(0, 0, 0) : Color.FromRgb(128, 128, 128)));
        Assert.True(p.Primary.G > p.Primary.R && p.Primary.G > p.Primary.B);
    }
}
