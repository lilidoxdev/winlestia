using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinlestiaSetup;

// Un seul accent, chaque surface teintée de la même teinte. La teinte vient du fond d'écran.
sealed class Palette
{
    public double Hue; public double Sat;

    static Color Hsl(double h, double s, double l, byte a = 255)
    {
        h = ((h % 360) + 360) % 360; double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c)
            : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Color.FromArgb(a, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static Palette FromWallpaper(string? path)
    {
        var fallback = new Palette { Hue = 12, Sat = 0.5 }; // teinte du primaire Winlestia par défaut
        if (path == null || !File.Exists(path)) return fallback;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(path); bi.DecodePixelWidth = 48; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            var fc = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            int w = fc.PixelWidth, h = fc.PixelHeight; var px = new byte[w * h * 4]; fc.CopyPixels(px, w * 4, 0);
            double sx = 0, sy = 0, sw = 0, ss = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0, mx = Math.Max(r, Math.Max(g, b)),
                    mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
                if (d < 0.02) continue;
                double hue = mx == r ? 60 * (((g - b) / d + 6) % 6) : mx == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
                double s = d / mx, wt = s * mx;
                sx += Math.Cos(hue * Math.PI / 180) * wt; sy += Math.Sin(hue * Math.PI / 180) * wt; sw += wt; ss += s * wt;
            }
            if (sw < 1e-3) return fallback;
            return new Palette { Hue = (Math.Atan2(sy, sx) * 180 / Math.PI + 360) % 360, Sat = Math.Clamp(ss / sw, 0.4, 0.75) };
        }
        catch { return fallback; }
    }

    public Color Backdrop => Hsl(Hue, Sat * 0.5, 0.16);

    public void Apply(ResourceDictionary r)
    {
        void S(string k, Color c) { var b = new SolidColorBrush(c); b.Freeze(); r[k] = b; }
        S("AccentBrush", Hsl(Hue, Math.Min(0.8, Sat + 0.15), 0.74));
        S("OnAccentBrush", Hsl(Hue, Sat, 0.11));
        S("SurfaceBrush", Hsl(Hue, Sat * 0.45, 0.08, 0xC8));
        S("SidebarBrush", Hsl(Hue, Sat * 0.5, 0.06, 0x8C));
        S("CardBrush", Hsl(Hue, Sat * 0.35, 0.22, 0x66));
        S("CardHoverBrush", Hsl(Hue, Sat * 0.4, 0.28, 0x80));
        S("TextBrush", Hsl(Hue, 0.25, 0.94));
        S("TextDimBrush", Hsl(Hue, 0.14, 0.70));
        S("OutlineBrush", Hsl(Hue, 0.22, 0.45, 0x80));
        S("AccentSoftBrush", Hsl(Hue, Math.Min(0.8, Sat + 0.15), 0.74, 0x2A));
    }
}
