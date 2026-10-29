// language: C# 12, file: Motion.cs, target: Windows 11 / .NET 8 WPF, independent shared motion system
using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PanneauxGlass;
internal enum MotionKind { StandardSmall,Standard,StandardLarge,StandardExtraLarge,EmphasizedSmall,Emphasized,EmphasizedLarge,EmphasizedExtraLarge,FastSpatial,DefaultSpatial,SlowSpatial,FastEffects,DefaultEffects,SlowEffects }
internal sealed class ShellSpline:EasingFunctionBase {
    readonly double x1,y1,x2,y2;
    internal ShellSpline(double a,double b,double c,double d){x1=a;y1=b;x2=c;y2=d;EasingMode=EasingMode.EaseIn;}
    protected override Freezable CreateInstanceCore()=>new ShellSpline(x1,y1,x2,y2);
    protected override double EaseInCore(double time){double lo=0,hi=1,t=time;for(int i=0;i<14;i++){double x=Bezier(t,x1,x2);if(Math.Abs(x-time)<.00001)break;if(x<time)lo=t;else hi=t;t=(lo+hi)/2;}return Bezier(t,y1,y2);}
    static double Bezier(double t,double a,double b){double s=1-t;return 3*s*s*t*a+3*s*t*t*b+t*t*t;}
}
internal static class Motion {
    sealed class State {internal int Version;internal Transform? Original;internal TranslateTransform? Translation;internal TransformGroup? Group;}
    static readonly ConditionalWeakTable<FrameworkElement,State> states=new();
    internal static int Level=>SystemParameters.ClientAreaAnimation?Math.Clamp(Store.State.MotionLevel,0,2):0;
    internal static string LevelName=>Level switch{0=>"Calme",2=>"Expressif",_=>"Standard"};
    internal static double Factor=>Level switch{0=>0,2=>1.15,_=>1};
    internal static bool On=>Level>0&&Store.State.Motion>0&&!M3.ReducedEffects;
    internal static IEasingFunction Ease=>Curve(MotionKind.DefaultSpatial);
    internal static IEasingFunction Curve(MotionKind kind)=>kind switch{
        MotionKind.FastEffects or MotionKind.DefaultEffects or MotionKind.SlowEffects=>new ShellSpline(.2,0,.2,1),
        MotionKind.EmphasizedSmall or MotionKind.Emphasized or MotionKind.EmphasizedLarge or MotionKind.EmphasizedExtraLarge=>new ShellSpline(.16,.76,.18,1),
        MotionKind.SlowSpatial=>new ShellSpline(.18,.92,.2,1),
        _=>new ShellSpline(.18,.85,.25,1)};
    internal static TimeSpan Span(double milliseconds)=>TimeSpan.FromMilliseconds(On?Math.Clamp(milliseconds*Factor,1,600):1);
    internal static TimeSpan Duration(MotionKind kind)=>Span(kind switch{MotionKind.StandardSmall or MotionKind.FastEffects=>120,MotionKind.Standard=>190,MotionKind.StandardLarge or MotionKind.DefaultEffects=>250,MotionKind.StandardExtraLarge or MotionKind.SlowEffects=>330,MotionKind.EmphasizedSmall or MotionKind.FastSpatial=>210,MotionKind.Emphasized or MotionKind.DefaultSpatial=>290,MotionKind.EmphasizedLarge or MotionKind.SlowSpatial=>370,_=>420});
    internal static void Reveal(FrameworkElement element,int index=0){
        var state=states.GetOrCreateValue(element);int version=++state.Version;element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=1;
        if(state.Translation!=null){state.Translation.BeginAnimation(TranslateTransform.YProperty,null);if(ReferenceEquals(element.RenderTransform,state.Group))element.RenderTransform=state.Original??Transform.Identity;}
        if(!On)return;
        state.Original=element.RenderTransform;var translate=new TranslateTransform(0,0);state.Translation=translate;var group=new TransformGroup();if(state.Original!=null)group.Children.Add(state.Original);group.Children.Add(translate);state.Group=group;element.RenderTransform=group;
        var delay=TimeSpan.FromMilliseconds(Math.Min(96,index*18));
        var opacity=new DoubleAnimation(.25,1,Duration(MotionKind.DefaultEffects)){BeginTime=delay,EasingFunction=Curve(MotionKind.DefaultEffects),FillBehavior=FillBehavior.Stop};
        var move=new DoubleAnimation(12,0,Duration(MotionKind.DefaultSpatial)){BeginTime=delay,EasingFunction=Ease,FillBehavior=FillBehavior.Stop};
        move.Completed+=(_,_)=>{if(state.Version!=version)return;element.BeginAnimation(UIElement.OpacityProperty,null);translate.BeginAnimation(TranslateTransform.YProperty,null);if(ReferenceEquals(element.RenderTransform,group))element.RenderTransform=state.Original??Transform.Identity;state.Translation=null;state.Group=null;};
        element.BeginAnimation(UIElement.OpacityProperty,opacity,HandoffBehavior.SnapshotAndReplace);translate.BeginAnimation(TranslateTransform.YProperty,move,HandoffBehavior.SnapshotAndReplace);
    }
    // Whole-panel Gaussian blur is absent: transitions animate retained transforms.
    internal static void BlurReveal(FrameworkElement element,double from=12){element.Effect=null;}
    internal static void Spring(FrameworkElement element,double from=.96){if(!On)return;var scale=new ScaleTransform(1,1);element.RenderTransformOrigin=new Point(.5,.5);element.RenderTransform=scale;var animation=new DoubleAnimation(from,1,Duration(MotionKind.FastSpatial)){EasingFunction=Ease,FillBehavior=FillBehavior.Stop};scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);}
    internal static void Stagger(FrameworkElement element,int index)=>Reveal(element,index);
    internal static void BindButton(Button button){var scale=new ScaleTransform(1,1);button.RenderTransformOrigin=new Point(.5,.5);button.RenderTransform=scale;void Change(double value){double from=scale.ScaleX;scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=value;if(!On)return;var animation=new DoubleAnimation(from,value,Duration(MotionKind.FastEffects)){EasingFunction=Curve(MotionKind.FastEffects),FillBehavior=FillBehavior.Stop};scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);}button.MouseEnter+=(_,_)=>Change(1.02);button.MouseLeave+=(_,_)=>Change(1);button.PreviewMouseLeftButtonDown+=(_,_)=>Change(.97);button.PreviewMouseLeftButtonUp+=(_,_)=>Change(button.IsMouseOver?1.02:1);button.Unloaded+=(_,_)=>{scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=1;};}
    internal static void Pulse(UIElement element,double low=.55,double high=1,double seconds=2.4){if(!On)return;element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(low,high,TimeSpan.FromSeconds(seconds)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});}
    internal static void Stop(UIElement element){element.BeginAnimation(UIElement.OpacityProperty,null);if(element is FrameworkElement framework&&states.TryGetValue(framework,out var state))state.Version++;}
}
