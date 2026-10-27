// language: C# 12, file: InteractionVerification.cs, target: Windows 11 / .NET 8 WPF, native binding and interruption regression checks
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace PanneauxGlass;
internal static class InteractionVerification {
    internal static int Shortcuts(){int failed=0;var checks=new List<object>();void Check(bool pass,string name){checks.Add(new{Name=name,Pass=pass});if(!pass)failed++;}Window first=new Window(),second=new Window();var a=new WindowInteropHelper(first).EnsureHandle();var b=new WindowInteropHelper(second).EnsureHandle();
        try{var settings=JsonSerializer.Deserialize<Preferences>("{\"Favorites\":[\"keep\"],\"Shortcut\":null}",Store.Json)!;settings.Normalize();Check(settings.Shortcut.Key==0x70&&settings.Shortcut.Modifiers==2&&settings.Favorites.Single()=="keep","Migration : Ctrl F1 ajouté sans perte de personnalisation");
            Check(GlobalShortcut.Valid(2,0x70)&&GlobalShortcut.Valid(7,0x41)&&!GlobalShortcut.Valid(0,0x70)&&!GlobalShortcut.Valid(2,0x7B)&&!GlobalShortcut.Valid(3,0x08),"Combinaisons valides, touches seules et F12 réservée refusées");
            using(var binding=new GlobalShortcut(a,new(){Modifiers=7,Key=0x86})){Check(binding.Error=="","Enregistrement Windows réel du raccourci de test");Check(Native.RegisterHotKey(b,90,0x4007,0x85),"Réservation concurrente d’une combinaison différente");try{Check(!binding.TrySet(7,0x85,out var error)&&!string.IsNullOrWhiteSpace(error),"Conflit détecté et expliqué");bool stolen=Native.RegisterHotKey(b,91,0x4007,0x86);Check(!stolen,"Le raccourci précédent reste réservé après échec");if(stolen)Native.UnregisterHotKey(b,91);}finally{Native.UnregisterHotKey(b,90);}Check(binding.TrySet(7,0x84,out _),"Remplacement atomique par une combinaison libre");bool released=Native.RegisterHotKey(b,92,0x4007,0x86);Check(released,"L’ancienne combinaison est libérée après succès");if(released)Native.UnregisterHotKey(b,92);}
            bool disposed=Native.RegisterHotKey(b,93,0x4007,0x84);Check(disposed,"Les raccourcis sont libérés à l’arrêt");if(disposed)Native.UnregisterHotKey(b,93);
            Check(new Preferences().PaletteShortcut.Modifiers==2&&new Preferences().PaletteShortcut.Key==0x71,"Commande rapide : Ctrl F2 par défaut, jamais Ctrl K");
            Check(Preferences.DefaultQuickTabs().Where(t=>t.Target.StartsWith("glass:")).All(t=>QuickTabsWindow.Views.Any(v=>v.Target==t.Target)),"Onglets rapides : chaque cible par défaut est résolue par l’éditeur");
            Check(Preferences.DefaultQuickTabs().All(t=>GlobalShortcut.Label(2,0x71).Length>0),"Raccourci et onglets cohabitent dans une préférence unique");
            Check(Theme.Catalogue.Count>=4&&Theme.Catalogue.Select(t=>t.Id).Distinct().Count()==Theme.Catalogue.Count,"Au moins quatre thèmes globaux, identifiants uniques");
            Check(Theme.Catalogue.All(t=>t.Surface!=t.HaloA&&t.Accent!=t.Text&&t.RadiusScale is >0 and <=2),"Chaque thème fournit verre, halos, accent et échelle d’angle");
            Check(Theme.Catalogue.Count(t=>t.Kind!=SurfaceKind.Glass)>=4,"Au moins quatre thèmes changent la matière et les formes");
            Check(Theme.Catalogue.Select(t=>t.Tiles).Distinct().Count()>=6&&Theme.Catalogue.Select(t=>t.Heading).Distinct().Count()>=4&&Theme.Catalogue.Select(t=>t.Frame).Distinct().Count()>=5,"Les thèmes varient aussi la disposition, la typographie et le cadre des contrôles");
            Check(new[]{"bento","neon","retro","suisse","zen"}.All(id=>Theme.Find(id)!=null),"Cinq matières supplémentaires sont proposées");
            Check(Theme.Find("bento")!.Tiles=="bento"&&Theme.Find("bento")!.Frame=="flat","Bento : cases pleines et contrôles plats");
            Check(Theme.Find("neon")!.Frame=="neon"&&Theme.Find("neon")!.ReflectionGain==0,"Néon : cadres lumineux, aucun reflet");
            Check(Theme.Find("retro")!.Heading=="pixel"&&Theme.Find("retro")!.Frame=="bevel","Rétro : typographie pixel et boutons biseautés");
            Check(Theme.Find("suisse")!.Tiles=="index"&&Theme.Find("suisse")!.Frame=="outline","Suisse : index numéroté et cadres filaires");
            Check(Theme.Find("zen")!.Tiles=="stack"&&Theme.Find("zen")!.Heading=="serif","Zen : rangées larges et titres serif");
            Theme.Use("amethyste");Check(UI.White.Color==Theme.Current.Text&&Theme.Current.Id=="amethyste","Changer de thème recolore le texte partagé");
            Theme.Use("calque");Check(Theme.Current.Light&&UI.White.Color==Theme.Current.Text&&Theme.Current.Tiles=="cards"&&Theme.Current.Heading=="serif","Papier calque : encre sombre, titres serif et fiches");
            Theme.Use("encre");Check(UI.Font==UI.Mono&&Theme.Current.Tiles=="list"&&Theme.Current.Kind==SurfaceKind.Ink,"Encre : chasse fixe et listes");
            Theme.Use("inconnu");Check(Theme.Current.Id=="nuit","Un thème inconnu retombe sur Nuit boréale");
            foreach(var dimensions in new[]{new Size(3440,1440),new Size(1920,1080),new Size(1366,768)}){var work=new Rect(-50,20,dimensions.Width,dimensions.Height);var left=ControlPairLayout.Calculate(work,false,true);Check(work.Contains(left.Pc)&&work.Contains(left.Home)&&left.Pc.Right<left.Home.Left,"Panneau PC à gauche sans chevauchement : "+dimensions);if(dimensions.Width>=1700){var both=ControlPairLayout.Calculate(work,true,true);Check(work.Contains(both.Pc)&&work.Contains(both.Controls)&&both.Pc.Right<both.Home.Left&&both.Home.Right<both.Controls.Left,"Trois surfaces dans l’écran : "+dimensions);}}
        }catch(Exception ex){Check(false,ex.ToString());}finally{first.Close();second.Close();}File.WriteAllText(Path.Combine(Store.Root,"verification-shortcuts-5.5.json"),JsonSerializer.Serialize(new{At=DateTimeOffset.Now,Passed=checks.Count-failed,Failed=failed,Checks=checks},Store.Json));return failed==0?0:1;}
    internal static async void Run(Coordinator app){int failed=0;var checks=new List<object>();var measurements=new List<object>();void Check(bool pass,string name){checks.Add(new{Name=name,Pass=pass});if(!pass)failed++;}
        async Task Measure(string name,int milliseconds){using var process=Process.GetCurrentProcess();var start=process.TotalProcessorTime;var clock=Stopwatch.StartNew();await Task.Delay(milliseconds);process.Refresh();measurements.Add(new{Name=name,ElapsedMs=clock.Elapsed.TotalMilliseconds,CpuMs=(process.TotalProcessorTime-start).TotalMilliseconds,TotalCpuPercent=(process.TotalProcessorTime-start).TotalMilliseconds/clock.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,WorkingSetMB=process.WorkingSet64/1048576d,PrivateMB=process.PrivateMemorySize64/1048576d,Samples=app.PcActivity.samples,Sampling=app.PcActivity.running});}
        try{app.ShowHome();await Task.Delay(500);await Measure("Accueil stable",8000);app.Collapse();await Task.Delay(20);app.Open();await Task.Delay(350);Check(app.UiActivity.open&&!app.UiActivity.closing&&app.UiActivity.visible>=3,"Réouverture pendant le repli : dernière intention respectée");
            for(int i=0;i<8;i++){app.ToggleControls();await Task.Delay(28);}await Task.Delay(400);Check(app.UiActivity.open&&!app.UiActivity.closing,"Huit bascules rapides ne laissent pas de fermeture différée");
            for(int i=0;i<4;i++){app.TogglePc();await Task.Delay(15);}app.TogglePc();for(int wait=0;wait<40&&app.PcActivity.samples==0;wait++)await Task.Delay(500);Check(app.PcActivity.samples>0&&app.PcActivity.running,"Réouvertures rapides : un collecteur actif et redémarrage possible");await Measure("Accueil + état PC",12000);app.TogglePc();await Task.Delay(1500);long samples=app.PcActivity.samples;await Measure("Accueil après repli PC",6000);Check(!app.PcActivity.running&&app.PcActivity.samples==samples,"Aucun relevé ni boucle de capteurs après masquage");
            app.ShowWorkspace();await Task.Delay(350);app.ShowHome();await Task.Delay(350);Check(app.UiActivity.open&&app.UiActivity.visible==4,"Retour depuis les quatre panneaux : accueil, horloge et deux flèches seulement");app.CollapseImmediately();await Task.Delay(250);
            app.ShowPalette();await Task.Delay(400);Check(app.PaletteActivity&&app.PaletteVisible,"Commande rapide disponible launcher replié");app.ClosePalette();await Task.Delay(250);Check(!app.PaletteActivity&&!app.PaletteVisible,"Commande rapide refermée sans laisser de surface fantôme");app.ShowPalette();await Task.Delay(300);app.CollapseImmediately();await Task.Delay(250);Check(!app.PaletteVisible,"Replier l’espace ferme aussi la palette ouverte");
            var editor=new QuickTabsWindow(app){ShowInTaskbar=true};editor.Show();await Task.Delay(350);bool editorOpened=editor.IsVisible;app.CollapseImmediately();await Task.Delay(350);Check(editorOpened&&!editor.IsVisible,"Replier l’espace ferme aussi l’éditeur d’onglets");
            await Measure("Interface repliée",6000);Check(!app.UiActivity.open&&!app.UiActivity.closing&&app.UiActivity.visible==0,"Repli complet de toutes les surfaces");
        }catch(Exception ex){Check(false,ex.ToString());}File.WriteAllText(Path.Combine(Store.Root,"verification-interactions-5.5.json"),JsonSerializer.Serialize(new{At=DateTimeOffset.Now,Passed=checks.Count-failed,Failed=failed,Checks=checks,Measurements=measurements},Store.Json));Application.Current.Shutdown(failed==0?0:1);
    }
}
