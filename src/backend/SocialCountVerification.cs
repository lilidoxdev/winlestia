// C# 12 / .NET 8. Vérifie la machine d'état du compteur social (UnreadCounter) sans WebView2 : --verify-social-count.
using System;

namespace PanneauxGlass;

internal static class SocialCountVerification {
    internal static int Run(){
        bool passed=true;
        void Check(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);passed&=ok;}
        var t0=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);

        var a=new UnreadCounter();int rises=0;
        foreach(var u in new[]{0,3,0,3})rises+=a.OnTitle(u,t0).Rise;
        Check("[0,3,0,3] -> 3 (pas 6), hausse signalee une seule fois (count "+a.Count+", hausses "+rises+")",a.Count==3&&rises==3);

        var b=new UnreadCounter();
        foreach(var u in new[]{1,2,1,2})b.OnTitle(u,t0);
        Check("[1,2,1,2] -> 2 (count "+b.Count+")",b.Count==2);

        var g=new UnreadCounter();g.OnTitle(2,t0);
        Check("titre=2 : TitleSeen, notification ne compte pas (Count "+g.Count+")",g.TitleSeen&&g.Count==2);
        Check("credit : notif < 3 s apres hausse fusionnee (2 credits), pas de 3e",g.ConsumeCredit(t0.AddSeconds(1))&&g.ConsumeCredit(t0.AddSeconds(2))&&!g.ConsumeCredit(t0.AddSeconds(2)));
        Check("credit expire apres 3 s",!new UnreadCounter().ConsumeCredit(t0));

        var d=new UnreadCounter();d.OnTitle(5,t0);var z=d.OnTitle(0,t0);
        Check("titre 5 puis 0 : toujours 5 avant le delai",d.Count==5&&z.ZeroToken>0);
        Check("delai ecoule -> 0",d.ZeroExpired(z.ZeroToken)&&d.Count==0);

        var e=new UnreadCounter();e.OnTitle(5,t0);var ze=e.OnTitle(0,t0);e.OnTitle(4,t0);
        Check("retour du titre avant 5 s annule l'effacement",!e.ZeroExpired(ze.ZeroToken)&&e.Count==4);

        var f=new UnreadCounter{Count=2};var z2=f.OnTitle(0,t0);
        Check("reseau sans compteur de titre : Count conserve, aucun delai arme",f.Count==2&&z2.ZeroToken==0&&!f.ZeroExpired(2));

        var h=new UnreadCounter{Count=99,Legacy=true,Title=4};
        Check("migration : ancien compteur remplace par le titre des qu'il s'affiche",h.Absorb()&&h.Count==4);
        var h0=new UnreadCounter{Count=7,Legacy=true};
        Check("migration : compteur notif-seul conserve tant qu'aucun titre n'est apparu",!h0.Absorb()&&h0.Count==7);
        var i=new UnreadCounter{Count=7};
        Check("absorption sans titre ni migration : compteur notif conserve",!i.Absorb()&&i.Count==7);

        var nb=new UnreadCounter();nb.NoteNotification(t0);var rb=nb.OnTitle(1,t0.AddSeconds(2));
        Check("notif puis hausse de titre < 3 s : fusion (AfterNotif, pas de 2e son/historique)",rb.Rise==1&&rb.AfterNotif&&!nb.ConsumeCredit(t0.AddSeconds(2)));
        var nc=new UnreadCounter();nc.NoteNotification(t0);var rc=nc.OnTitle(1,t0.AddSeconds(5));
        Check("notif puis hausse de titre > 3 s : hausse normale",rc.Rise==1&&!rc.AfterNotif);
        var nd=new UnreadCounter();var rd=nd.OnTitle(1,t0);
        Check("hausse seule : pas de fusion",rd.Rise==1&&!rd.AfterNotif);

        var o=new UnreadCounter{Count=4};
        Check("reseau notif-seul : ouverture -> 0",o.ResetOnOpen()&&o.Count==0&&!o.ResetOnOpen());
        var o2=new UnreadCounter();o2.OnTitle(3,t0);
        Check("reseau a titre : ouverture ne remet pas a 0",!o2.ResetOnOpen()&&o2.Count==3);

        Console.WriteLine(passed?"ALL PASSED":"FAILED");
        return passed?0:1;
    }
}
