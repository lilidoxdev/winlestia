#if WINLESTIA_SELFTEST
// WINLESTIA_FILES_SELFTEST=palette : mise à jour de palette en direct. Pointe Store.DataDir vers <fixture>\prefs (jamais les vraies
// préférences), écrit un choix clair puis un choix sombre,
// envoie le message "palette" sur le canal propre à cette instance (WINLESTIA_FILES_PIPE fourni par le harnais) et vérifie qu'un pinceau de
// jeton vivant change de couleur dans les deux sens.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using PanneauxGlass;
using Winlestia.Shared;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // WINLESTIA_FILES_SELFTEST=themeshot : une capture de la fenêtre une fois la palette stabilisée (WINLESTIA_FILES_SHOTDIR,
    // WINLESTIA_FILES_SHOTNAME ; source de palette via WINLESTIA_FILES_PALETTE_FILE).
    async Task ThemeShot()
    {
        await WaitLoaded();
        string F = Path.GetFullPath(Active.Location);
        if (!F.StartsWith(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"), StringComparison.OrdinalIgnoreCase))
        {
            T("ABORT fixture outside %TEMP%/WinlestiaFilesOps: " + F);
            return;
        }
        Directory.CreateDirectory(Path.Combine(F, "Documents"));
        Directory.CreateDirectory(Path.Combine(F, "Images"));
        Active.Go(F);
        await WaitLoaded();
        Activate();
        await Task.Delay(PauseScanMs);          // palette resolves off-thread (wallpaper scan)
        list.SelectedIndex = 1;
        list.Focus();
        await Task.Delay(PauseStabilisationMs);
        string dir = Environment.GetEnvironmentVariable("WINLESTIA_FILES_SHOTDIR") ?? Path.GetTempPath();
        Shot(Path.Combine(dir, Environment.GetEnvironmentVariable("WINLESTIA_FILES_SHOTNAME") ?? "files-theme.png"));
        T($"themeshot dark={Theme.Current.IsDark} primary={Theme.Current.Role(M3Role.Primary)} " +
            $"surface={Theme.Current.Role(M3Role.Surface)}");
        Close();
    }

    async Task PaletteTest()
    {
        string F = Path.GetFullPath(Active.Location);
        if (!F.StartsWith(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"), StringComparison.OrdinalIgnoreCase))
        {
            T("ABORT fixture outside %TEMP%/WinlestiaFilesOps: " + F);
            return;
        }
        await WaitLoaded();
        string prefs = Path.Combine(F, "prefs");
        Directory.CreateDirectory(prefs);
        Store.DataDir = prefs;
        var brush = UI.T(M3Role.Surface);
        Color dark = brush.Color;
        Check("starts dark", Theme.Current.IsDark);
        async Task<bool> Send()
        {
            for (int i = 0; i < 40; i++)
            {      // the server starts at ApplicationIdle
                if (await Task.Run(() => FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "palette"), 300)))
                    return true;
                await Task.Delay(PauseAnimationMs);
            }
            return false;
        }
        async Task<bool> Until(Func<bool> cond)
        {
            for (int i = 0; i < 60 && !cond(); i++)
                await Task.Delay(PauseTickMs);
            return cond();
        }

        File.WriteAllText(Path.Combine(prefs, "preferences-glass.json"), "{\"Theme\":\"shell\"}");
        Check("palette message acknowledged (light)", await Send());
        bool light = await Until(() => brush.Color != dark);
        Check("surface brush repainted to light", light && !Theme.Current.IsDark, $"{dark} -> {brush.Color}");

        File.WriteAllText(Path.Combine(prefs, "preferences-glass.json"), "{\"Theme\":\"nuit\"}");
        Check("palette message acknowledged (dark)", await Send());
        bool back = await Until(() => brush.Color == dark);
        Check("surface brush repainted back to dark", back && Theme.Current.IsDark, brush.Color.ToString());
        T($"DONE fails={testFails}");
        Close();
    }
}
#endif
