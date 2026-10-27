// language: C# 12, file: ClockWindow.cs, target: Windows 11 / .NET 8 WPF, large glass clock above the home surface
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
// The clock only ticks while the home is open: no dispatcher work behind a closed launcher.
internal sealed class ClockWindow:GlassWindow {
    readonly Coordinator owner;readonly TextBlock hour=UI.Text("",54,null,true),seconds=UI.Text("",15,UI.Dim),day=UI.Text("",13,UI.White),greeting=UI.Text("",11,UI.Dim);
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1000)};readonly RadialGradientBrush sheen=new(){RadiusX=.42,RadiusY=1.5};readonly Button shortcutChip;double lastX=-1,lastY=-1;
    internal string Time=>hour.Text;internal string Date=>day.Text;
    internal ClockWindow(Coordinator coordinator):base("Panneaux Glass — heure et date"){
        owner=coordinator;Radius=38;ContentHost.Margin=new Thickness(32,0,24,0);ContentHost.VerticalAlignment=VerticalAlignment.Center;
        hour.Typography.NumeralAlignment=FontNumeralAlignment.Tabular;hour.Margin=new Thickness(0,0,0,0);seconds.VerticalAlignment=VerticalAlignment.Bottom;seconds.Margin=new Thickness(7,0,0,11);seconds.Typography.NumeralAlignment=FontNumeralAlignment.Tabular;
        var time=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand,Background=Brushes.Transparent};time.Children.Add(hour);time.Children.Add(seconds);
        time.ToolTip="Ouvrir la commande rapide · "+owner.PaletteLabel;System.Windows.Automation.AutomationProperties.SetName(time,"Heure · ouvrir la commande rapide");
        time.MouseLeftButtonUp+=(_,_)=>owner.TogglePalette();
        var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(26,0,0,0)};
        var divider=new Border{Width=1,Height=38,Background=UI.Brush("#24FFFFFF"),Margin=new Thickness(0,0,24,0),VerticalAlignment=VerticalAlignment.Center};
        day.FontWeight=FontWeights.Medium;texts.Children.Add(day);greeting.Margin=new Thickness(0,5,0,0);texts.Children.Add(greeting);
        var left=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};left.Children.Add(time);left.Children.Add(divider);left.Children.Add(texts);
        var chips=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
        shortcutChip=Chip(owner.PaletteLabel,"Commande rapide · "+owner.PaletteLabel,"\uE721",()=>owner.TogglePalette());chips.Children.Add(shortcutChip);
        chips.Children.Add(Chip("Note","Note rapide","\uE70B",()=>owner.OpenNotes()));
        chips.Children.Add(Chip("Minuteur","Régler un minuteur","\uE917",()=>owner.OpenFocus()));
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        layout.Children.Add(left);Grid.SetColumn(chips,2);layout.Children.Add(chips);ContentHost.Children.Add(layout);
        var highlight=new Border{Background=sheen,IsHitTestVisible=false,Opacity=.9};Panel.SetZIndex(highlight,99);Surface.Children.Add(highlight);
        PreviewMouseMove+=(_,e)=>{var point=e.GetPosition(this);if(ActualWidth<1||ActualHeight<1)return;double x=Math.Clamp(point.X/ActualWidth,0,1),y=Math.Clamp(point.Y/ActualHeight,0,1);if(Math.Abs(x-lastX)<.02&&Math.Abs(y-lastY)<.02)return;lastX=x;lastY=y;var origin=new Point(x,y);sheen.Center=origin;sheen.GradientOrigin=origin;};
        MouseLeave+=(_,_)=>{lastX=lastY=-1;var origin=new Point(.22,.5);sheen.Center=origin;sheen.GradientOrigin=origin;};
        timer.Tick+=(_,_)=>Update();IsVisibleChanged+=(_,_)=>{if(IsVisible){Update();timer.Start();}else timer.Stop();};Closed+=(_,_)=>timer.Stop();
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(26,255,255,255),0));sheen.GradientStops.Add(new GradientStop(Color.FromArgb(10,196,244,255),.45));sheen.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        sheen.Center=sheen.GradientOrigin=new Point(.22,.5);
        Update();
    }
    internal void RefreshLabels(){shortcutChip.Content=owner.PaletteLabel;shortcutChip.ToolTip="Commande rapide · "+owner.PaletteLabel;System.Windows.Automation.AutomationProperties.SetName(shortcutChip,shortcutChip.ToolTip.ToString());}
    Button Chip(string label,string name,string glyph,Action action){var button=UI.IconButton(glyph,name,action);button.Content=label;button.Width=double.NaN;button.Height=34;button.Padding=new Thickness(13,0,13,0);button.FontSize=12;button.Margin=new Thickness(8,0,0,0);button.BorderBrush=UI.Brush("#26FFFFFF");return button;}
    internal void Update(){
        var now=DateTime.Now;
        hour.Text=now.ToString("HH:mm",CultureInfo.InvariantCulture);seconds.Text=now.ToString("ss",CultureInfo.InvariantCulture);
        string label=now.ToString("dddd d MMMM",CultureInfo.GetCultureInfo("fr-FR"));day.Text=char.ToUpper(label[0],CultureInfo.GetCultureInfo("fr-FR"))+label[1..];
        greeting.Text=Greeting(now);
    }
    internal static string Greeting(DateTime at)=>at.Hour switch{<6=>"Bonne nuit, dj",<12=>"Bonjour, dj",<18=>"Bel après-midi, dj",_=>"Bonsoir, dj"};
}
