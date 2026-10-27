// language: C# 12, file: CornerProof.cs, target: Windows 11 / .NET 8 WPF, native alpha comparison on a contrasting owner surface
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PanneauxGlass;
internal static class CornerProof {
    internal static void Show(){UI.Install();Store.Load();var canvas=new Canvas();var background=new Window{Title="Panneaux Glass — contrôle des coins",Width=1000,Height=370,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brushes.Coral,Content=canvas,Topmost=true};
        for(int y=0;y<370;y+=24)for(int x=0;x<1000;x+=24){var cell=new Rectangle{Width=24,Height=24,Fill=(x/24+y/24)%2==0?Brushes.Coral:Brushes.LightSeaGreen};Canvas.SetLeft(cell,x);Canvas.SetTop(cell,y);canvas.Children.Add(cell);}
        var title=UI.Text("Ancien fond à gauche · masque alpha à droite",22,Brushes.White,true);Canvas.SetLeft(title,35);Canvas.SetTop(title,25);canvas.Children.Add(title);
        background.Loaded+=(_,_)=>{double x=background.Left,y=background.Top;Native.Layered=false;var old=new GlassWindow("Panneaux Glass — ancien découpage"){Owner=background,ShowInTaskbar=true,Radius=42};old.ContentHost.Children.Add(UI.Text("Ancien",20));old.MoveInto(x+35,y+100,440,84,0,false);Native.Layered=true;var fixedWindow=new GlassWindow("Panneaux Glass — masque alpha"){Owner=background,ShowInTaskbar=true,Radius=42};fixedWindow.ContentHost.Children.Add(UI.Text("Masque alpha",20));fixedWindow.MoveInto(x+525,y+100,440,84,0,false);};
        background.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)System.Windows.Application.Current.Shutdown();};background.Show();
    }
}
