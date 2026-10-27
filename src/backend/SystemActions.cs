// language: C# 12, file: SystemActions.cs, target: Windows 11 / .NET 8, reversible system commands with explicit confirmation
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace PanneauxGlass;
// Every destructive command asks once, in the same glass layer, then runs. Nothing touches the user's files.
internal static class SystemActions {
    [DllImport("user32.dll")]static extern bool LockWorkStation();
    [DllImport("powrprof.dll")]static extern bool SetSuspendState(bool hibernate,bool forceCritical,bool disableWakeEvent);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHEmptyRecycleBin(IntPtr handle,string? root,uint flags);
    internal static void Lock(Coordinator owner){owner.CollapseImmediately();LockWorkStation();}
    internal static void Sleep(Coordinator owner){owner.CollapseImmediately();if(!SetSuspendState(false,false,false))owner.Message("La mise en veille n’a pas pu être déclenchée sur ce PC.");}
    internal static void Screenshot(Coordinator owner)=>owner.LaunchWindowsShortcut(new WindowsShortcut{Name="Capture",Target="ms-screenclip:"});
    internal static void TaskManager(Coordinator owner){owner.CollapseImmediately();TryStart("taskmgr.exe",owner);}
    internal static void EmptyBin(Coordinator owner){
        if(!owner.Confirm("Vider la corbeille ?\n\nLes fichiers qu’elle contient seront supprimés définitivement, sans passer par le Bureau."))return;
        int code=SHEmptyRecycleBin(IntPtr.Zero,null,0x1|0x2|0x4);
        owner.ShowToast(code==0?"Corbeille vidée":"La corbeille est déjà vide");
    }
    internal static void Power(Coordinator owner,bool restart){
        string verb=restart?"Redémarrer":"Éteindre";
        if(!owner.Confirm(verb+" le PC maintenant ?\n\nEnregistre ton travail avant de continuer."))return;
        owner.CollapseImmediately();TryStart(restart?"shutdown.exe /r /t 0":"shutdown.exe /s /t 0",owner);
    }
    internal static void Copy(Coordinator owner,string text,string label){
        try{if(string.IsNullOrEmpty(text))return;Clipboard.SetText(text);owner.ShowToast(label+" copié");}
        catch(Exception ex){owner.ShowToast("Copie impossible : "+ex.Message);}
    }
    static void TryStart(string command,Coordinator owner){
        try{var parts=command.Split(' ',2);Process.Start(new ProcessStartInfo(parts[0],parts.Length>1?parts[1]:""){UseShellExecute=false,CreateNoWindow=true});}
        catch(Exception ex){owner.Message("La commande « "+command+" » n’a pas pu être lancée.\n"+ex.Message);}
    }
}
