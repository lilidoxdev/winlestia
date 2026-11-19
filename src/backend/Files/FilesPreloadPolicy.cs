// Décisions pures de relance du Winlestia Fichiers résident (ni Store ni Process) : liées dans tests/FilesPreloadPolicy.
using System;

namespace PanneauxGlass;

internal enum PreloadAction
{
    None, Recycle, StopTooFast, Crash
}

internal sealed class FilesPreloadPolicy
{
    // le résident laissé volontairement après trop de menus natifs (voir src/files/App.xaml.cs)
    internal const int RecycleExitCode = 75;
    internal const int MinRecycleIntervalMs = 60000;  // deux sorties de recyclage plus proches que cela signifient qu'une boucle tourne
    // lancements par session du backend, le premier compris (un recyclage n'est pas un plantage et n'est pas compté)
    internal const int CrashLimit = 2;

    long lastRecycle = long.MinValue;
    internal int Starts;                              // compte les lancements qui ne sont pas des recyclages

    // Appelé quand le processus résident s'est terminé. nowMs est une horloge monotone.
    internal PreloadAction OnExit(int exitCode, long nowMs, bool quitting, bool prefOn)
    {
        if (quitting || !prefOn)
            return PreloadAction.None;
        if (exitCode == RecycleExitCode)
        {
            bool tooFast = lastRecycle != long.MinValue && nowMs - lastRecycle < MinRecycleIntervalMs;
            lastRecycle = nowMs;
            return tooFast ? PreloadAction.StopTooFast : PreloadAction.Recycle;
        }
        return Starts < CrashLimit ? PreloadAction.Crash : PreloadAction.None;
    }

    // Barrière de lancement : les relances après plantage et le premier lancement sont limités, pas les lancements de recyclage.
    internal bool MayLaunch(bool recycle, bool prefOn, bool quitting) => prefOn && !quitting && (recycle || Starts < CrashLimit);
}
