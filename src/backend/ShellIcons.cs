// language: C# 12, file: ShellIcons.cs, target: Windows 11 / .NET 8 WPF, original rounded vector interface icons
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PanneauxGlass;
internal static class ShellIcons {
    static readonly Dictionary<string,string> paths=new(){
        ["dashboard"]="M3,3 L9,3 9,9 3,9Z M15,3 L21,3 21,9 15,9Z M3,15 L9,15 9,21 3,21Z M15,15 L21,15 21,21 15,21Z",
        ["music"]="M3,5 L14,5 M3,9 L11,9 M3,13 L9,13 M15,7 L15,18 C15,21 10,22 10,19 C10,16 15,16 15,18 M15,7 L21,5 21,10 15,12",
        ["performance"]="M4,18 C-1,7 7,1 16,4 M20,8 C23,11 23,15 21,18 L4,18 M12,13 L18,6 M7,14 L7,15",
        ["spaces"]="M12,8 L12,12 M6,15 L10,12 14,12 18,15 M9,4 A3,3 0 1 0 15,4 A3,3 0 1 0 9,4 M2,19 A3,3 0 1 0 8,19 A3,3 0 1 0 2,19 M16,19 A3,3 0 1 0 22,19 A3,3 0 1 0 16,19",
        ["windows"]="M3,3 L10,3 10,10 3,10Z M14,3 L21,3 21,10 14,10Z M3,14 L10,14 10,21 3,21Z M14,14 L21,14 21,21 14,21Z",
        ["volume"]="M3,9 L7,9 12,5 12,19 7,15 3,15Z M16,8 Q21,12 16,16 M19,5 Q26,12 19,19",
        ["sun"]="M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12 M12,1 L12,3 M12,21 L12,23 M1,12 L3,12 M21,12 L23,12 M4,4 L5.5,5.5 M18.5,18.5 L20,20 M4,20 L5.5,18.5 M18.5,5.5 L20,4",
        ["wifi"]="M2,8 Q12,0 22,8 M5,12 Q12,6 19,12 M8,16 Q12,12 16,16 M11.5,19 L12.5,19",
        ["bluetooth"]="M12,2 L19,8 6,19 M12,22 L19,16 6,5 M12,2 L12,22",
        ["search"]="M3,10 A7,7 0 1 0 17,10 A7,7 0 1 0 3,10 M15,15 L22,22",
        ["settings"]="M9,3 L15,3 16,6 19,6 22,11 20,14 20,17 15,21 12,19 9,21 4,17 4,14 2,11 5,6 8,6Z M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12",
        ["files"]="M2,7 L2,20 Q2,22 4,22 L20,22 Q22,22 22,20 L22,8 12,8 10,4 4,4 Q2,4 2,7Z",
        ["play"]="M8,4 L20,12 8,20Z",["pause"]="M7,4 L7,20 M17,4 L17,20",["previous"]="M5,5 L5,19 M19,5 L8,12 19,19Z",["next"]="M19,5 L19,19 M5,5 L16,12 5,19Z",
        ["close"]="M5,5 L19,19 M19,5 L5,19",["bell"]="M5,17 L5,9 Q5,3 12,3 Q19,3 19,9 L19,17 22,19 2,19Z M9,22 L15,22",
        ["cpu"]="M6,6 L18,6 18,18 6,18Z M9,9 L15,9 15,15 9,15Z M9,2 L9,6 M15,2 L15,6 M9,18 L9,22 M15,18 L15,22 M2,9 L6,9 M2,15 L6,15 M18,9 L22,9 M18,15 L22,15"
    };
    internal static FrameworkElement View(string name,double size=21,Brush? color=null){if(!paths.TryGetValue(name,out var data))data=paths["dashboard"];var brush=(color??UI.White).CloneCurrentValue();var pen=new Pen(brush,1.65){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};var drawing=new GeometryDrawing(name=="windows"?brush:null,pen,Geometry.Parse(data));var group=new DrawingGroup();group.Children.Add(drawing);group.Freeze();return new Viewbox{Width=size,Height=size,Child=new Image{Source=new DrawingImage(group),Width=24,Height=24,Stretch=Stretch.Uniform}};}
    internal static Button Button(string icon,string label,Action action,double size=21){var button=UI.Button("",action);button.Content=View(icon,size);button.ToolTip=label;button.Width=button.Height=38;button.Padding=new Thickness(8);System.Windows.Automation.AutomationProperties.SetName(button,label);return button;}
}
