// language: C# 12, file: ControlModels.cs, target: Windows 11 / .NET 8
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass;
public sealed class WindowsShortcut {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Name {get;set;}="";public string Target {get;set;}="";public string Arguments {get;set;}="";public string Glyph {get;set;}="\uE713";
}
public sealed class ControlPreferences {
    public List<string> Modules {get;set;}=new(){"connections","media","levels","actions","shortcuts"};
    public List<string> HiddenModules {get;set;}=new();public bool ShowVolume {get;set;}=true;public bool ShowBrightness {get;set;}=true;
    public List<WindowsShortcut> Shortcuts {get;set;}=Defaults();public string MediaApp {get;set;}="";
    public static List<WindowsShortcut> Defaults()=>new(){
        new(){Id="capture",Name="Capture",Target="ms-screenclip:",Glyph="\uE722"},new(){Id="calculator",Name="Calculatrice",Target="calc.exe",Glyph="\uE8EF"},
        new(){Id="clock",Name="Horloge",Target="ms-clock:",Glyph="\uE917"},new(){Id="notepad",Name="Notes",Target="notepad.exe",Glyph="\uE70B"},
        new(){Id="explorer",Name="Fichiers",Target="explorer.exe",Glyph="\uE8B7"},
        new(){Id="sound",Name="Audio",Target="ms-settings:sound",Glyph="\uE767"},new(){Id="settings",Name="Réglages",Target="ms-settings:",Glyph="\uE713"}
    };
    public static IEnumerable<WindowsShortcut> Library()=>Defaults().Concat(new[]{
        new WindowsShortcut{Name="Concentration",Target="ms-settings:quiethours",Glyph="\uE708"},new WindowsShortcut{Name="Écran",Target="ms-settings:display",Glyph="\uE7F4"},new WindowsShortcut{Name="Wi-Fi",Target="ms-settings:network-wifi",Glyph="\uE701"},new WindowsShortcut{Name="Bluetooth",Target="ms-settings:bluetooth",Glyph="\uE702"},
        new WindowsShortcut{Name="Réseau",Target="ms-settings:network-status",Glyph="\uE839"},new WindowsShortcut{Name="Notifications",Target="ms-settings:notifications",Glyph="\uE7F4"},
        new WindowsShortcut{Name="Stockage",Target="ms-settings:storagesense",Glyph="\uEDA2"},new WindowsShortcut{Name="Applications",Target="ms-settings:appsfeatures",Glyph="\uE71D"},
        new WindowsShortcut{Name="Personnalisation",Target="ms-settings:personalization",Glyph="\uE790"},new WindowsShortcut{Name="Alimentation",Target="ms-settings:powersleep",Glyph="\uE7E8"},
        new WindowsShortcut{Name="Gestionnaire des tâches",Target="taskmgr.exe",Glyph="\uE9D9"},new WindowsShortcut{Name="Appareil photo",Target="microsoft.windows.camera:",Glyph="\uE722"}
    });
    public void Normalize(){Modules??=new();HiddenModules??=new();Shortcuts??=Defaults();MediaApp??="";string[] valid={"connections","media","levels","actions","shortcuts"};Modules=Modules.Where(valid.Contains).Distinct().Concat(valid.Except(Modules)).ToList();HiddenModules=HiddenModules.Where(valid.Contains).Distinct().ToList();Shortcuts=Shortcuts.Where(s=>s!=null&&!string.IsNullOrWhiteSpace(s.Target)).Take(48).ToList();foreach(var s in Shortcuts){if(string.IsNullOrWhiteSpace(s.Id))s.Id=Guid.NewGuid().ToString("N");if(string.IsNullOrWhiteSpace(s.Name))s.Name=s.Target;if(string.IsNullOrWhiteSpace(s.Glyph))s.Glyph="\uE713";s.Arguments??="";}}
}
