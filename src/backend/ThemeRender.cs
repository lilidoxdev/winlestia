// language: C# 12, file: ThemeRender.cs, target: Windows 11 / .NET 8 WPF, off-screen theme and palette renders
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PanneauxGlass;
// Verification without a desktop: the coordinator paints its real windows, but every panel is moved
// far outside the virtual screen, so nothing appears and the pointer is never taken. Each theme is
// exported to its own PNG, then the app leaves on its own.
internal static class ThemeRender {
    internal static void Themes(Coordinator coordinator,string? only){
        var ids=Theme.Catalogue.Select(theme=>theme.Id).Where(id=>string.IsNullOrWhiteSpace(only)||id==only).ToList();
        if(ids.Count==0)ids.Add(Theme.Current.Id);
        int index=-1;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(900)};
        timer.Tick+=(_,_)=>{
            if(index>=0&&index<ids.Count){coordinator.ExportPreview("render-"+ids[index]+".png");Store.Log("Rendu hors écran · "+ids[index]);}
            index++;
            if(index>=ids.Count){timer.Stop();Application.Current.Shutdown();return;}
            coordinator.SetTheme(ids[index]);
        };
        timer.Start();
    }
    internal static void Palette(Coordinator coordinator,string query){
        coordinator.PreviewPaletteQuery(query);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(2200)};
        timer.Tick+=(_,_)=>{
            timer.Stop();
            coordinator.ExportPreview("render-palette.png");
            Store.Log("Rendu hors écran · palette");
            Application.Current.Shutdown();
        };
        timer.Start();
    }
}
