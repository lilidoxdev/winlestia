// language: C# 12, file: DrawerShape.cs, target: Windows 11 / .NET 8 WPF, edge-connected drawer geometry
using System;
using System.Windows;
using System.Windows.Media;

namespace PanneauxGlass;
internal static class DrawerShape {
    internal static Geometry Create(double width,double height){double shoulder=Math.Min(20,width/10),radius=Math.Min(25,height/3);var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(new Point(0,0),true,true);path.LineTo(new Point(width,0),true,false);path.QuadraticBezierTo(new Point(width-shoulder,0),new Point(width-shoulder,shoulder),true,false);path.LineTo(new Point(width-shoulder,height-radius),true,false);path.QuadraticBezierTo(new Point(width-shoulder,height),new Point(width-shoulder-radius,height),true,false);path.LineTo(new Point(shoulder+radius,height),true,false);path.QuadraticBezierTo(new Point(shoulder,height),new Point(shoulder,height-radius),true,false);path.LineTo(new Point(shoulder,shoulder),true,false);path.QuadraticBezierTo(new Point(shoulder,0),new Point(0,0),true,false);}geometry.Freeze();return geometry;}
}
