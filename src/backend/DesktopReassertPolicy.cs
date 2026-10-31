// Pure timing and class rules for re-applying the hidden desktop icons (no Store, no WinEvent): linked into tests/DesktopReassert.
using System;

namespace PanneauxGlass;

static class DesktopEventFilter {
    // WorkerW (hôte du fond) ou vue d'icônes du Bureau ; un SHELLDLL_DefView d'une fenêtre de dossier est ignoré.
    internal static bool Relevant(string className,string parentClassName)=>
        className=="WorkerW"||className=="SHELLDLL_DefView"&&parentClassName is "Progman" or "WorkerW";
}

// Anti-rebond 400 ms, une exécution toutes les 2 s au plus, une réaffirmation de suivi à +3 s,
// et silence de 1,5 s après chaque exécution : SECOURS_BUREAU change lui-même la vue, ses événements ne relancent rien.
sealed class DesktopReassertPolicy {
    internal static readonly TimeSpan Debounce=TimeSpan.FromMilliseconds(400),MinGap=TimeSpan.FromSeconds(2),Mute=TimeSpan.FromMilliseconds(1500),Follow=TimeSpan.FromSeconds(3),MaxWait=TimeSpan.FromSeconds(2);
    DateTime lastRun=DateTime.MinValue,due=DateTime.MaxValue,pendingSince=DateTime.MaxValue;bool follow;

    // Retourne false si l'événement est ignoré (silence après une exécution). force : redémarrage d'Explorer.
    internal bool Event(DateTime now,bool force=false){
        if(!force&&lastRun!=DateTime.MinValue&&now-lastRun<Mute)return false;
        if(follow){follow=false;due=DateTime.MaxValue;}
        if(due==DateTime.MaxValue)pendingSince=now;
        DateTime wanted=now+Debounce,cap=pendingSince+MaxWait;if(wanted>cap)wanted=cap;
        DateTime floor=lastRun==DateTime.MinValue?DateTime.MinValue:lastRun+MinGap;
        due=wanted>floor?wanted:floor;return true;
    }
    // HideIcons (HKCU ExplorerAdvanced) à 0 : l'utilisateur ou SECOURS_BUREAU a volontairement rétabli les icônes, on ne les recache pas.
    internal static bool StillWanted(int hideIcons)=>hideIcons!=0;
    internal bool Due(DateTime now)=>due<=now;
    internal TimeSpan? Delay(DateTime now)=>due==DateTime.MaxValue?null:due>now?due-now:TimeSpan.Zero;
    // Fin d'une exécution : la première planifie le suivi à +3 s, le suivi ne planifie rien.
    internal void Ran(DateTime end){bool wasFollow=follow;lastRun=end;if(wasFollow){follow=false;due=DateTime.MaxValue;}else{follow=true;due=end+Follow;}pendingSince=DateTime.MaxValue;}
    internal void Reset(){lastRun=DateTime.MinValue;due=pendingSince=DateTime.MaxValue;follow=false;}
}
