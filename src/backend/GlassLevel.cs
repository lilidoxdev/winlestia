// language: C# 12, file: GlassLevel.cs, target: Windows 11 / .NET 8 WPF, pointer/keyboard/accessible liquid capsule
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
internal sealed class GlassLevel:RangeBase {
    internal string Glyph {get;set;}="\uE767";internal event Action<bool>? DraggingChanged;bool dragging;internal bool Dragging=>dragging;
    static readonly Brush track=Freeze(new LinearGradientBrush(Color.FromArgb(54,255,255,255),Color.FromArgb(16,255,255,255),70));
    static readonly Brush fill=Freeze(new LinearGradientBrush(Color.FromRgb(250,253,255),Color.FromRgb(214,239,251),90));
    static Brush Freeze(Brush brush){brush.Freeze();return brush;}
    internal GlassLevel(string name){Minimum=0;Maximum=100;SmallChange=1;LargeChange=10;Focusable=true;Cursor=Cursors.Hand;MinWidth=76;Height=198;UseLayoutRounding=true;System.Windows.Automation.AutomationProperties.SetName(this,name);IsEnabledChanged+=(_,_)=>InvalidateVisual();}
    protected override AutomationPeer OnCreateAutomationPeer()=>new LevelPeer(this);
    protected override void OnValueChanged(double oldValue,double newValue){base.OnValueChanged(oldValue,newValue);InvalidateVisual();if(UIElementAutomationPeer.FromElement(this) is { } peer)peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty,oldValue,newValue);}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);double width=ActualWidth,height=ActualHeight;if(width<1||height<1)return;double radius=Math.Min(width/2,48),ratio=(Value-Minimum)/Math.Max(1,Maximum-Minimum);var clip=new RectangleGeometry(new Rect(0,0,width,height),radius,radius);dc.PushClip(clip);dc.DrawRectangle(track,null,new Rect(0,0,width,height));if(IsEnabled)dc.DrawRectangle(fill,null,new Rect(0,height*(1-ratio),width,height*ratio));dc.Pop();var edge=new Pen(UI.Brush(IsKeyboardFocused?"#E0D8F5FF":"#42FFFFFF"),IsKeyboardFocused?2:1);dc.DrawRoundedRectangle(null,edge,new Rect(.5,.5,width-1,height-1),radius,radius);
        var icon=new FormattedText(Glyph,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe Fluent Icons"),30,UI.Brush(IsEnabled&&ratio>.23?"#277EB7":"#F1F8FF"),VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(icon,new Point((width-icon.Width)/2,height-58));string value=IsEnabled?Math.Round(Value).ToString(CultureInfo.InvariantCulture):"—";var number=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(UI.Font,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),17,UI.Brush(IsEnabled&&ratio>.88?"#275675":"#F5FAFF"),VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(number,new Point((width-number.Width)/2,20));}
    void FromPointer(MouseEventArgs e)=>Value=Minimum+Math.Clamp(1-e.GetPosition(this).Y/Math.Max(1,ActualHeight),0,1)*(Maximum-Minimum);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);if(!IsEnabled)return;Focus();CaptureMouse();dragging=true;DraggingChanged?.Invoke(true);FromPointer(e);e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging&&e.LeftButton==MouseButtonState.Pressed){FromPointer(e);e.Handled=true;}}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);if(!dragging)return;FromPointer(e);ReleaseMouseCapture();EndDrag();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);EndDrag();}
    void EndDrag(){if(!dragging)return;dragging=false;DraggingChanged?.Invoke(false);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){if(!IsEnabled)return;Value+=Math.Sign(e.Delta)*2;e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);double? next=e.Key switch{Key.Up or Key.Right=>Value+SmallChange,Key.Down or Key.Left=>Value-SmallChange,Key.PageUp=>Value+LargeChange,Key.PageDown=>Value-LargeChange,Key.Home=>Minimum,Key.End=>Maximum,_=>null};if(next.HasValue){Value=Math.Clamp(next.Value,Minimum,Maximum);e.Handled=true;}}
    sealed class LevelPeer:FrameworkElementAutomationPeer,IRangeValueProvider {
        GlassLevel Level=>(GlassLevel)Owner;internal LevelPeer(GlassLevel owner):base(owner){}protected override string GetClassNameCore()=>"GlassLevel";protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Slider;public override object? GetPattern(PatternInterface pattern)=>pattern==PatternInterface.RangeValue?this:base.GetPattern(pattern);
        public bool IsReadOnly=>!Level.IsEnabled;public double LargeChange=>Level.LargeChange;public double SmallChange=>Level.SmallChange;public double Maximum=>Level.Maximum;public double Minimum=>Level.Minimum;public double Value=>Level.Value;
        public void SetValue(double value){if(!Level.IsEnabled)throw new ElementNotEnabledException();if(value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(nameof(value));Level.Dispatcher.Invoke(()=>Level.Value=value);}
    }
}
