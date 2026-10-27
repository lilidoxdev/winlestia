// language: C# 12, file: FocusTimer.cs, target: Windows 11 / .NET 8 WPF, small always-on-top focus countdown
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
// A countdown is a window, not a background task: closing it stops everything.
internal sealed class FocusTimer:GlassWindow {
    static FocusTimer? active;
    readonly Coordinator owner;readonly TextBlock remaining,label;readonly Border fill;readonly Grid track=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};readonly TextBlock alarm;int left,total;
    [DllImport("user32.dll")]static extern bool MessageBeep(uint kind);
    internal static string DurationText(int seconds){var span=TimeSpan.FromSeconds(Math.Max(0,seconds));return span.TotalHours>=1?((int)span.TotalHours)+" h "+span.Minutes.ToString("00")+" min":span.Minutes>0?span.Minutes+" min "+span.Seconds.ToString("00")+" s":span.Seconds+" s";}
    internal static void Start(Coordinator owner,int seconds){
        active?.Close();active=null;
        var timer=new FocusTimer(owner,seconds);active=timer;
        var work=owner.Screen.WorkingArea;double scale=VisualTreeHelper.GetDpi(owner.Anchor).DpiScaleX;
        timer.MoveInto(work.Right/scale-24-timer.Width,work.Bottom/scale-24-timer.Height,timer.Width,timer.Height,0,true);
        timer.timer.Start();
    }
    FocusTimer(Coordinator coordinator,int seconds):base("Panneaux Glass — focus"){
        owner=coordinator;Width=300;Height=196;Radius=28;ContentHost.Margin=new Thickness(22,20,22,20);
        label=UI.Text("FOCUS · "+DurationText(seconds).ToUpperInvariant(),10,UI.Brush("#A9D3E4F1"),true);alarm=UI.Text("Sonnerie à la fin",9,UI.Dim);alarm.HorizontalAlignment=HorizontalAlignment.Right;alarm.VerticalAlignment=VerticalAlignment.Center;
        remaining=UI.Text("",34,null,true);remaining.Margin=new Thickness(0,6,0,2);remaining.Typography.NumeralAlignment=FontNumeralAlignment.Tabular;
        track.Height=4;track.Margin=new Thickness(0,9,0,12);track.Children.Add(new Border{CornerRadius=new CornerRadius(2),Background=UI.Brush("#1FFFFFFF")});
        fill=new Border{CornerRadius=new CornerRadius(2),Background=UI.Brush("#BFE9F7FF"),HorizontalAlignment=HorizontalAlignment.Left};track.Children.Add(fill);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
        buttons.Children.Add(UI.Button("+1 min",()=>{left+=60;total+=60;Update();}));
        var test=UI.Button("",()=>FocusAlarm.Play());test.Content=UI.Glyph("\uE767",14);test.Width=34;test.Height=34;test.Padding=new Thickness(0);test.Margin=new Thickness(8,0,0,0);test.ToolTip="Écouter la sonnerie";System.Windows.Automation.AutomationProperties.SetName(test,"Écouter la sonnerie de fin");buttons.Children.Add(test);
        var stop=UI.Button("Arrêter",()=>{timer.Stop();Close();});stop.Margin=new Thickness(8,0,0,0);buttons.Children.Add(stop);
        var head=new DockPanel();head.Children.Add(alarm);head.Children.Add(label);
        var body=new StackPanel();body.Children.Add(head);body.Children.Add(remaining);body.Children.Add(track);body.Children.Add(buttons);ContentHost.Children.Add(body);
        total=left=Math.Max(1,seconds);
        timer.Tick+=(_,_)=>{left--;Update();if(left<=0)Finish();};
        SizeChanged+=(_,_)=>Draw();Closed+=(_,_)=>{timer.Stop();if(active==this)active=null;};
        Update();
    }
    void Update(){
        if(left<0)left=0;var span=TimeSpan.FromSeconds(left);remaining.Text=span.TotalHours>=1?((int)span.TotalHours)+":"+span.Minutes.ToString("00")+":"+span.Seconds.ToString("00"):span.Minutes.ToString("00")+":"+span.Seconds.ToString("00");Draw();
    }
    void Draw(){double ratio=total<=0?0:Math.Clamp(left/(double)total,0,1);fill.Width=Math.Max(0,track.ActualWidth*ratio);fill.Background=UI.Brush(ratio<.2?"#E8F3C79A":"#BFE9F7FF");}
    void Finish(){
        timer.Stop();FocusAlarm.Play();owner.ShowToast("Focus terminé · "+DurationText(total));owner.Notify("Focus terminé","Tes "+DurationText(total)+" sont écoulées.");Close();
    }
}
