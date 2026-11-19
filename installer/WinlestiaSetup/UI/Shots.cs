using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinlestiaSetup;

// --screenshots <dir> : rend chaque écran en PNG avec RenderTargetBitmap. Rien n'est installé ni écrit hors de <dir>.
// DWM ne peut pas être capturé ainsi : le fond acrylique est simulé avec le fond d'écran flouté.
static class Shots
{
    const double W = 1040, H = 680, Scale = 1.5;

    static async Task Flush(MainWindow w)
    {
        for (int i = 0; i < 2; i++) await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    static async Task Snap(MainWindow w, string path)
    {
        await Flush(w);
        await Task.Delay(320); // laisse finir les animations de bascule de 180 ms avant de capturer
        await Flush(w);
        w.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)(W * Scale), (int)(H * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(w.RenderRoot), null, new Rect(0, 0, W, H));
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path); enc.Save(fs);
    }

    public static async Task Run(MainWindow w, string dir, string? wallpaper, Palette pal)
    {
        Directory.CreateDirectory(dir);
        await Flush(w);
        w.RenderRoot.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 12, 12);
        if (wallpaper != null)
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(wallpaper); bi.DecodePixelWidth = 1000;
            bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            w.Backdrop.Children.Add(new Image { Source = bi, Stretch = Stretch.UniformToFill, Effect = new BlurEffect {
                Radius = 60 }, Margin = new Thickness(-60) });
        }
        w.Backdrop.Children.Add(new Border { Background =
            new SolidColorBrush(Color.FromArgb(0x99, pal.Backdrop.R, pal.Backdrop.G, pal.Backdrop.B)) });
        w.Backdrop.Visibility = Visibility.Visible;

        var fake = new InstallInfo { Exists = true, Version = "0.9.1", Dir = Ctx.DefaultDir };
        string P(string lang, string n) => Path.Combine(dir, lang + "-" + n + ".png");

        void Reset() { Ctx.Info = new InstallInfo(); Ctx.Mode = Mode.Install; Features.Reset(); Pages.SelectedFeature = "taskbar";
            Runner.Warnings.Clear(); Runner.Failure = null; w.SetMode(Mode.Install); }

        foreach (var lang in new[] { "fr", "en", "es", "de" })
        {
            Loc.Set(lang);
            bool full = lang is "fr" or "en";
            Reset(); w.GoTo("welcome"); await Snap(w, P(lang, "01-welcome"));
            Ctx.Info = fake; Features.Reset(); w.SetMode(Mode.Update); w.GoTo("welcome"); await Snap(w, P(lang, "02-welcome-update"));
            Reset();
            if (full) { w.GoTo("license"); await Snap(w, P(lang, "03-license")); }
            Pages.SelectedFeature = "taskbar"; w.GoTo("features"); await Snap(w, P(lang, "04-features-a-taskbar-on"));
            Ctx.Features["taskbar"] = false; w.GoTo("features"); await Snap(w, P(lang, "04-features-b-taskbar-off"));
            if (full)
            {
                Ctx.Features["terminal"] = true; Pages.SelectedFeature = "winjets"; w.GoTo("features");
                await Snap(w, P(lang, "04-features-c-winjets-locked"));
                Pages.SelectedFeature = "terminal"; w.GoTo("features"); await Snap(w, P(lang, "04-features-d-terminal"));
            }
            Reset(); w.GoTo("updates"); await Snap(w, P(lang, "05-updates"));
            if (full)
            {
                Ctx.Features["taskbar"] = false; Ctx.Features["terminal"] = true;
                w.GoTo("summary"); await Snap(w, P(lang, "06-summary"));
                Reset(); w.GoTo("progress"); await Snap(w, P(lang, "07-progress"));
                Reset(); w.GoTo("done"); await Snap(w, P(lang, "08-done"));
            }
            Ctx.Info = fake; Ctx.O.RemoveData = false; w.SetMode(Mode.Uninstall); w.GoTo("uconfirm");
            await Snap(w, P(lang, "09-uninstall"));
            if (full) { w.GoTo("done"); await Snap(w, P(lang, "10-uninstall-done")); }
        }

        // Mouvement : 18 images de la transition licence -> fonctionnalités (300 ms, ease-out), plus six images choisies.
        Loc.Set("en"); Reset(); w.GoTo("license");
        var frames = Path.Combine(dir, "transition-frames"); Directory.CreateDirectory(frames);
        w.GoTo("features", false, true);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        const int N = 18;
        for (int i = 0; i < N; i++)
        {
            w.SetFrame(ease.Ease(i / (double)(N - 1)));
            await Snap(w, Path.Combine(frames, $"frame-{i:00}.png"));
        }
        w.FinishTransition();
        int[] pick = { 0, 4, 7, 10, 13, 17 };
        for (int k = 0; k < pick.Length; k++) File.Copy(Path.Combine(frames, $"frame-{pick[k]:00}.png"),
            Path.Combine(dir, $"transition-{k + 1}.png"), true);

        try
        {
            // La vraie transition dure 300 ms ; la vidéo la joue six fois plus lentement pour qu'on puisse la lire.
            var psi = new ProcessStartInfo("ffmpeg",
                $"-y -loglevel error -framerate 10 -i \"{Path.Combine(frames, "frame-%02d.png")}\" "
                + "-vf \"tpad=stop_mode=clone:stop_duration=0.8,scale=1040:-2,format=yuv420p\" -c:v libx264 "
                + $"\"{Path.Combine(dir, "transition-slow.mp4")}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(60000);
            psi = new ProcessStartInfo("ffmpeg",
                $"-y -loglevel error -framerate 10 -i \"{Path.Combine(frames, "frame-%02d.png")}\" "
                + "-vf \"scale=780:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" "
                + $"\"{Path.Combine(dir, "transition-slow.gif")}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(60000);
        }
        catch { /* ffmpeg not installed: the frames remain */ }
    }
}
