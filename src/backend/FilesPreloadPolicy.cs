// Pure restart decisions for the resident Winlestia Fichiers (no Store, no Process): linked into tests/FilesPreloadPolicy.
using System;

namespace PanneauxGlass;

internal enum PreloadAction { None, Recycle, StopTooFast, Crash }

internal sealed class FilesPreloadPolicy {
    internal const int RecycleExitCode = 75;          // the resident left on purpose after too many native menus (see src/files/App.xaml.cs)
    internal const int MinRecycleIntervalMs = 60000;  // two recycle exits closer than this mean something is looping
    internal const int CrashLimit = 2;                // launches per backend session, the first one included (a recycle is not a crash and is not counted)

    long lastRecycle = long.MinValue;
    internal int Starts;                              // counts launches that are not recycles

    // Called when the resident process exited. nowMs is a monotonic clock.
    internal PreloadAction OnExit(int exitCode, long nowMs, bool quitting, bool prefOn) {
        if (quitting || !prefOn) return PreloadAction.None;
        if (exitCode == RecycleExitCode) {
            bool tooFast = lastRecycle != long.MinValue && nowMs - lastRecycle < MinRecycleIntervalMs;
            lastRecycle = nowMs;
            return tooFast ? PreloadAction.StopTooFast : PreloadAction.Recycle;
        }
        return Starts < CrashLimit ? PreloadAction.Crash : PreloadAction.None;
    }

    // Launch gate: crash restarts and the first launch are limited, recycle launches are not.
    internal bool MayLaunch(bool recycle, bool prefOn, bool quitting) => prefOn && !quitting && (recycle || Starts < CrashLimit);
}
