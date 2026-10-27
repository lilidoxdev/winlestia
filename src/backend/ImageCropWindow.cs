// language: C# 12, file: ImageCropWindow.cs, target: Windows 11 / .NET 8 WPF, non-destructive square PNG cropping with pan and zoom
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PanneauxGlass;
internal sealed class CropViewport:FrameworkElement {
    readonly BitmapSource image;readonly double basis;double zoom=1,x,y;Point dragStart;double dragX,dragY;bool dragging;
    const double Size=320;
    internal event Action<double>? ZoomChanged;
    internal double Zoom=>zoom;
    internal CropViewport(BitmapSource source,LogoCrop? crop){image=source;Width=Height=Size;ClipToBounds=true;Focusable=true;Cursor=Cursors.SizeAll;basis=Math.Max(Size/source.PixelWidth,Size/source.PixelHeight);if(crop==null||crop.Centered)Fit();else{zoom=double.IsFinite(crop.Zoom)?Math.Clamp(crop.Zoom,.1,6):1;x=double.IsFinite(crop.X)?crop.X*Size:0;y=double.IsFinite(crop.Y)?crop.Y*Size:0;}}
    internal void SetZoom(double value){double next=Math.Clamp(value,.1,6),ratio=next/zoom;x=Size/2+(x-Size/2)*ratio;y=Size/2+(y-Size/2)*ratio;zoom=next;InvalidateVisual();ZoomChanged?.Invoke(zoom);}
    internal void Fit(){zoom=Math.Min(Size/image.PixelWidth,Size/image.PixelHeight)/basis;Center();ZoomChanged?.Invoke(zoom);}
    internal void Fill(){zoom=1;Center();ZoomChanged?.Invoke(zoom);}
    internal void Center(){x=(Size-image.PixelWidth*basis*zoom)/2;y=(Size-image.PixelHeight*basis*zoom)/2;InvalidateVisual();}
    internal LogoCrop State()=>new(){Zoom=zoom,X=x/Size,Y=y/Size,Centered=false};
    protected override void OnRender(DrawingContext drawing){for(int row=0;row<16;row++)for(int col=0;col<16;col++)drawing.DrawRectangle(UI.Brush((row+col)%2==0?"#BB31495B":"#BB203747"),null,new Rect(col*20,row*20,20,20));DrawImage(drawing,1);drawing.DrawRectangle(null,new Pen(UI.Brush("#90E3F4FF"),1),new Rect(.5,.5,319,319));}
    void DrawImage(DrawingContext drawing,double scale){drawing.DrawImage(image,new Rect(x*scale,y*scale,image.PixelWidth*basis*zoom*scale,image.PixelHeight*basis*zoom*scale));}
    internal BitmapSource Export(){var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())DrawImage(drawing,256d/Size);var result=new RenderTargetBitmap(256,256,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;}
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();dragging=true;dragStart=e.GetPosition(this);dragX=x;dragY=y;CaptureMouse();e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;var point=e.GetPosition(this);x=dragX+point.X-dragStart.X;y=dragY+point.Y-dragStart.Y;InvalidateVisual();}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){dragging=false;ReleaseMouseCapture();base.OnMouseLeftButtonUp(e);}
    protected override void OnLostMouseCapture(MouseEventArgs e){dragging=false;base.OnLostMouseCapture(e);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){SetZoom(zoom*Math.Pow(1.12,e.Delta/120d));e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e){double step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?10:1;switch(e.Key){case Key.Left:x-=step;break;case Key.Right:x+=step;break;case Key.Up:y-=step;break;case Key.Down:y+=step;break;default:base.OnKeyDown(e);return;}InvalidateVisual();e.Handled=true;}
}
internal sealed class ImageCropWindow:GlassWindow {
    internal BitmapSource? Output;internal LogoCrop? Crop;readonly CropViewport viewport;
    internal ImageCropWindow(string path,LogoCrop? original):base("Panneaux Glass — recadrer l’image"){
        Width=650;Height=750;ShowActivated=true;WindowStartupLocation=WindowStartupLocation.CenterOwner;ContentHost.Margin=new Thickness(28,24,28,24);var form=new StackPanel();ContentHost.Children.Add(form);
        var title=new DockPanel();var close=UI.IconButton("\uE711","Annuler le recadrage",Close);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(UI.Text("Ton logo, ton cadrage.",25,null,true));form.Children.Add(title);
        var hint=UI.Text("Glisse l’image pour la déplacer. Molette ou curseur pour zoomer.",13,UI.Dim);hint.Margin=new Thickness(0,10,0,19);form.Children.Add(hint);
        viewport=new CropViewport(LoadImage(path),original);var frame=new Border{Child=viewport,CornerRadius=new CornerRadius(16),HorizontalAlignment=HorizontalAlignment.Center};form.Children.Add(frame);
        var zoomRow=new Grid{Margin=new Thickness(4,18,4,12)};zoomRow.ColumnDefinitions.Add(new(){Width=new GridLength(62)});zoomRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});zoomRow.ColumnDefinitions.Add(new(){Width=new GridLength(65)});zoomRow.Children.Add(UI.Text("Zoom",13,UI.Dim));var slider=new Slider{Minimum=.1,Maximum=6,Value=viewport.Zoom,SmallChange=.05,Margin=new Thickness(8,0,12,0)};System.Windows.Automation.AutomationProperties.SetName(slider,"Zoom de l’image");Grid.SetColumn(slider,1);zoomRow.Children.Add(slider);var value=UI.Text("",12,UI.Dim);value.TextAlignment=TextAlignment.Right;Grid.SetColumn(value,2);zoomRow.Children.Add(value);void Update(double z){slider.Value=z;value.Text=Math.Round(z*100)+" %";}slider.ValueChanged+=(_,e)=>viewport.SetZoom(e.NewValue);viewport.ZoomChanged+=Update;Update(viewport.Zoom);form.Children.Add(zoomRow);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};var fit=UI.Button("Image entière",viewport.Fit);fit.Margin=new Thickness(0,0,8,0);tools.Children.Add(fit);var fill=UI.Button("Remplir",viewport.Fill);fill.Margin=new Thickness(0,0,8,0);tools.Children.Add(fill);tools.Children.Add(UI.Button("Centrer",viewport.Center));form.Children.Add(tools);
        var note=UI.Text("PNG 256 × 256 · le damier reste transparent dans l’image enregistrée.",12,UI.Dim);note.Margin=new Thickness(0,18,0,18);note.TextAlignment=TextAlignment.Center;form.Children.Add(note);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(UI.Button("Annuler",Close));var save=UI.Button("Utiliser ce cadrage",()=>{Output=viewport.Export();Crop=viewport.State();DialogResult=true;});save.Margin=new Thickness(12,0,0,0);save.IsDefault=true;actions.Children.Add(save);form.Children.Add(actions);KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    internal static BitmapSource LoadImage(string path){int orientation=1,pixelWidth=2048;try{using var stream=File.OpenRead(path);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);pixelWidth=decoder.Frames[0].PixelWidth;if(decoder.Frames[0].Metadata is BitmapMetadata metadata&&metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort rotation)orientation=rotation;}catch{}
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.DecodePixelWidth=Math.Min(2048,pixelWidth);image.EndInit();image.Freeze();BitmapSource source=image;var transforms=new TransformGroup();if(orientation is 3 or 4)transforms.Children.Add(new RotateTransform(180));if(orientation is 5 or 6)transforms.Children.Add(new RotateTransform(90));if(orientation is 7 or 8)transforms.Children.Add(new RotateTransform(270));if(orientation is 2 or 4 or 5 or 7)transforms.Children.Add(new ScaleTransform(-1,1));if(transforms.Children.Count>0){source=new TransformedBitmap(source,transforms);source.Freeze();}return source;
    }
}
