// language: C# 12, file: PanelLayout.cs, target: Windows 11 / .NET 8 WPF
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
public sealed class PanelPlacement {
    public double X {get;set;} public double Y {get;set;} public double Width {get;set;} public double Height {get;set;}
    internal bool Valid=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Width)&&double.IsFinite(Height)&&Width>0&&Height>0;
    internal static PanelPlacement Capture(Rect r,Rect bounds)=>new(){X=(r.X-bounds.X)/bounds.Width,Y=(r.Y-bounds.Y)/bounds.Height,Width=r.Width/bounds.Width,Height=r.Height/bounds.Height};
    internal Rect Resolve(Rect bounds)=>PanelLayout.Clamp(new Rect(bounds.X+X*bounds.Width,bounds.Y+Y*bounds.Height,Width*bounds.Width,Height*bounds.Height),bounds);
}
internal static class PanelLayout {
    internal static Rect Clamp(Rect r,Rect bounds){double w=Math.Clamp(r.Width,Math.Min(480,bounds.Width),bounds.Width),h=Math.Clamp(r.Height,Math.Min(310,bounds.Height),bounds.Height);return new Rect(Math.Clamp(r.X,bounds.Left,bounds.Right-w),Math.Clamp(r.Y,bounds.Top,bounds.Bottom-h),w,h);}
    internal static Rect Drag(Rect original,Rect bounds,double dx,double dy,string edge){
        if(edge=="move")return Clamp(new Rect(original.X+dx,original.Y+dy,original.Width,original.Height),bounds);
        double minW=Math.Min(480,bounds.Width),minH=Math.Min(310,bounds.Height),l=original.Left,t=original.Top,r=original.Right,b=original.Bottom;
        if(edge.Contains('w'))l=Math.Clamp(l+dx,bounds.Left,r-minW);if(edge.Contains('e'))r=Math.Clamp(r+dx,l+minW,bounds.Right);
        if(edge.Contains('n'))t=Math.Clamp(t+dy,bounds.Top,b-minH);if(edge.Contains('s'))b=Math.Clamp(b+dy,t+minH,bounds.Bottom);
        return new Rect(l,t,r-l,b-t);
    }
}
internal sealed class PanelInteraction {
    readonly BoardWindow window;readonly Coordinator owner;Rect initial;Native.Point cursor;double scale;
    internal PanelInteraction(BoardWindow panel,Coordinator coordinator,Panel header){window=panel;owner=coordinator;
        var move=Make("move",Cursors.SizeAll);move.ToolTip="Glisser pour déplacer ce panneau";header.Children.Add(move);
        foreach(string edge in new[]{"n","s","e","w","nw","ne","sw","se"}){
            var thumb=Make(edge,edge is "n" or "s"?Cursors.SizeNS:edge is "e" or "w"?Cursors.SizeWE:edge is "nw" or "se"?Cursors.SizeNWSE:Cursors.SizeNESW);
            thumb.HorizontalAlignment=edge.Contains('w')?HorizontalAlignment.Left:edge.Contains('e')?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
            thumb.VerticalAlignment=edge.Contains('n')?VerticalAlignment.Top:edge.Contains('s')?VerticalAlignment.Bottom:VerticalAlignment.Stretch;
            if(edge.Length==2){thumb.Width=24;thumb.Height=24;}else if(edge is "e" or "w"){thumb.Width=9;thumb.Margin=new Thickness(0,28,0,28);}else{thumb.Height=9;thumb.Margin=new Thickness(28,0,28,0);}
            window.Surface.Children.Add(thumb);
        }
        var grip=UI.Glyph("\uE784",13);grip.Opacity=.38;grip.IsHitTestVisible=false;grip.HorizontalAlignment=HorizontalAlignment.Right;grip.VerticalAlignment=VerticalAlignment.Bottom;grip.Margin=new Thickness(0,0,11,9);window.Surface.Children.Add(grip);
    }
    Thumb Make(string edge,Cursor pointer){var t=new Thumb{Cursor=pointer,Background=Brushes.Transparent,Focusable=false};var factory=new FrameworkElementFactory(typeof(Border));factory.SetValue(Border.BackgroundProperty,Brushes.Transparent);t.Template=new ControlTemplate(typeof(Thumb)){VisualTree=factory};System.Windows.Automation.AutomationProperties.SetName(t,edge=="move"?"Déplacer "+window.Model.Title:"Redimensionner "+window.Model.Title+" "+edge);
        t.DragStarted+=(_,_)=>{owner.BeginLayoutInteraction();initial=new Rect(window.Left,window.Top,window.Width,window.Height);Native.GetCursorPos(out cursor);scale=VisualTreeHelper.GetDpi(window).DpiScaleX;window.ContentHost.CacheMode=null;};
        t.DragDelta+=(_,_)=>{Native.GetCursorPos(out var now);var rect=PanelLayout.Drag(initial,owner.PanelBounds,(now.X-cursor.X)/scale,(now.Y-cursor.Y)/scale,edge);window.Left=rect.X;window.Top=rect.Y;window.Width=rect.Width;window.Height=rect.Height;};
        t.DragCompleted+=(_,e)=>{if(e.Canceled){window.Left=initial.X;window.Top=initial.Y;window.Width=initial.Width;window.Height=initial.Height;}else window.Model.Placement=PanelPlacement.Capture(new Rect(window.Left,window.Top,window.Width,window.Height),owner.PanelBounds);window.ContentHost.CacheMode=new BitmapCache{EnableClearType=true,SnapsToDevicePixels=true};owner.EndLayoutInteraction();};return t;
    }
}
