// language: C# 12, file: CaptureTools.cs, target: Windows 11 / .NET 8 WPF, capture, annotation, pinned paper, OCR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using BitmapFrame=System.Windows.Media.Imaging.BitmapFrame;
using WinBitmapDecoder=Windows.Graphics.Imaging.BitmapDecoder;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PanneauxGlass;
internal static class ScreenShot {
    internal static System.Drawing.Rectangle VirtualBounds()=>System.Windows.Forms.SystemInformation.VirtualScreen;
    internal static BitmapSource Capture(System.Drawing.Rectangle bounds){
        using var bitmap=new System.Drawing.Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bitmap.Size,System.Drawing.CopyPixelOperation.SourceCopy);
        IntPtr handle=bitmap.GetHbitmap();
        try{var source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();return source;}
        finally{Native.DeleteObject(handle);}
    }
    internal static byte[] Png(BitmapSource source){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}
    internal static void Save(BitmapSource image){
        var dialog=new SaveFileDialog{Title="Enregistrer la capture",Filter="Image PNG|*.png",FileName="capture-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".png"};
        if(dialog.ShowDialog()!=true)return;
        using var stream=File.Create(dialog.FileName);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);
    }
}
// Freeze the screen, let dj frame a region, then annotate it on the pixels themselves.
internal sealed class CaptureOverlay:Window {
    static CaptureOverlay? current;
    readonly Coordinator owner;readonly BitmapSource screen;readonly double scaleX,scaleY;readonly bool textOnly;
    readonly Grid root=new();readonly Image background;readonly System.Windows.Shapes.Path shade=new();readonly Border frame=new();readonly TextBlock sizeLabel;
    InkCanvas? ink;Border? tools;bool drawing,annotating;Point origin;Rect selection=new();
    internal static void Begin(Coordinator owner,bool textOnly=false){
        if(current!=null){current.Activate();return;}
        try{current=new CaptureOverlay(owner,textOnly);current.Show();Native.Raise(current);}catch(Exception ex){current=null;owner.Message("La capture n’a pas pu démarrer.\n"+ex.Message);}
    }
    CaptureOverlay(Coordinator coordinator,bool textOnly){
        owner=coordinator;this.textOnly=textOnly;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;Background=Brushes.Black;WindowStartupLocation=WindowStartupLocation.Manual;
        Left=SystemParameters.VirtualScreenLeft;Top=SystemParameters.VirtualScreenTop;Width=SystemParameters.VirtualScreenWidth;Height=SystemParameters.VirtualScreenHeight;
        var bounds=ScreenShot.VirtualBounds();screen=ScreenShot.Capture(bounds);
        scaleX=screen.PixelWidth/Math.Max(1,Width);scaleY=screen.PixelHeight/Math.Max(1,Height);
        Content=root;root.Children.Add(background=new Image{Source=screen,Stretch=Stretch.Fill});
        shade.Fill=new SolidColorBrush(Color.FromArgb(120,4,8,14));shade.IsHitTestVisible=false;root.Children.Add(shade);
        frame.BorderBrush=new SolidColorBrush(Color.FromArgb(235,255,255,255));frame.BorderThickness=new Thickness(1);frame.Background=Brushes.Transparent;frame.IsHitTestVisible=false;root.Children.Add(frame);
        sizeLabel=UI.Text("",12,UI.White);sizeLabel.Background=new SolidColorBrush(Color.FromArgb(190,15,22,32));sizeLabel.Padding=new Thickness(8,3,8,3);sizeLabel.IsHitTestVisible=false;sizeLabel.Visibility=Visibility.Collapsed;root.Children.Add(sizeLabel);
        var hint=UI.Text(textOnly?"Encadre le texte à lire · Échap pour annuler":"Glisse pour encadrer · Échap pour annuler",12,new SolidColorBrush(Color.FromArgb(210,235,245,255)));
        hint.HorizontalAlignment=HorizontalAlignment.Center;hint.VerticalAlignment=VerticalAlignment.Top;hint.Margin=new Thickness(0,26,0,0);hint.IsHitTestVisible=false;hint.Effect=new DropShadowEffect{BlurRadius=18,ShadowDepth=0,Opacity=.7};root.Children.Add(hint);
        root.MouseLeftButtonDown+=(_,e)=>{if(annotating)return;origin=e.GetPosition(root);drawing=true;selection=new Rect(origin,new Size(0,0));DrawShade();};
        root.MouseMove+=(_,e)=>{if(!drawing)return;var point=e.GetPosition(root);selection=new Rect(new Point(Math.Min(origin.X,point.X),Math.Min(origin.Y,point.Y)),new Point(Math.Max(origin.X,point.X),Math.Max(origin.Y,point.Y)));DrawShade();};
        root.MouseLeftButtonUp+=(_,_)=>{if(!drawing)return;drawing=false;if(selection.Width<10||selection.Height<10){DrawShade();return;}
            // Text mode skips the pen entirely: frame it, read it, done.
            if(textOnly){var image=Render();Close();_ = OcrService.ReadAsync(owner,image);return;}
            StartAnnotating();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}else if(e.Key==Key.Enter){Copy();e.Handled=true;}else if(e.Key==Key.C&&Keyboard.Modifiers==ModifierKeys.Control){Copy();e.Handled=true;}else if(e.Key==Key.S&&Keyboard.Modifiers==ModifierKeys.Control){Save();e.Handled=true;}};
        Closed+=(_,_)=>{if(ReferenceEquals(current,this))current=null;};
        Loaded+=(_,_)=>{DrawShade();Activate();};
    }
    void DrawShade(){
        if(selection.Width<1||selection.Height<1){shade.Data=Geometry.Empty;frame.Visibility=Visibility.Collapsed;sizeLabel.Visibility=Visibility.Collapsed;return;}
        var all=new RectangleGeometry(new Rect(0,0,root.ActualWidth,root.ActualHeight));
        var hole=new RectangleGeometry(selection,3,3);
        shade.Data=new CombinedGeometry(GeometryCombineMode.Exclude,all,hole);
        frame.Margin=new Thickness(selection.X,selection.Y,0,0);frame.Width=selection.Width;frame.Height=selection.Height;frame.HorizontalAlignment=HorizontalAlignment.Left;frame.VerticalAlignment=VerticalAlignment.Top;frame.Visibility=Visibility.Visible;
        sizeLabel.Text=(int)(selection.Width*scaleX)+" × "+(int)(selection.Height*scaleY);
        sizeLabel.Visibility=Visibility.Visible;sizeLabel.Measure(new Size(400,40));
        sizeLabel.Margin=new Thickness(Math.Max(4,selection.X),Math.Max(4,selection.Y-sizeLabel.DesiredSize.Height-6),0,0);
        sizeLabel.HorizontalAlignment=HorizontalAlignment.Left;sizeLabel.VerticalAlignment=VerticalAlignment.Top;
    }
    void StartAnnotating(){
        annotating=true;
        ink=new InkCanvas{Width=selection.Width,Height=selection.Height,Background=null,EditingMode=InkCanvasEditingMode.Ink,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(selection.X,selection.Y,0,0),Cursor=Cursors.Pen};
        ink.DefaultDrawingAttributes=new DrawingAttributes{Color=Color.FromRgb(255,86,86),Width=3,Height=3,FitToCurve=true,StylusTip=StylusTip.Ellipse};
        root.Children.Add(ink);
        tools=BuildToolbar();
        root.Children.Add(tools);
        PositionToolbar();
        systemBar();
    }
    void systemBar(){
        // InkCanvas swallows the mouse inside the selection: the annotation bar is the only escape hatch.
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Z&&Keyboard.Modifiers==ModifierKeys.Control)Undo();};
    }
    Border BuildToolbar(){
        var bar=new Border{CornerRadius=new CornerRadius(20),Background=new SolidColorBrush(Color.FromArgb(242,20,28,40)),BorderBrush=new SolidColorBrush(Color.FromArgb(90,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(6),Effect=new DropShadowEffect{BlurRadius=24,ShadowDepth=2,Opacity=.55}};
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        Button Tool(string glyph,string label,Action action){var button=UI.IconButton(glyph,label,action);button.Width=32;button.Height=32;button.Padding=new Thickness(0);button.Margin=new Thickness(1,0,1,0);button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;return button;}
        void Mode(InkCanvasEditingMode mode,DrawingAttributes attributes,Button button){
            ink!.EditingMode=mode;if(attributes!=null)ink.DefaultDrawingAttributes=attributes;
            foreach(var child in row.Children.OfType<Button>())child.Background=Brushes.Transparent;
            button.Background=new SolidColorBrush(Color.FromArgb(64,255,255,255));
        }
        var pen=Tool("\uE70F","Stylo",()=>{ });
        var highlight=Tool("\uE7E6","Surligneur",()=>{ });
        var eraser=Tool("\uE75C","Gomme",()=>{ });
        pen.Click+=(_,_)=>Mode(InkCanvasEditingMode.Ink,new DrawingAttributes{Color=Color.FromRgb(255,86,86),Width=3,Height=3,FitToCurve=true,StylusTip=StylusTip.Ellipse},pen);
        highlight.Click+=(_,_)=>Mode(InkCanvasEditingMode.Ink,new DrawingAttributes{Color=Color.FromArgb(150,255,222,92),Width=14,Height=14,FitToCurve=true,StylusTip=StylusTip.Rectangle,IsHighlighter=true},highlight);
        eraser.Click+=(_,_)=>Mode(InkCanvasEditingMode.EraseByStroke,null,eraser);
        row.Children.Add(pen);row.Children.Add(highlight);row.Children.Add(eraser);
        row.Children.Add(new Border{Width=1,Height=20,Background=new SolidColorBrush(Color.FromArgb(48,255,255,255)),Margin=new Thickness(5,6,5,6)});
        row.Children.Add(Tool("\uE7A7","Annuler",Undo));
        row.Children.Add(Tool("\uE8A5","Copier le texte de la zone",ExtractText));
        row.Children.Add(new Border{Width=1,Height=20,Background=new SolidColorBrush(Color.FromArgb(48,255,255,255)),Margin=new Thickness(5,6,5,6)});
        row.Children.Add(Tool("\uE840","Épingler sur l’écran",Pin));
        row.Children.Add(Tool("\uE74E","Enregistrer en PNG",Save));
        var copy=Tool("\uE8C8","Copier",Copy);copy.Background=new SolidColorBrush(Color.FromArgb(70,255,255,255));
        row.Children.Add(copy);
        row.Children.Add(Tool("\uE711","Fermer",Close));
        bar.Child=row;return bar;
    }
    void PositionToolbar(){
        if(tools==null)return;tools.Measure(new Size(600,60));var size=tools.DesiredSize;
        double left=Math.Clamp(selection.X,8,Math.Max(8,Width-size.Width-8));
        double top=selection.Y-size.Height-10;if(top<8)top=Math.Min(Height-size.Height-8,selection.Bottom+10);
        tools.HorizontalAlignment=HorizontalAlignment.Left;tools.VerticalAlignment=VerticalAlignment.Top;tools.Margin=new Thickness(left,Math.Max(8,top),0,0);
    }
    void Undo(){if(ink!=null&&ink.Strokes.Count>0)ink.Strokes.RemoveAt(ink.Strokes.Count-1);}
    BitmapSource Render(){
        int x=(int)Math.Round(selection.X*scaleX),y=(int)Math.Round(selection.Y*scaleY),w=Math.Max(1,(int)Math.Round(selection.Width*scaleX)),h=Math.Max(1,(int)Math.Round(selection.Height*scaleY));
        var cropped=new CroppedBitmap(screen,new Int32Rect(Math.Clamp(x,0,screen.PixelWidth-1),Math.Clamp(y,0,screen.PixelHeight-1),Math.Min(w,screen.PixelWidth-x),Math.Min(h,screen.PixelHeight-y)));
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawImage(cropped,new Rect(0,0,cropped.PixelWidth,cropped.PixelHeight));if(ink!=null&&ink.Strokes.Count>0)dc.DrawRectangle(new VisualBrush(ink){Stretch=Stretch.Fill},null,new Rect(0,0,cropped.PixelWidth,cropped.PixelHeight));}
        var bitmap=new RenderTargetBitmap(cropped.PixelWidth,cropped.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    void Copy(){var image=Render();Close();try{System.Windows.Clipboard.SetImage(image);owner.ShowToast("Capture copiée");}catch(Exception ex){owner.Message("La capture n’a pas pu être copiée.\n"+ex.Message);}}
    void Pin(){var image=Render();Close();PinWindow.Open(owner,image);}
    void Save(){var image=Render();Close();ScreenShot.Save(image);owner.ShowToast("Capture enregistrée");}
    void ExtractText(){var image=Render();Close();_ = OcrService.ReadAsync(owner,image);}
}
// A sheet taped above every window: a capture or a memo. Every placed detail - text, size,
// position, transparency, angle - lives in Preferences and comes back at the next start.
internal sealed class PinWindow:Window {
    static readonly List<PinWindow> pins=new();
    readonly Coordinator owner;readonly PinNote note;readonly Border card;readonly RotateTransform rotation;readonly TranslateTransform drop=new();
    readonly TextBox? editor;readonly DispatcherTimer save=new(){Interval=TimeSpan.FromMilliseconds(450)};
    readonly bool isImage;double ratio;double baseAngle;bool placed;

    internal static void RestoreAll(Coordinator coordinator){
        if(System.Windows.Application.Current==null)return;
        int restored=0;foreach(var pinned in Store.State.Pins.ToArray()){
            try{
                BitmapSource? image=null;
                if(pinned.Kind=="image"){
                    if(!File.Exists(pinned.ImagePath)){Store.State.Pins.Remove(pinned);continue;}
                    image=Load(pinned.ImagePath);
                }
                Show(coordinator,pinned,image,false);restored++;
            }catch(Exception ex){Store.Log("Papier restauré: "+ex.Message);}
        }
        if(restored>0)Store.Log("Papiers restaurés · "+restored);
        Store.Save();
    }
    internal static void Open(Coordinator coordinator,BitmapSource image){
        if(System.Windows.Application.Current==null)return;
        try{
            string folder=Path.Combine(Store.Root,"pins");Directory.CreateDirectory(folder);
            var pinned=new PinNote{Kind="image",Width=Math.Min(420,image.PixelWidth*(96/Math.Max(1,image.DpiX))),Angle=-2.6};
            pinned.ImagePath=Path.Combine(folder,pinned.Id+".png");
            using(var stream=File.Create(pinned.ImagePath)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);}
            pinned.Height=pinned.Width*image.PixelHeight/Math.Max(1,image.PixelWidth);
            Store.State.Pins.Insert(0,pinned);Store.Save();
            Show(coordinator,pinned,image,true);
        }catch(Exception ex){coordinator.Message("Le papier n’a pas pu être épinglé.\n"+ex.Message);}
    }
    internal static void OpenMemo(Coordinator coordinator,string text=""){
        if(System.Windows.Application.Current==null)return;
        var pinned=new PinNote{Kind="memo",Text=text,Width=300,Angle=-2.4};
        Store.State.Pins.Insert(0,pinned);Store.Save();
        var window=Show(coordinator,pinned,null,true);window.editor?.Focus();
    }
    static PinWindow Show(Coordinator coordinator,PinNote pinned,BitmapSource? image,bool activate){
        var window=new PinWindow(coordinator,pinned,image);pins.Add(window);window.Closed+=(_,_)=>pins.Remove(window);window.Show();
        if(activate)Native.Raise(window);return window;
    }
    // A dialog takes precedence without un-pinning the paper: it goes back above once the dialog closes.
    internal static void Lower(bool lower){foreach(var window in pins)try{window.Topmost=!lower;}catch{}}
    // While the space is open the sheets float between the toolbar and the panels, dimmed and
    // transparent to input: still readable, but not one click of the interface is taken from you.
    internal static void SpaceOpen(bool open,IntPtr toolbar){foreach(var window in pins)try{window.ApplySpace(open,toolbar);}catch(Exception ex){Store.Log("Papier espace: "+ex.Message);}}
    // Activating a panel puts it above the sheets; this brings them back without taking focus.
    internal static void EnsureAbove(){foreach(var window in pins)try{Native.KeepAbove(new WindowInteropHelper(window).Handle);}catch{}}
    void ApplySpace(bool open,IntPtr toolbar){
        var handle=new WindowInteropHelper(this).EnsureHandle();
        Native.ClickThrough(handle,open);Topmost=true;
        BeginAnimation(OpacityProperty,new DoubleAnimation(Opacity,open?note.Opacity*.78:note.Opacity,Motion.Span(180)){EasingFunction=Motion.Ease});
    }
    internal static void CloseAll(){foreach(var window in pins.ToArray())window.Close();}
    static BitmapSource Load(string path){
        var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.UriSource=new Uri(path);source.EndInit();source.Freeze();return source;
    }
    PinWindow(Coordinator coordinator,PinNote pinned,BitmapSource? image){
        owner=coordinator;note=pinned;baseAngle=pinned.Angle;isImage=image!=null;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;ShowActivated=true;Opacity=Math.Clamp(pinned.Opacity,.35,1);
        double width=Math.Clamp(pinned.Width,160,1200);
        if(image!=null){
            ratio=image.PixelWidth/(double)Math.Max(1,image.PixelHeight);
            double maxWidth=SystemParameters.WorkArea.Width*.46,maxHeight=SystemParameters.WorkArea.Height*.62,height=width/ratio;
            if(height>maxHeight){height=maxHeight;width=height*ratio;}
            Width=width+16;Height=height+16;
        }else{SizeToContent=SizeToContent.Height;Width=width+16;MinHeight=120;}
        var root=new Grid();
        card=new Border{CornerRadius=new CornerRadius(9),Background=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromRgb(253,251,244),0),new GradientStop(Color.FromRgb(246,240,227),1)},new Point(.12,.05),new Point(.9,1)),BorderBrush=new SolidColorBrush(Color.FromArgb(90,150,116,86)),BorderThickness=new Thickness(1),Padding=new Thickness(8),Effect=new DropShadowEffect{BlurRadius=34,ShadowDepth=10,Direction=270,Opacity=.45},RenderTransformOrigin=new Point(.5,.5)};
        rotation=new RotateTransform(baseAngle-6.5);card.RenderTransform=new TransformGroup{Children={rotation,drop}};
        if(image!=null){
            var picture=new Image{Source=image,Stretch=Stretch.Fill,SnapsToDevicePixels=true};
            card.Child=new Border{Clip=new RectangleGeometry(new Rect(0,0,width,width/ratio),5,5),Child=picture};
        }else{
            editor=new TextBox{Text=pinned.Text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Segoe Print"),FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(48,45,42)),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(4,2,4,2),MinHeight=96,MaxHeight=480,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};
            System.Windows.Automation.AutomationProperties.SetName(editor,"Texte du papier");
            editor.TextChanged+=(_,_)=>{note.Text=editor.Text;Schedule();};
            card.Child=editor;
        }
        root.Children.Add(card);
        var tape=new Border{Width=Math.Min(132,width*.44),Height=20,Background=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(225,255,249,224),0),new GradientStop(Color.FromArgb(235,243,229,187),1)},new Point(0,0),new Point(1,1)),BorderBrush=new SolidColorBrush(Color.FromArgb(70,163,140,96)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,-9,0,0),Opacity=.95,Cursor=Cursors.SizeAll,RenderTransform=new RotateTransform(-3),ToolTip="Glisser par le ruban pour déplacer"};
        tape.MouseLeftButtonDown+=(_,_)=>{try{DragMove();}catch{}};
        root.Children.Add(tape);
        var close=UI.IconButton("\uE711","Détacher et supprimer ce papier",Detach);close.Width=24;close.Height=24;close.FontSize=9;close.HorizontalAlignment=HorizontalAlignment.Right;close.VerticalAlignment=VerticalAlignment.Top;close.Margin=new Thickness(0,-10,-10,0);close.Opacity=0;close.Background=new SolidColorBrush(Color.FromArgb(220,28,36,48));close.BorderBrush=Brushes.Transparent;
        root.Children.Add(close);
        card.MouseEnter+=(_,_)=>{close.Opacity=1;Animate(0);};
        card.MouseLeave+=(_,_)=>{close.Opacity=0;Animate(baseAngle);};
        if(image!=null)card.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is Button)return;try{DragMove();}catch{}};
        card.PreviewMouseWheel+=(_,e)=>Adjust(e);
        LocationChanged+=(_,_)=>{if(placed)Schedule();};
        save.Tick+=(_,_)=>Persist();
        Closed+=(_,_)=>Persist();
        Content=root;
        // The sheet lands: a few pixels of fall, then it settles into its angle, tape and all.
        Loaded+=(_,_)=>{
            Place();
            rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(baseAngle-6.5,baseAngle,Motion.Span(420)){EasingFunction=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=.45}});
            drop.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-18,0,Motion.Span(420)){EasingFunction=Motion.Ease});
            card.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,Motion.Span(180)));
            Schedule();
        };
    }
    void Place(){
        var area=SystemParameters.WorkArea;double width=Width,height=Height;
        double left=note.Left is double savedLeft&&double.IsFinite(savedLeft)?savedLeft:area.Left+area.Width*.5-width*.5;
        double top=note.Top is double savedTop&&double.IsFinite(savedTop)?savedTop:area.Top+area.Height*.28;
        Left=Math.Clamp(left,area.Left-20,Math.Max(area.Left,area.Right-width+20));
        Top=Math.Clamp(top,area.Top-20,Math.Max(area.Top,area.Bottom-height+20));
        placed=true;
    }
    // The wheel sizes the sheet, Ctrl + wheel makes it more or less transparent. Both are kept.
    void Adjust(MouseWheelEventArgs e){
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0){
            note.Opacity=Math.Clamp(note.Opacity+(e.Delta>0?.08:-.08),.35,1);Opacity=note.Opacity;Schedule();e.Handled=true;return;
        }
        double next=Math.Clamp(Width*(e.Delta>0?1.08:1/1.08),176,1416);Width=next;
        if(isImage&&ratio>0)Height=next/ratio;
        note.Width=next-16;if(isImage)note.Height=Math.Max(40,Height-16);
        Schedule();e.Handled=true;
    }
    void Schedule(){if(!placed)return;save.Stop();save.Start();}
    void Persist(){
        save.Stop();if(editor!=null)note.Text=editor.Text;
        note.Left=Left;note.Top=Top;note.Width=Math.Clamp(Width-16,160,1400);if(isImage)note.Height=Math.Max(40,Height-16);
        Store.Save();
    }
    void Detach(){
        save.Stop();Store.State.Pins.Remove(note);
        if(!string.IsNullOrWhiteSpace(note.ImagePath))try{File.Delete(note.ImagePath);}catch(Exception ex){Store.Log("Papier: "+ex.Message);}
        Store.Save();Close();
    }
    void Animate(double angle){rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(angle,Motion.Span(220)){EasingFunction=Motion.Ease});}
}
internal static class OcrService {
    internal static async Task ReadAsync(Coordinator owner,BitmapSource image){
        try{
            string text=await RecognizeAsync(image);
            if(string.IsNullOrWhiteSpace(text)){owner.ShowToast("Aucun texte lisible dans cette zone");return;}
            SystemActions.Copy(owner,text,"Texte");
            var window=new TextResultWindow(owner,"Texte lu par Windows",text){Owner=owner.Anchor,ShowInTaskbar=owner.PreviewMode};
            window.ShowDialog();
        }catch(Exception ex){owner.Message("La lecture du texte a échoué.\n"+ex.Message);}
    }
    internal static async Task<string> RecognizeAsync(BitmapSource source){
        byte[] png=ScreenShot.Png(source);
        var stream=new InMemoryRandomAccessStream();
        var writer=new DataWriter(stream);writer.WriteBytes(png);await writer.StoreAsync();await writer.FlushAsync();writer.DetachStream();writer.Dispose();
        stream.Seek(0);
        var decoder=await WinBitmapDecoder.CreateAsync(stream);
        using var bitmap=await decoder.GetSoftwareBitmapAsync();
        var engine=OcrEngine.TryCreateFromUserProfileLanguages()??throw new InvalidOperationException("Aucun moteur de reconnaissance n’est installé pour cette langue.");
        var result=await engine.RecognizeAsync(bitmap);
        var lines=new List<string>();
        foreach(var line in result.Lines)lines.Add(string.Join(" ",line.Words.Select(word=>word.Text)));
        return string.Join(Environment.NewLine,lines);
    }
}
internal sealed class TextResultWindow:GlassWindow {
    internal TextResultWindow(Coordinator owner,string title,string text):base("Panneaux Glass — "+title){
        Width=620;Height=Math.Min(520,SystemParameters.WorkArea.Height-90);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=32;ShowActivated=true;
        ContentHost.Margin=new Thickness(28,24,28,22);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);var titles=new StackPanel();titles.Children.Add(UI.Text(title,23,null,true));var sub=UI.Text("Copié dans le presse-papiers · modifie ou recopie si besoin",11,UI.Dim);sub.Margin=new Thickness(0,6,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var editor=new TextBox{Text=text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=14,Padding=new Thickness(15,12,15,12),VerticalContentAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,16,0,14)};
        System.Windows.Automation.AutomationProperties.SetName(editor,"Texte reconnu");Grid.SetRow(editor,1);ContentHost.Children.Add(editor);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var copy=UI.Button("Copier à nouveau",()=>SystemActions.Copy(owner,editor.Text,"Texte"));actions.Children.Add(copy);
        var done=UI.Button("Terminer",Close);done.Margin=new Thickness(10,0,0,0);done.MinWidth=104;actions.Children.Add(done);Grid.SetRow(actions,2);ContentHost.Children.Add(actions);
        Loaded+=(_,_)=>ApplyGlass();KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
}
