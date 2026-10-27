// language: C# 12, file: VerificationTools.cs, target: Windows 11 / .NET 8, workshop checks without desktop mutation
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PanneauxGlass;
internal static class VerificationTools {
    internal static int Run(){
        var checks=new List<object>();int failed=0;
        void Check(bool condition,string name){checks.Add(new{Name=name,Pass=condition});if(!condition)failed++;}
        Store.Load();
        try{
            var shot=ScreenShot.Capture(new System.Drawing.Rectangle(0,0,64,64));byte[] png=ScreenShot.Png(shot);
            Check(shot.PixelWidth==64&&png.Length>200&&png[0]==0x89&&png[1]==0x50,"Capture d’écran : pixels saisis et PNG valide");
        }catch(Exception ex){Store.Log("Vérification capture: "+ex);Check(false,"Capture d’écran : pixels saisis et PNG valide");}
        try{Check(Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()!=null,"Moteur de lecture de texte disponible");}
        catch(Exception ex){Store.Log("Vérification OCR: "+ex);Check(false,"Moteur de lecture de texte disponible");}
        try{var outputs=AudioSwitch.Devices(0);Check(outputs.Count>0&&outputs.All(device=>device.Id.Length>0),"Sorties audio énumérées");}
        catch(Exception ex){Store.Log("Vérification audio: "+ex);Check(false,"Sorties audio énumérées");}
        Check(SystemTools.Size(1536)=="2 Ko"&&SystemTools.Size(5L*1024*1024)=="5 Mo","Tailles de fichiers lisibles");
        Check(GlobalShortcut.Valid(2,0x72)&&GlobalShortcut.Label(2,0x72)=="Ctrl + F3"&&!GlobalShortcut.Valid(0,0x72),"Raccourcis F valides et libellés");
        var sample=Store.Entries.FirstOrDefault(e=>e.Name=="Google Chrome")??Store.Entries.FirstOrDefault();
        var logo=sample==null?null:IconLoader.Sample(sample);
        Check(logo is System.Windows.Media.Imaging.BitmapSource bitmap&&bitmap.PixelWidth>=16,"Logo réel d’une application disponible pour la commande rapide");
        var orphan=new Entry{Id="test:orphan",Name="Zotero",Target="shell:AppsFolder\\application-manquante"};
        var plate=IconLoader.InitialPlate(orphan);
        Check(plate is System.Windows.Media.ImageSource&&plate is System.Windows.Media.DrawingImage,"Un accès sans logo reçoit une vignette de secours, jamais un blanc");
        var shellEntry=Store.Entries.FirstOrDefault(e=>e.Target.StartsWith("shell:",StringComparison.OrdinalIgnoreCase));
        Check(shellEntry!=null,"Un accès Microsoft Store est présent dans le catalogue");
        var state=new Preferences();state.Normalize();
        Check(state.ClipboardHistory&&state.PinShortcut.Key==0x77&&state.CaptureShortcut.Key==0x72,"Préférences du quotidien par défaut");
        Check(Motion.LevelName is "Calme" or "Standard" or "Expressif","Niveau de mouvement lisible");
        var profile=new WorkspaceProfile{Name="Test",Targets=new(){"C:\\Windows\\notepad.exe"}};
        var round=JsonSerializer.Deserialize<WorkspaceProfile>(JsonSerializer.Serialize(profile,Store.Json),Store.Json)!;
        Check(round.Targets.Count==1&&round.Layout!=null,"Profils d’espace persistants");
        var clip=JsonSerializer.Deserialize<ClipboardItem>(JsonSerializer.Serialize(new ClipboardItem{Kind="text",Text="bonjour"},Store.Json),Store.Json)!;
        Check(clip.Text=="bonjour"&&clip.Id.Length>0,"Éléments du presse-papiers persistants");
        var pinned=new Preferences{Pins=new(){new(){Kind="memo",Text="devis vendredi",Width=10,Opacity=.1,Angle=40,Left=120,Top=240},new(){Kind="image",ImagePath="pins/exemple.png",Width=320,Height=180}}};
        pinned.Normalize();
        Check(pinned.Pins.Count==2&&pinned.Pins[0].Text=="devis vendredi"&&pinned.Pins[0].Width>=160&&pinned.Pins[0].Opacity>=.35&&Math.Abs(pinned.Pins[0].Angle)<=12,"Papier pense-bête : texte, taille, transparence et angle bornés");
        var stored=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(pinned,Store.Json),Store.Json)!;
        Check(stored.Pins.Count==2&&stored.Pins[0].Left==120&&stored.Pins[0].Top==240&&stored.Pins[1].ImagePath=="pins/exemple.png","Papier pense-bête : position et image conservées au redémarrage");
        try{
            var probe=new System.Windows.Window{ShowInTaskbar=false};var handle=new System.Windows.Interop.WindowInteropHelper(probe).EnsureHandle();
            Native.ClickThrough(handle,true);bool through=(Native.ExtendedStyle(handle)&0x20)!=0;
            Native.ClickThrough(handle,false);bool restored=(Native.ExtendedStyle(handle)&0x20)==0;
            probe.Close();Check(through&&restored,"Papier au-dessus de l’espace : clics traversants puis rendus");
        }catch(Exception ex){Store.Log("Vérification clics traversants: "+ex);Check(false,"Papier au-dessus de l’espace : clics traversants puis rendus");}
        var report=new{At=DateTimeOffset.Now,Passed=checks.Count-failed,Failed=failed,Checks=checks};
        File.WriteAllText(Path.Combine(Store.Root,"verification-tools.json"),JsonSerializer.Serialize(report,Store.Json));
        return failed==0?0:1;
    }
}
