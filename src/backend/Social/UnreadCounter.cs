// Machine d'état pure du compteur de non-lus, extraite de SocialService.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PanneauxGlass;

// Machine d'état pure du compteur (sans WebView2 ni horloge système : le temps est passé en paramètre) ; testée par
// --verify-social-count.
internal sealed class UnreadCounter
{
    internal readonly record struct TitleResult(bool Changed, int Rise, int ZeroToken, bool AfterNotif = false);
    public int Count, Title, Credit; public bool TitleSeen, Legacy;
    int zeroToken; DateTime riseAt = DateTime.MinValue, notifAt = DateTime.MinValue;

    // Titre lu. Rise = hausse réelle par rapport au compteur affiché (son + historique). ZeroToken>0 = armer le délai de 5 s.
    public TitleResult OnTitle(int unread, DateTime now)
    {
        Title = unread;
        zeroToken++;
        if (unread == 0)
            return new(false, 0, TitleSeen ? zeroToken : 0);
        TitleSeen = true;
        Legacy = false;
        int before = Count;
        Count = unread;
        int rise = Math.Max(0, unread - before);
        // Notification arrivée avant la hausse (<=3 s) : déjà chimée + historisée, la hausse ne doit pas doubler (AfterNotif).
        bool after = rise > 0 && (now - notifAt).TotalSeconds is >= 0 and <= 3;
        if (rise > 0)
        {
            riseAt = now;
            Credit = after ? Math.Max(0, rise - 1) : rise;
        }
        return new(unread != before, rise, 0, after);
    }
    public bool ZeroExpired(int token)
    {
        if (token != zeroToken || Title != 0 || !TitleSeen || Count == 0)
            return false;
        Count = 0;
        return true;
    }
    public void CancelZero() => zeroToken++;
    // Une notification arrivée dans les 3 s d'une hausse du titre consomme un crédit (déjà comptée par le titre).
    public void NoteNotification(DateTime now) => notifAt = now;
    // Ouverture de la page : un réseau sans compteur de titre n'a pas d'autre moyen de revenir à 0.
    public bool ResetOnOpen()
    {
        if (TitleSeen || Count == 0)
            return false;
        Count = 0;
        Credit = 0;
        return true;
    }
    public bool ConsumeCredit(DateTime now)
    {
        if ((now - riseAt).TotalSeconds <= 3 && Credit > 0)
        {
            Credit--;
            return true;
        }
        return false;
    }
    // Absorption initiale silencieuse : le titre affiché devient le compteur (ancien fichier : même un titre à 0 remplace).
    public bool Absorb()
    {
        if (Title > 0)
            TitleSeen = true;
        if (!TitleSeen)
            return false; // ancien fichier : un compteur de notifications valide n'est remplacé que si le réseau affiche un titre
        Legacy = false;
        bool changed = Count != Title;
        Count = Title;
        return changed;
    }
    public void Reset()
    {
        Count = 0;
        Title = 0;
        Credit = 0;
        TitleSeen = false;
        Legacy = false;
        zeroToken++;
    }
}
