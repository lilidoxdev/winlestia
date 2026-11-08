// Normalisation des logos de sites (fond uni retire, silhouette sombre eclaircie), analyse d'adresses, formats de taille et
// filtres de mesures materielles (temperature plausible, debit, pourcentage).
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanneauxGlass;
namespace Winlestia.Tests;
public class WebIconsTests
{
    [Theory]
    [InlineData("gmail.com", "https://gmail.com/")]
    [InlineData("  http://exemple.fr/a?b=1 ", "http://exemple.fr/a?b=1")]
    [InlineData("https://github.com", "https://github.com/")]
    public void ParseAddsHttpsWhenSchemeIsMissing(string input, string expected) =>
        Assert.Equal(expected, WebIcons.Parse(input).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("ftp://exemple.fr")]
    [InlineData("https://user:pass@exemple.fr")]
    [InlineData("https://")]
    public void ParseRejectsNonWebOrCredentialedAddresses(string input) =>
        Assert.Throws<FormatException>(() => WebIcons.Parse(input));

    static byte[] PngBytes(int size, Func<int, int, (byte b, byte g, byte r, byte a)> pixel)
    {
        var data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var (b, g, r, a) = pixel(x, y);
                int i = (y * size + x) * 4;
                data[i] = b; data[i + 1] = g; data[i + 2] = r; data[i + 3] = a;
            }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, data, size * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    static (int w, int h, byte[] px) Decode(string path)
    {
        using var fs = File.OpenRead(path);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return (bgra.PixelWidth, bgra.PixelHeight, px);
    }

    [Fact]
    public void NormalizeRemovesEdgeConnectedMatteButKeepsEnclosedDetails()
    {
        using var dir = new TempDir();
        string path = dir.Combine("logo.png");
        // Fond blanc plein, disque rouge au centre contenant un point blanc enferme.
        var data = PngBytes(40, (x, y) =>
        {
            double d = Math.Sqrt((x - 20) * (x - 20) + (y - 20) * (y - 20));
            if (d < 3) return ((byte)255, (byte)255, (byte)255, (byte)255);
            return d < 12 ? ((byte)0, (byte)0, (byte)220, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255);
        });
        Sta.Run(() => WebIcons.Normalize(data, path));
        var (w, _, px) = Decode(path);
        Assert.Equal(40, w);
        Assert.Equal(0, px[3]);                                   // coin : fond retire
        int center = (20 * w + 20) * 4;
        Assert.Equal(255, px[center + 3]);                        // point blanc enferme conserve
        int ring = (20 * w + 28) * 4;
        Assert.Equal(220, px[ring + 2]);                          // disque colore non modifie
    }

    [Fact]
    public void NormalizeLightensDarkMonochromeSilhouetteKeepingAlpha()
    {
        using var dir = new TempDir();
        string path = dir.Combine("sombre.png");
        var data = PngBytes(16, (x, y) => x < 8 ? ((byte)10, (byte)10, (byte)10, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        Sta.Run(() => WebIcons.Normalize(data, path));
        var (w, _, px) = Decode(path);
        Assert.Equal(new byte[] { 255, 247, 242, 255 }, px[..4]);       // BGRA : variante claire, alpha conserve
        Assert.Equal(0, px[(8 * 4) + 3]);
        Assert.Equal(16, w);
    }

    [Fact]
    public void NormalizeDownscalesLargeImagesTo256AndRejectsFullyTransparentOnes()
    {
        using var dir = new TempDir();
        string path = dir.Combine("grand.png");
        var big = PngBytes(512, (x, y) => ((byte)(x % 200), (byte)(y % 200), (byte)120, (byte)255));
        Sta.Run(() => WebIcons.Normalize(big, path));
        var (w, h, _) = Decode(path);
        Assert.Equal((256, 256), (w, h));
        var empty = PngBytes(8, (x, y) => ((byte)0, (byte)0, (byte)0, (byte)0));
        Assert.Throws<InvalidDataException>(() => Sta.Run(() => WebIcons.Normalize(empty, dir.Combine("vide.png"))));
    }

    [Fact]
    public void DefaultIconIsA128PixelPngWithTransparentCorners()
    {
        using var dir = new TempDir();
        string path = dir.Combine("defaut.png");
        WebIcons.Default(path);
        var (w, h, px) = Decode(path);
        Assert.Equal((128, 128), (w, h));
        Assert.Equal(0, px[3]);
        Assert.Contains(Enumerable(px), a => a > 200);
    }

    static System.Collections.Generic.IEnumerable<byte> Enumerable(byte[] px)
    {
        for (int i = 3; i < px.Length; i += 4)
            yield return px[i];
    }
}

public class MetricsFiltersTests
{
    [Theory]
    [InlineData(0L, "0 Ko")]
    [InlineData(1536L, "2 Ko")]
    [InlineData(5L * 1024 * 1024, "5 Mo")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1,5 Go")]
    public void SizeUsesLargestWholeUnit(long bytes, string expected) =>
        Assert.Equal(expected.Replace(',', System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]),
                     SystemTools.Size(bytes));

    [Theory]
    [InlineData(null, null)]
    [InlineData(-5.0, 0.0)]
    [InlineData(42.5, 42.5)]
    [InlineData(250.0, 100.0)]
    [InlineData(double.NaN, null)]
    public void PercentClampsAndRejectsNonFinite(double? input, double? expected) =>
        Assert.Equal(expected, PcTelemetry_Percent(input));

    static double? PcTelemetry_Percent(double? v) => PcTelemetrySource.Percent(v);

    [Theory]
    [InlineData(1000L, 0L, 2.0, 500.0)]
    [InlineData(1000L, 0L, 0.0, 0.0)]
    [InlineData(100L, 500L, 1.0, 0.0)]       // compteur remis a zero : pas de debit negatif
    public void RateIsBytesPerSecondAndNeverNegative(long current, long previous, double seconds, double expected) =>
        Assert.Equal(expected, PcTelemetrySource.Rate(current, previous, seconds));

    [Theory]
    [InlineData(null, null)]
    [InlineData(10.0, null)]
    [InlineData(10.5, 10.5)]
    [InlineData(125.0, 125.0)]
    [InlineData(125.1, null)]
    public void PlausibleTemperatureIsBetween10And125(double? input, double? expected) =>
        Assert.Equal(expected, CpuTempChain.Plausible(input));

    [Theory]
    [InlineData(26.85)]
    [InlineData(27.85)]
    [InlineData(5.0)]
    public void RealRejectsAcpiStubConstantsAndImplausibleValues(double value) => Assert.Null(CpuTempChain.Real(value));

    [Fact]
    public void RealKeepsGenuineReadings() => Assert.Equal(54.3, CpuTempChain.Real(54.3));
}
