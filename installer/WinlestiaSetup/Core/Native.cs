using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinlestiaSetup;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref Margins m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint a, uint b, StringBuilder s, uint f);

    // Cadre sombre, coins arrondis et fond acrylique viennent de DWM, pas d'un dessin.
    public static void ApplyGlass(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        var src = HwndSource.FromHwnd(h);
        if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        int v = 1; DwmSetWindowAttribute(h, 20, ref v, 4);          // DWMWA_USE_IMMERSIVE_DARK_MODE
        v = 2; DwmSetWindowAttribute(h, 33, ref v, 4);              // DWMWA_WINDOW_CORNER_PREFERENCE = arrondi
        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(h, ref m);
        v = 3;                                                      // DWMWA_SYSTEMBACKDROP_TYPE = transitoire (acrylique)
        if (DwmSetWindowAttribute(h, 38, ref v, 4) != 0) { v = 2; DwmSetWindowAttribute(h, 38, ref v, 4); } // Repli sur Mica
    }

    public static string? WallpaperPath()
    {
        var sb = new StringBuilder(520);
        if (SystemParametersInfo(0x73, 520, sb, 0) && File.Exists(sb.ToString())) return sb.ToString();
        var t = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return File.Exists(t) ? t : null;
    }
}
