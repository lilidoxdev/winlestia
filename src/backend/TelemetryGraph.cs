// language: C# 12, file: TelemetryGraph.cs, target: Windows 11 / .NET 8 WPF, bounded retained-mode charts
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PanneauxGlass;
internal sealed class TelemetryGraph:FrameworkElement {
    internal const int Capacity=60;
    readonly List<(double? a,double? b)> history=new();readonly DrawingVisual visual=new();readonly TranslateTransform translation=new();
    readonly Color color;readonly Brush stroke,secondary;readonly bool percent;
    internal int Count=>history.Count;
    internal TelemetryGraph(string hex,bool percent=true){this.percent=percent;color=UI.Color(hex);stroke=UI.Brush(hex);secondary=UI.Brush("#E6C7AAFF");AddVisualChild(visual);visual.Transform=translation;IsHitTestVisible=false;SizeChanged+=(_,_)=>Draw(false);}
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?visual:throw new ArgumentOutOfRangeException(nameof(index));
    internal void Push(double? value,double? second=null){history.Add((Valid(value),Valid(second)));if(history.Count>Capacity)history.RemoveAt(0);Draw(true);}
    static double? Valid(double? v)=>v.HasValue&&double.IsFinite(v.Value)&&v>=0?v:null;
    internal void Reset(){history.Clear();Stop();Draw(false);}
    internal void Stop()=>translation.BeginAnimation(TranslateTransform.XProperty,null);
    void Draw(bool animate){double width=ActualWidth,height=ActualHeight;if(width<2||height<2)return;Clip=new RectangleGeometry(new Rect(0,0,width,height));double step=width/(Capacity-1),max=percent?100:Math.Max(1024,history.SelectMany(v=>new[]{v.a??0,v.b??0}).DefaultIfEmpty().Max()*1.15);
        using(var drawing=visual.RenderOpen()){var grid=new Pen(UI.Brush("#14FFFFFF"),.7);foreach(double fraction in new[]{.25,.75})drawing.DrawLine(grid,new Point(0,height*fraction),new Point(width,height*fraction));Plot(drawing,false,stroke,max,width,height,step);if(history.Any(v=>v.b.HasValue))Plot(drawing,true,secondary,max,width,height,step);}
        translation.BeginAnimation(TranslateTransform.XProperty,null);translation.X=0;if(animate&&history.Count>1&&Store.State.Motion>0){var motion=new DoubleAnimation(step,0,TimeSpan.FromMilliseconds(240*Store.State.Motion)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};Timeline.SetDesiredFrameRate(motion,60);translation.BeginAnimation(TranslateTransform.XProperty,motion);}
    }
    void Plot(DrawingContext drawing,bool second,Brush brush,double max,double width,double height,double step){var line=new StreamGeometry();Point? last=null;using(var context=line.Open()){bool started=false;for(int i=0;i<history.Count;i++){var value=second?history[i].b:history[i].a;if(!value.HasValue){started=false;continue;}var point=new Point(width-(history.Count-1-i)*step,Math.Clamp(height-3-value.Value/max*(height-6),3,height-3));if(!started){context.BeginFigure(point,false,false);started=true;}else context.LineTo(point,true,false);last=point;}}line.Freeze();var pen=new Pen(brush.CloneCurrentValue(),1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};pen.Freeze();drawing.DrawGeometry(null,pen,line);if(last.HasValue)drawing.DrawEllipse(brush,null,last.Value,2.6,2.6);}
}
