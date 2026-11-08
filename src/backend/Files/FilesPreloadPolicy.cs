// Décisions pures de relance du Winlestia Fichiers résident (ni Store ni Process) : liées dans tests/FilesPreloadPolicy.
using System;

namespace PanneauxGlass;

internal enum PreloadAction
{
    None, Recycle, StopTooFast, Crash
}

internal sealed class FilesPreloadPolicy
{
    // the resident left on purpose after too many native menus (see src/files/App.xaml.cs)
    internal const int RecycleExitCode = 75;
    internal const int MinRecycleIntervalMs = 60000;  // two recycle exits closer than this mean something is looping
    // launches per backend session, the first one included (a recycle is not a crash and is not counted)
    internal const int CrashLimit = 2;

    long lastRecycle = long.MinValue;
    internal int Starts;                              // counts launches that are not recycles

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
