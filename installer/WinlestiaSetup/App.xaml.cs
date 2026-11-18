using System.Windows;
using System.Windows.Media;

namespace WinlestiaSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var o = Options.Parse(e.Args);
        Ctx.O = o; Ctx.Screenshot = o.Screenshots != null; Ctx.LoadVersion();
        Loc.Init(o.Lang);
        if (!Ctx.Screenshot) Log.File = Path.Combine(Path.GetTempPath(), "WinlestiaSetup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
            + ".log");
        Log.Write("WinlestiaSetup " + Ctx.Version + " args: " + string.Join(" ", e.Args));

        Ctx.Info = Ctx.Screenshot ? new InstallInfo() : InstallInfo.Detect();
        Ctx.Demo = !Ctx.Screenshot && !o.Uninstall && !Installer.HasPayload();
        if (o.Uninstall) Ctx.Mode = Mode.Uninstall;
        else if (Ctx.Info.Exists) Ctx.Mode = Mode.Update;

        // Le setup ne doit jamais s'exécuter depuis le dossier qu'il remplace ou supprime (le balayage du shell l'arrêterait).
        if (!Ctx.Screenshot && Ctx.Info.Exists)
        {
            try { if (Uninstaller.RelocateIfNeeded(e.Args)) { Shutdown(); return; } } catch (Exception ex) {
                Log.Write("Relocation failed: " + ex.Message); }
        }

        string? wall = o.Wallpaper ?? (Ctx.Screenshot ? RepoWallpaper() : null) ?? Native.WallpaperPath();
        var pal = Palette.FromWallpaper(wall);
        pal.Apply(Resources);
        Features.Reset();

        var win = new MainWindow();
        if (Ctx.Screenshot)
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual; win.Left = -20000; win.Top = 0; win.ShowInTaskbar = false;
            win.Show();
            _ = Shots.Run(win, o.Screenshots!, wall, pal).ContinueWith(t =>
            {
                if (t.IsFaulted) File.WriteAllText(Path.Combine(o.Screenshots!, "error.txt"), t.Exception!.ToString());
                Dispatcher.Invoke(Shutdown);
            });
            return;
        }
        win.Show();
        win.Closed += (_, _) =>
        {
            if (Ctx.Mode == Mode.Uninstall) Uninstaller.CleanTempCopy();
            if (Ctx.LaunchAfter && win.PageId == "done" && Runner.Failure == null && Ctx.Mode
                != Mode.Uninstall) Installer.Launch(Ctx.InstallDir);
        };
    }

    static string? RepoWallpaper()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var w = Path.Combine(d.FullName, "assets", "wallpapers");
            if (Directory.Exists(w)) return Directory.GetFiles(w).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).FirstOrDefault();
        }
        return null;
    }
}
