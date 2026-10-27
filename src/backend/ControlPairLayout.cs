// language: C# 12, file: ControlPairLayout.cs, target: Windows 11 / .NET 8 WPF
using System;
using System.Windows;

namespace PanneauxGlass;
// The clock band sits directly on top of the home surface and shares its width; the group stays centred.
internal sealed record ControlPairLayout(Rect Clock,Rect Home,Rect Controls,Rect LeftArrow,Rect RightArrow,Rect Pc) {
    internal static ControlPairLayout Calculate(Rect work,bool expanded,bool pcExpanded=false,bool clockBand=true){
        double controlsWidth=work.Width>=1550?480:Math.Clamp(work.Width*.31,350,440),gap=72;
        double clockHeight=clockBand?Math.Clamp(work.Height*.075,84,112):1,stack=clockBand?clockHeight+14:0;
        double height=Math.Min(820,Math.Max(300,work.Height-60-stack));
        int sides=(expanded?1:0)+(pcExpanded?1:0);double homeWidth=Math.Min(1200,Math.Max(400,work.Width-(sides>0?sides*(controlsWidth+gap)+144:160))),groupWidth=homeWidth+sides*(controlsWidth+gap);
        double x=work.X+(work.Width-groupWidth)/2+(pcExpanded?controlsWidth+gap:0),y=work.Y+(work.Height-(height+stack))/2;
        var clock=new Rect(x,y,homeWidth,clockHeight);var home=new Rect(x,y+stack,homeWidth,height);
        var controls=new Rect(home.Right+gap,home.Y,controlsWidth,home.Height);var pc=new Rect(home.Left-gap-controlsWidth,home.Y,controlsWidth,home.Height);
        double arrowX=home.Right+(expanded?11:16);
        return new(clock,home,controls,new Rect(Math.Max(work.Left+5,x-66),home.Y+home.Height/2-25,50,50),new Rect(Math.Min(work.Right-55,arrowX),home.Y+home.Height/2-25,50,50),pc);
    }
}
