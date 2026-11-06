// WINLESTIA_FILES_SELFTEST=themes : for every Winlestia theme (Porcelaine = "shell", Nuit = "nuit", Personnalise = "custom", Automatique = AutoTheme + palette-shell.json)
// applies the palette through the real PaletteLoader.Refresh path, saves <theme>.png, and measures WCAG contrast of the role pairs the UI really uses.
// Palette files are written under WINLESTIA_FILES_SHOTDIR\palettes (never the real profile). Then scale-100/125/150/200.png on the last theme.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    static Color Over(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        return Color.FromRgb((byte)Math.Round(top.R * a + bottom.R * (1 - a)), (byte)Math.Round(top.G * a + bottom.G * (1 - a)), (byte)Math.Round(top.B * a + bottom.B * (1 - a)));
    }

    static double Ratio(Color a, Color b)
    {
        double l1 = M3Palette.Lum(a), l2 = M3Palette.Lum(b);
        return (Math.Max(l1, l2) + .05) / (Math.Min(l1, l2) + .05);
    }

    async Task ThemesTest()
    {
        string dir = Environment.GetEnvironmentVariable("WINLESTIA_FILES_SHOTDIR") ?? "";
        if (dir.Length == 0)
        {
            T("ABORT WINLESTIA_FILES_SHOTDIR missing");
            Close();
            return;
        }
        string tmp = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(FilesSettings.Dir).StartsWith(tmp, StringComparison.OrdinalIgnoreCase))
        {
            T("ABORT data dir is not a %TEMP% folder");
            Close();
            return;
        }
        string F = Fixture();
        await WaitLoaded();
        await sidebar.Ready;
        Activate();
        string pal = Path.Combine(dir, "palettes");
        Directory.CreateDirectory(pal);

        // fixture: folders + files of different sizes, a broken quick-access pin, the fixture itself pinned (selected sidebar entry)
        Directory.CreateDirectory(Path.Combine(F, "Documents"));
        Directory.CreateDirectory(Path.Combine(F, "Images"));
        File.WriteAllBytes(Path.Combine(F, "Rapport annuel.docx"), new byte[183_000]);
        File.WriteAllBytes(Path.Combine(F, "Budget.xlsx"), new byte[2_400_000]);
        QuickAccess.Pin(F);
        QuickAccess.Pin(Path.Combine(F, "Dossier disparu"));
        Active.Go(F);
        await WaitLoaded();
        await Task.Delay(2500);       // the pin probe marks the missing one
        list.Select(Active.Items.Take(2).Concat(Active.Items.Where(i => !i.IsDir).Take(2)).Distinct().ToList());
        list.Focus();
        await Task.Delay(300);

        const string AutoDark = "{\"mode\":\"auto\",\"palette\":{\"m3surface\":\"#141318\",\"m3surfaceContainerLow\":\"#1c1b20\",\"m3surfaceContainer\":\"#201f25\",\"m3surfaceContainerHigh\":\"#2b292f\",\"m3surfaceContainerHighest\":\"#36343a\",\"m3onSurface\":\"#e6e1e9\",\"m3onSurfaceVariant\":\"#c9c5d0\",\"m3primary\":\"#c9bfff\",\"m3onPrimary\":\"#2f2475\",\"m3primaryContainer\":\"#463c8c\",\"m3onPrimaryContainer\":\"#e5deff\",\"m3secondary\":\"#c8c3dc\",\"m3secondaryContainer\":\"#474459\",\"m3onSecondaryContainer\":\"#e4dff9\",\"m3outline\":\"#938f99\",\"m3outlineVariant\":\"#49454f\",\"m3error\":\"#ffb4ab\",\"m3onError\":\"#690005\"}}";
        const string AutoLight = "{\"mode\":\"auto\",\"palette\":{\"m3surface\":\"#fdf8ff\",\"m3surfaceContainerLow\":\"#f7f2fa\",\"m3surfaceContainer\":\"#f1ecf4\",\"m3surfaceContainerHigh\":\"#ebe6ee\",\"m3surfaceContainerHighest\":\"#e6e1e9\",\"m3onSurface\":\"#1c1b1f\",\"m3onSurfaceVariant\":\"#49454f\",\"m3primary\":\"#5b4db0\",\"m3onPrimary\":\"#ffffff\",\"m3primaryContainer\":\"#e5deff\",\"m3onPrimaryContainer\":\"#1b0d62\",\"m3secondary\":\"#605c71\",\"m3secondaryContainer\":\"#e6dff9\",\"m3onSecondaryContainer\":\"#1d192c\",\"m3outline\":\"#7a757f\",\"m3outlineVariant\":\"#cac4d0\",\"m3error\":\"#ba1a1a\",\"m3onError\":\"#ffffff\"}}";
        var themes = new (string Name, string Prefs, string? Shell, bool Dark)[] {
            ("porcelaine", "{\"Theme\":\"shell\"}", null, false),
            ("nuit", "{\"Theme\":\"nuit\"}", null, true),
            ("personnalise-clair", "{\"Theme\":\"custom\",\"CustomPalette\":{\"background\":\"#f4f1fa\",\"text\":\"#26233a\",\"primary\":\"#6a5acd\",\"secondary\":\"#7d6f9f\"}}", null, false),
            ("personnalise-sombre", "{\"Theme\":\"custom\",\"CustomPalette\":{\"background\":\"#1c1b2b\",\"text\":\"#e8e6f5\",\"primary\":\"#a99bf0\",\"secondary\":\"#8d86b8\"}}", null, true),
            ("automatique-clair", "{\"AutoTheme\":true}", AutoLight, false),
            ("automatique-sombre", "{\"AutoTheme\":true}", AutoDark, true) };

        var report = new StringBuilder("theme | pair | ratio | minimum | result\n");
        foreach (var t in themes)
        {
            string prefsFile = Path.Combine(pal, t.Name + ".json"), shellFile = Path.Combine(pal, t.Name + "-palette-shell.json");
            File.WriteAllText(prefsFile, t.Prefs);
            if (t.Shell != null)
                File.WriteAllText(shellFile, t.Shell);
            Environment.SetEnvironmentVariable("WINLESTIA_FILES_PALETTE_FILE", prefsFile);
            Environment.SetEnvironmentVariable("WINLESTIA_FILES_PALETTE_SHELL", t.Shell != null ? shellFile : Path.Combine(pal, "absent.json"));
            var applied = new TaskCompletionSource();
            PaletteLoader.Refresh(Dispatcher, () => { ApplyDwm(); applied.TrySetResult(); });
            await Task.WhenAny(applied.Task, Task.Delay(8000));
            await Task.Delay(600);
            Check($"[{t.Name}] palette applied", applied.Task.IsCompleted && Theme.Current.IsDark == t.Dark, $"dark={Theme.Current.IsDark}");

            Color R(M3Role r) => Theme.Current.Role(r);
            Color win = R(M3Role.Surface), panel = Over(UI.Panel.Color, win), card = R(M3Role.SurfaceContainer);
            Color hover = Over(UI.Tint.Color, panel), selected = R(M3Role.PrimaryContainer);
            var pairs = new (string Name, Color Fg, Color Bg, double Min)[] {
                ("texte principal / liste (OnSurface / panneau)", R(M3Role.OnSurface), panel, 4.5),
                ("texte principal / fenetre (OnSurface / Surface)", R(M3Role.OnSurface), win, 4.5),
                ("texte adresse+recherche (OnSurface / SurfaceContainer)", R(M3Role.OnSurface), card, 4.5),
                ("texte secondaire / liste (OnSurfaceVariant / panneau)", R(M3Role.OnSurfaceVariant), panel, 3),
                ("placeholder recherche (OnSurfaceVariant / SurfaceContainer)", R(M3Role.OnSurfaceVariant), card, 3),
                ("selection texte (OnPrimaryContainer / PrimaryContainer)", R(M3Role.OnPrimaryContainer), selected, 4.5),
                ("selection texte secondaire (OnSurfaceVariant / PrimaryContainer)", R(M3Role.OnSurfaceVariant), selected, 3),
                ("selection icone dossier (Primary / PrimaryContainer)", R(M3Role.Primary), selected, 3),
                ("survol texte (OnSurface / survol sur panneau)", R(M3Role.OnSurface), hover, 4.5),
                ("anneau de focus (Primary / panneau)", R(M3Role.Primary), panel, 3),
                ("anneau de focus sur selection (Primary / PrimaryContainer)", R(M3Role.Primary), selected, 3),
                ("introuvable libelle (OnSurfaceVariant / panneau)", R(M3Role.OnSurfaceVariant), panel, 3),
                ("introuvable icone (Error / panneau)", R(M3Role.Error), panel, 3),
                ("desactive (Outline / panneau)", R(M3Role.Outline), panel, 3),
                ("accent sur panneau, bouton Nouveau (Primary / panneau)", R(M3Role.Primary), panel, 3) };
            foreach (var p in pairs)
            {
                double ratio = Ratio(p.Fg, p.Bg);
                Check($"[{t.Name}] {p.Name} = {ratio:F2} (>= {p.Min})", ratio >= p.Min);
                report.AppendLine($"{t.Name} | {p.Name} | {ratio:F2} | {p.Min} | {(ratio >= p.Min ? "OK" : "ECHEC")}");
            }
            if (list.SelectedItems.Count > 0 && list.ItemContainerGenerator.ContainerFromItem(list.SelectedItems[0]) is System.Windows.Controls.ListBoxItem row)
                row.Focus();   // keyboard focus ring on a row
            await Task.Delay(250);
            ShotScaled(Path.Combine(dir, t.Name + ".png"), 1);
        }
        File.WriteAllText(Path.Combine(dir, "contrast.txt"), report.ToString());

        // Scales: a 1920x1040 screen at 100/125/150/200 % is 1920/s x 1040/s DIP; the tree is rendered at s x (96*s dpi worth of pixels).
        foreach (int pct in new[] { 100, 125, 150, 200 })
        {
            double s = pct / 100.0;
            WindowState = WindowState.Normal;
            Left = 0;
            Top = 0;
            Width = Math.Max(MinWidth, 1920 / s);
            Height = Math.Max(MinHeight, 1040 / s);
            ((FrameworkElement)Content).UpdateLayout();
            await Task.Delay(700);
            ShotScaled(Path.Combine(dir, $"scale-{pct}.png"), s);
        }
        T($"DONE fails={testFails}");
        Close();
    }

    void ShotScaled(string path, double s)
    {
        var root = (FrameworkElement)Content;
        double w = root.ActualWidth, h = root.ActualHeight;
        var m = root.Margin;
        double fw = w + m.Left + m.Right, fh = h + m.Top + m.Bottom;      // the window background around the root grid is part of the picture
        int pw = Math.Max(1, (int)Math.Round(fw * s)), ph = Math.Max(1, (int)Math.Round(fh * s));
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(s, s));
            var c = UI.T(M3Role.Surface).Color;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), null, new Rect(0, 0, fw, fh));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(m.Left, m.Top, w, h));
        }
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
        T($"shot {path} ({pw}x{ph}, logical {w:F0}x{h:F0})");
    }
}
