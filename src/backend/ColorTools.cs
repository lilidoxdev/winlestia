// language: C# 12, file: ColorTools.cs, target: Windows 11 / .NET 8 WPF, colour loupe and pixel ruler
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Brushes=System.Windows.Media.Brushes;

namespace PanneauxGlass;
internal static class ColorTools {
    static ColorPickerWindow? picker;static RulerOverlay? ruler;
    internal static void OpenPicker(Coordinator owner){Store.Log("Pipette: ouverture demandée");if(picker!=null){picker.Activate();return;}picker=new ColorPickerWindow(owner);picker.Closed+=(_,_)=>picker=null;picker.Show();Native.Raise(picker);Store.Log("Pipette: fenêtre affichée");}
    internal static void OpenRuler(Coordinator owner){if(ruler!=null){ruler.Activate();return;}ruler=new RulerOverlay(owner);ruler.Closed+=(_,_)=>ruler=null;ruler.Show();Native.Raise(ruler);}
}
internal sealed class ColorPickerWindow:Window {
    readonly System.Windows.Controls.Image preview=new(){Width=188,Height=188,Stretch=Stretch.UniformToFill};
    readonly TextBlock hex=UI.Text("",20,null,true),rgb=UI.Text("",11,UI.Dim),hint=UI.Text("Clic : copier · clic droit : fermer",10,UI.Dim);
    readonly StackPanel swatches=new(){Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(90)};
    readonly Coordinator owner;System.Windows.Media.Color current=Colors.White;bool sampling=true;IntPtr hook;Native.LowLevelMouseProc? procedure;
    internal ColorPickerWindow(Coordinator coordinator){
        owner=coordinator;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;Width=224;Height=376;WindowStartupLocation=WindowStartupLocation.Manual;
        var card=new Border{CornerRadius=new CornerRadius(22),Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(246,20,27,38)),BorderBrush=new SolidColorBrush(System.Windows.Media.Color.FromArgb(96,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(12),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=30,ShadowDepth=8,Opacity=.5}};
        var stack=new StackPanel();
        // An always-visible way out, on top of the global click that already picks the colour.
        var head=new DockPanel{Margin=new Thickness(2,0,2,9)};var heading=UI.Text("Pipette",12,UI.White,true);heading.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(heading);
        var shut=UI.IconButton("\uE711","Fermer la pipette",Close);shut.Width=22;shut.Height=22;shut.Padding=new Thickness(0);shut.Background=Brushes.Transparent;shut.BorderBrush=Brushes.Transparent;DockPanel.SetDock(shut,Dock.Right);head.Children.Add(shut);stack.Children.Add(head);
        var frame=new Border{CornerRadius=new CornerRadius(14),Clip=new RectangleGeometry(new Rect(0,0,188,188),14,14),Child=new Grid()};
        RenderOptions.SetBitmapScalingMode(preview,BitmapScalingMode.NearestNeighbor);
        ((Grid)frame.Child).Children.Add(preview);
        var cross=new Grid{IsHitTestVisible=false};
        cross.Children.Add(new Border{Width=1,Height=188,Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(120,255,255,255)),HorizontalAlignment=HorizontalAlignment.Center});
        cross.Children.Add(new Border{Height=1,Width=188,Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(120,255,255,255)),VerticalAlignment=VerticalAlignment.Center});
        ((Grid)frame.Child).Children.Add(cross);
        stack.Children.Add(frame);
        var line=new DockPanel{Margin=new Thickness(2,11,2,0)};var dot=new Border{Width=20,Height=20,CornerRadius=new CornerRadius(6),Background=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};dot.Name="swatch";line.Children.Add(dot);
        var texts=new StackPanel();hex.Text="#FFFFFF";texts.Children.Add(hex);rgb.Margin=new Thickness(0,2,0,0);texts.Children.Add(rgb);line.Children.Add(texts);stack.Children.Add(line);
        stack.Children.Add(swatches);hint.Margin=new Thickness(2,9,0,0);stack.Children.Add(hint);
        card.Child=stack;Content=card;
        var history=Store.State.ColorHistory.ToList();foreach(var value in history.Take(6))swatches.Children.Add(Swatch(value));
        MouseLeftButtonUp+=(_,e)=>{if(e.OriginalSource is Border border&&border.Name=="swatch"&&border.Tag is string saved)Copy(saved);else Copy(Hex(current));};
        MouseRightButtonUp+=(_,_)=>Close();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();if(e.Key==Key.C&&Keyboard.Modifiers==ModifierKeys.Control)Copy(Hex(current));};
        timer.Tick+=(_,_)=>Sample();
        Loaded+=(_,_)=>{Sample();timer.Start();Install();Activate();Focus();};
        Closed+=(_,_)=>{timer.Stop();Remove();};
    }
    Border Swatch(string value){
        var color=Parse(value);var dot=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(7),Margin=new Thickness(0,0,6,0),Background=new SolidColorBrush(color),BorderBrush=new SolidColorBrush(System.Windows.Media.Color.FromArgb(90,255,255,255)),BorderThickness=new Thickness(1),Tag=value,Cursor=Cursors.Hand,ToolTip=value};
        dot.MouseLeftButtonUp+=(_,e)=>{Copy(value);e.Handled=true;};return dot;
    }
    void Sample(){
        if(!sampling)return;
        try{
            Native.GetCursorPos(out var point);
            using var bitmap=new Bitmap(25,25,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(point.X-12,point.Y-12,0,0,new System.Drawing.Size(25,25),CopyPixelOperation.SourceCopy);
            current=System.Windows.Media.Color.FromRgb(bitmap.GetPixel(12,12).R,bitmap.GetPixel(12,12).G,bitmap.GetPixel(12,12).B);
            IntPtr handle=bitmap.GetHbitmap();
            try{var source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());preview.Source=source;}
            finally{Native.DeleteObject(handle);}
            hex.Text=Hex(current);rgb.Text="rgb("+current.R+", "+current.G+", "+current.B+")";
            if(Content is Border card&&card.Child is StackPanel stack&&stack.Children.OfType<DockPanel>().FirstOrDefault() is DockPanel line&&line.Children[0] is Border swatch)swatch.Background=new SolidColorBrush(current);
            double scale=VisualTreeHelper.GetDpi(this).DpiScaleX;
            // It follows the pointer, but freezes the moment the pointer is on it: otherwise the loupe
            // could never be clicked, and the only way out would be closing the whole application.
            bool over=point.X>=Left*scale&&point.Y>=Top*scale&&point.X<(Left+Width)*scale&&point.Y<(Top+Height)*scale;
            if(!over){
                var work=Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y)).WorkingArea;
                double left=point.X/scale+26,top=point.Y/scale+18;
                if(left+Width>work.Right/scale-6)left=point.X/scale-Width-26;
                if(top+Height>work.Bottom/scale-6)top=work.Bottom/scale-Height-10;
                Left=left;Top=top;
            }
        }catch(Exception ex){Store.Log("Pipette: "+ex.Message);}
    }
    // A global, silent observer: the next left click anywhere picks the colour and closes, the next
    // right click cancels. The click is swallowed so it never triggers the window underneath.
    void Install(){
        procedure=OnMouse;hook=Native.SetWindowsHookExW(14,procedure,Native.GetModuleHandleW(null),0);
        if(hook==IntPtr.Zero)Store.Log("Pipette: crochet souris indisponible");
    }
    void Remove(){if(hook==IntPtr.Zero)return;Native.UnhookWindowsHookEx(hook);hook=IntPtr.Zero;procedure=null;}
    IntPtr OnMouse(int code,IntPtr message,IntPtr data){
        if(code>=0&&sampling&&(!Native.GetCursorPos(out var point)||!Inside(point))){
            int id=message.ToInt32();
            if(id==0x0201){Dispatcher.BeginInvoke((Action)(()=>Copy(Hex(current))));return new IntPtr(1);}
            if(id==0x0204){Dispatcher.BeginInvoke((Action)Close);return new IntPtr(1);}
        }
        return Native.CallNextHookEx(hook,code,message,data);
    }
    bool Inside(Native.Point point){
        var dpi=VisualTreeHelper.GetDpi(this);
        return point.X>=Left*dpi.DpiScaleX&&point.Y>=Top*dpi.DpiScaleY&&point.X<(Left+Width)*dpi.DpiScaleX&&point.Y<(Top+Height)*dpi.DpiScaleY;
    }
    static string Hex(System.Windows.Media.Color color)=>"#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2");
    static System.Windows.Media.Color Parse(string value){try{return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);}catch{return Colors.White;}}
    void Copy(string value){
        sampling=false;timer.Stop();
        try{System.Windows.Clipboard.SetText(value);}catch{}
        Store.State.ColorHistory.RemoveAll(c=>string.Equals(c,value,StringComparison.OrdinalIgnoreCase));
        Store.State.ColorHistory.Insert(0,value);Store.State.ColorHistory=Store.State.ColorHistory.Take(24).ToList();Store.Save();
        owner.ShowToast(value+" copié");Close();
    }
}
internal sealed class RulerOverlay:Window {
    readonly Canvas canvas=new();readonly TextBlock label=UI.Text("",12,UI.White);readonly System.Windows.Shapes.Line line=new(){Stroke=new SolidColorBrush(System.Windows.Media.Color.FromRgb(255,226,120)),StrokeThickness=2};
    readonly TextBlock hint=UI.Text("Clique un premier point · Échap pour fermer",12,UI.White);
    bool started,finished;System.Windows.Point origin,corner;double scaleX=1,scaleY=1;
    internal RulerOverlay(Coordinator coordinator){
        _=coordinator;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(26,4,8,14));ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.Manual;Cursor=Cursors.Cross;
        Left=SystemParameters.VirtualScreenLeft;Top=SystemParameters.VirtualScreenTop;Width=SystemParameters.VirtualScreenWidth;Height=SystemParameters.VirtualScreenHeight;
        Content=canvas;canvas.Children.Add(line);
        var box=new Border{CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(220,18,26,36)),Padding=new Thickness(8,5,8,5),Child=label,Visibility=Visibility.Collapsed};box.Name="box";canvas.Children.Add(box);
        hint.Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(200,18,26,36));hint.Padding=new Thickness(12,7,12,7);hint.IsHitTestVisible=false;Canvas.SetLeft(hint,20);Canvas.SetTop(hint,20);canvas.Children.Add(hint);
        MouseLeftButtonDown+=(_,e)=>{var point=e.GetPosition(this);if(!started||finished){started=true;finished=false;origin=corner=point;hint.Text="Clique le second point · clic droit pour recommencer";}else{finished=true;corner=point;hint.Text="Mesure terminée · Échap pour fermer";}Update(point);};
        MouseMove+=(_,e)=>{if(started&&!finished)Update(e.GetPosition(this));};
        MouseRightButtonDown+=(_,_)=>{started=finished=false;line.Visibility=Visibility.Collapsed;hint.Text="Clique un premier point · Échap pour fermer";var box=canvas.Children.OfType<Border>().FirstOrDefault(b=>b.Name=="box");if(box!=null)box.Visibility=Visibility.Collapsed;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
        Loaded+=(_,_)=>{var dpi=VisualTreeHelper.GetDpi(this);scaleX=dpi.DpiScaleX;scaleY=dpi.DpiScaleY;};
    }
    void Update(System.Windows.Point current){
        corner=current;line.X1=origin.X;line.Y1=origin.Y;line.X2=corner.X;line.Y2=corner.Y;line.Visibility=Visibility.Visible;
        double width=Math.Abs(corner.X-origin.X)*scaleX,height=Math.Abs(corner.Y-origin.Y)*scaleY,distance=Math.Sqrt(width*width+height*height);
        double diagonal=Math.Sqrt(width*width+height*height)/96d*2.54;
        label.Text=(int)width+" × "+(int)height+" px   ·   "+Math.Round(diagonal,1)+" cm";
        var box=canvas.Children.OfType<Border>().FirstOrDefault(b=>b.Name=="box");if(box==null)return;box.Visibility=Visibility.Visible;
        box.Measure(new System.Windows.Size(600,60));
        double left=Math.Max(6,corner.X+14),top=Math.Max(6,corner.Y+14);
        if(left+box.DesiredSize.Width>Width-8)left=corner.X-box.DesiredSize.Width-14;
        if(top+box.DesiredSize.Height>Height-8)top=corner.Y-box.DesiredSize.Height-14;
        Canvas.SetLeft(box,left);Canvas.SetTop(box,top);
    }
}
