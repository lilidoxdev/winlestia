// language: C# 12, file: ShellTaskbar.cs, target: Windows 11 / .NET 8 WPF, native bottom taskbar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace PanneauxGlass;
internal sealed class ShellTaskbar:IDisposable {
    readonly Coordinator owner;readonly List<ShellDockWindow> bars=new();readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(240)};
    readonly TaskbarNative.WinEvent callback;readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;readonly List<IntPtr> hooks=new();
    Process? guard;WindowsDockKey? windowsKey;EventWaitHandle? ready,release;TaskbarNative.Lease? lease;bool enabled,disposed;string key="";
    internal IEnumerable<Window> Windows=>bars;
    internal bool Enabled=>enabled;
    internal ShellTaskbar(Coordinator coordinator){owner=coordinator;callback=(_,kind,h,obj,child,_,_)=>{if(!enabled||obj!=0||child!=0||kind!=3&&Native.Owns(h))return;dispatcher.BeginInvoke(DispatcherPriority.Background,(Action)(()=>{if(!disposed){refresh.Stop();refresh.Start();}}));};refresh.Tick+=(_,_)=>{refresh.Stop();Update();};}
    internal void ShowPreview(){enabled=true;CreateBars();Update(true);}
    internal bool Enable(){
        if(enabled)return true;
        try{
            lease=TaskbarNative.Snapshot();string suffix=Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");lease.Ready="Local\\PanneauxGuardReady-"+suffix;lease.Release="Local\\PanneauxGuardRelease-"+suffix;
            ready=new(false,EventResetMode.ManualReset,lease.Ready);release=new(false,EventResetMode.ManualReset,lease.Release);
            File.WriteAllText(TaskbarNative.LeasePath,JsonSerializer.Serialize(lease,Store.Json));
            string nativeGuard=Path.Combine(Store.Root,"RETABLIR_BARRE_WINDOWS.exe");bool native=File.Exists(nativeGuard);var start=new ProcessStartInfo(native?nativeGuard:Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(native?"--watch-parent":"--taskbar-guard");start.ArgumentList.Add(Environment.ProcessId.ToString());if(native){start.ArgumentList.Add(lease.Ready);start.ArgumentList.Add(lease.Release);}else start.ArgumentList.Add(TaskbarNative.LeasePath);guard=Process.Start(start);
            if(guard==null||!ready.WaitOne(4000))throw new InvalidOperationException("Le secours de la barre n’a pas démarré.");
            enabled=true;CreateBars();TaskbarNative.HideExplorer();if(owner.QtShell==null)foreach(var bar in bars)bar.Register();windowsKey=new WindowsDockKey(RevealDock);foreach(var bar in bars)bar.Conceal(true);
            hooks.Add(TaskbarNative.SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0));hooks.Add(TaskbarNative.SetWinEventHook(0x8002,0x8003,IntPtr.Zero,callback,0,0,0));hooks.Add(TaskbarNative.SetWinEventHook(0x800C,0x800C,IntPtr.Zero,callback,0,0,0));Update(true);return true;
        }catch(Exception ex){Store.Log("Barre Windows: "+ex);Disable();return false;}
    }
    void CreateBars(){foreach(var screen in Forms.Screen.AllScreens){var bar=new ShellDockWindow(owner,this,screen);bars.Add(bar);bar.Show();}}
    internal void DisplaysChanged(){if(!enabled)return;foreach(var bar in bars)bar.Close();bars.Clear();CreateBars();if(owner.QtShell==null)foreach(var bar in bars)bar.Register();Update(true);}
    internal void Update(bool force=false){if(!enabled||disposed)return;if(lease!=null)TaskbarNative.EnsureHidden();if(owner.QtShell!=null){foreach(var bar in bars)bar.Hide();return;}
        var apps=TaskbarNative.Applications();string signature=Native.GetForegroundWindow().ToInt64()+"/"+string.Join("|",apps.Select(a=>a.Handle.ToInt64()+a.Title))+"/"+Theme.Version+"/"+string.Join("|",Store.State.TaskbarPins);
        foreach(var bar in bars){if(owner.QtShell!=null){bar.Hide();continue;}bar.Fullscreen();if(force||key!=signature)bar.SetApps(apps);}key=signature;
    }
    internal IEnumerable<Entry> Pins(){var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var id in Store.State.TaskbarPins){var entry=Store.Entries.FirstOrDefault(e=>e.Id==id);if(entry!=null){if(seen.Add(TaskbarNative.LaunchPath(entry.Target)))yield return entry;continue;}if(File.Exists(id)&&seen.Add(TaskbarNative.LaunchPath(id)))yield return new Entry{Id=id,Name=Path.GetFileNameWithoutExtension(id),Target=id,IconPath=id};}}
    internal void Refresh()=>Update(true);
    internal void RevealDock(){if(!enabled)return;if(owner.QtShell!=null){owner.QtShell.Send("dock");return;}Native.GetCursorPos(out var mouse);var target=Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X,mouse.Y));foreach(var bar in bars)if(bar.IsOn(target))bar.Reveal(true);TaskbarNative.EnsureHidden();}
    internal void PointerTick(){if(owner.QtShell!=null)return;if(!enabled||!Native.GetCursorPos(out var mouse))return;foreach(var bar in bars)bar.PointerTick(mouse);}
    internal void Disable(){enabled=false;windowsKey?.Dispose();windowsKey=null;refresh.Stop();foreach(var hook in hooks)if(hook!=IntPtr.Zero)TaskbarNative.UnhookWinEvent(hook);hooks.Clear();foreach(var bar in bars.ToArray())bar.Close();bars.Clear();
        release?.Set();if(guard!=null)try{guard.WaitForExit(2200);}catch{}TaskbarNative.Restore(lease);guard?.Dispose();guard=null;ready?.Dispose();release?.Dispose();ready=release=null;
    }
    public void Dispose(){if(disposed)return;disposed=true;Disable();}
}

internal sealed class ShellDockWindow:Window {
    internal const double HostHeight=70;
    readonly Coordinator owner;readonly ShellTaskbar manager;readonly Forms.Screen screen;
    readonly StackPanel apps=new(){Orientation=Orientation.Horizontal};readonly TextBlock time=UI.Text("",12),date=UI.Text("",10,UI.Dim);
    readonly Border pill=new(){Height=58,CornerRadius=new CornerRadius(29),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(12,0,12,4)};
    readonly System.Windows.Media.TranslateTransform slide=new();
    readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(15)};
    IntPtr handle;HwndSource? source;uint message;bool registered,placing,covered,closed,hiding;int themeVersion=-1,motionVersion;Native.Rect last;
    DateTime away=DateTime.MinValue,forcedUntil=DateTime.MinValue;
    readonly uint explorerRestart=TaskbarNative.RegisterWindowMessage("TaskbarCreated");
    internal ShellDockWindow(Coordinator coordinator,ShellTaskbar taskbar,Forms.Screen target){
        owner=coordinator;manager=taskbar;screen=target;Title="Panneaux — barre des tâches";WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Height=HostHeight;UseLayoutRounding=true;Background=Brushes.Transparent;
        var surface=new Grid{Background=Brushes.Transparent};pill.RenderTransform=slide;surface.Children.Add(pill);Content=surface;Build();
        SourceInitialized+=(_,_)=>{handle=new WindowInteropHelper(this).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Hook);};
        Closed+=(_,_)=>{closed=true;clock.Stop();if(registered){var data=TaskbarNative.Data(handle);TaskbarNative.SHAppBarMessage(1,ref data);registered=false;}source?.RemoveHook(Hook);};
        clock.Tick+=(_,_)=>Clock();clock.Start();Clock();PlacePhysical();
    }
    void Build(){if(apps.Parent is ScrollViewer old)old.Content=null;if(time.Parent is Panel times)times.Children.Clear();themeVersion=Theme.Version;
        pill.Background=new SolidColorBrush(Theme.Current.Surface);pill.BorderBrush=UI.Alpha(Theme.Current.Dim,42);pill.BorderThickness=new Thickness(1);
        var row=new Grid{Margin=new Thickness(12,4,12,4)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pill.Child=row;
        var left=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(left);
        left.Children.Add(Icon("windows","Mes applications et mes récents",()=>owner.ShowShellPage("access")));
        left.Children.Add(Icon("search","Rechercher · "+owner.PaletteLabel,owner.TogglePalette));
        var scroller=new ScrollViewer{Content=apps,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Center,Margin=new Thickness(14,0,14,0),Style=new Style(typeof(ScrollViewer))};scroller.PreviewMouseWheel+=(_,e)=>{scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset-e.Delta);e.Handled=true;};Grid.SetColumn(scroller,1);row.Children.Add(scroller);
        var right=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(right,2);row.Children.Add(right);
        right.Children.Add(Icon("volume","Son et commandes rapides",owner.ToggleControls));right.Children.Add(Icon("wifi","Réseau",()=>owner.LaunchTarget("ms-settings:network-status")));right.Children.Add(Icon("bell","Notifications Windows",()=>{owner.CollapseImmediately();TaskbarNative.WindowsKey(0x4E);}));right.Children.Add(Icon("settings","Personnaliser",()=>owner.Settings()));
        var when=new StackPanel{VerticalAlignment=VerticalAlignment.Center};time.TextAlignment=date.TextAlignment=TextAlignment.Center;when.Children.Add(time);when.Children.Add(date);var clockButton=Button("Horloge et calendrier",()=>owner.ShowShellPage("dashboard"));clockButton.Width=75;clockButton.Content=when;right.Children.Add(clockButton);
        row.MouseRightButtonUp+=(_,e)=>{owner.TaskbarMenu(this,row);e.Handled=true;};row.MouseLeftButtonDown+=(_,_)=>owner.CollapseImmediately();SizePill();
    }
    static Button Button(string label,Action action){var button=UI.Button("",action);button.Width=40;button.Height=42;button.Padding=new Thickness(7);button.Margin=new Thickness(2,0,2,0);button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;button.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(button,label);return button;}
    static Button Icon(string name,string label,Action action){var button=Button(label,action);button.Content=ShellIcons.View(name,19);return button;}
    internal bool IsOn(Forms.Screen value)=>value.DeviceName==screen.DeviceName;
    void Clock(){time.Text=DateTime.Now.ToString("HH:mm");date.Text=DateTime.Now.ToString("dd MMM");}
    internal void SetApps(IReadOnlyList<TaskbarNative.AppWindow> windows){
        if(themeVersion!=Theme.Version)Build();apps.Children.Clear();var represented=new HashSet<IntPtr>();
        foreach(var entry in manager.Pins().Take(24)){string target=TaskbarNative.LaunchPath(entry.Target);var matching=windows.FirstOrDefault(w=>!represented.Contains(w.Handle)&&!string.IsNullOrEmpty(w.Path)&&string.Equals(w.Path,target,StringComparison.OrdinalIgnoreCase));if(matching!=null)represented.Add(matching.Handle);AddApp(entry,matching,true);}
        if(apps.Children.Count>0&&windows.Any(w=>!represented.Contains(w.Handle)))apps.Children.Add(new Border{Width=1,Height=23,Background=UI.Alpha(Theme.Current.Dim,50),Margin=new Thickness(9,0,9,0)});
        foreach(var window in windows.Where(w=>!represented.Contains(w.Handle)))AddApp(new Entry{Id=string.IsNullOrWhiteSpace(window.Path)?"window:"+window.Handle.ToInt64():"running:"+window.Path.ToLowerInvariant(),Name=window.Title,Target=window.Path,IconPath=window.Path},window,false);
        SizePill();
    }
    void SizePill(){double width=screen.Bounds.Width/TaskbarNative.Scale(screen);pill.Width=Math.Min(Math.Min(1440,width-24),Math.Max(740,420+apps.Children.Count*44));}
    void AddApp(Entry entry,TaskbarNative.AppWindow? window,bool pinned){var button=Button(entry.Name,()=>{owner.CollapseImmediately();if(window!=null)TaskbarNative.Activate(window);else owner.Launch(entry);});var stack=new Grid();var image=new Image{Width=28,Height=28,Stretch=Stretch.Uniform};stack.Children.Add(image);owner.Icons.Load(entry,source=>UI.SetIcon(image,source),"taskbar");
        if(window!=null){bool active=Native.GetForegroundWindow()==window.Handle;stack.Children.Add(new Border{Height=3,Width=active?16:4,CornerRadius=new CornerRadius(2),Background=UI.Accent,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,-3)});button.Background=active?UI.Alpha(Theme.Current.Accent,24):Brushes.Transparent;}button.Content=stack;button.MouseRightButtonUp+=(_,e)=>{owner.TaskbarAppMenu(this,button,entry,pinned);e.Handled=true;};apps.Children.Add(button);}
    void PlacePhysical(){double scale=TaskbarNative.Scale(screen);var b=screen.Bounds;Left=b.Left/scale;Top=b.Bottom/scale-HostHeight;Width=b.Width/scale;Height=HostHeight;if(handle!=IntPtr.Zero)TaskbarNative.SetWindowPos(handle,new IntPtr(-1),b.Left,b.Bottom-(int)Math.Round(HostHeight*scale),b.Width,(int)Math.Round(HostHeight*scale),0x0010);}
    internal void Register(){if(handle==IntPtr.Zero||registered)return;message=TaskbarNative.RegisterWindowMessage("PanneauxAppbar-"+handle);var data=TaskbarNative.Data(handle);data.Callback=message;if(TaskbarNative.SHAppBarMessage(0,ref data)==UIntPtr.Zero)throw new InvalidOperationException("Windows a refusé la réservation du bord.");registered=true;Position();}
    void Position(){if(!registered||placing||closed)return;placing=true;try{var b=screen.Bounds;var data=TaskbarNative.Data(handle);data.Edge=3;data.Rect=new Native.Rect{Left=b.Left,Top=b.Bottom-2,Right=b.Right,Bottom=b.Bottom};TaskbarNative.SHAppBarMessage(2,ref data);data.Rect.Top=data.Rect.Bottom-2;if(data.Rect.Left!=last.Left||data.Rect.Top!=last.Top||data.Rect.Bottom!=last.Bottom||data.Rect.Right!=last.Right){TaskbarNative.SHAppBarMessage(3,ref data);last=data.Rect;}PlacePhysical();}finally{placing=false;}}
    internal void Fullscreen(){var foreground=Native.GetForegroundWindow();covered=false;if(!Native.Owns(foreground)&&TaskbarNative.ClassName(foreground) is not ("Progman" or "WorkerW" or "Shell_TrayWnd")&&TaskbarNative.GetWindowRect(foreground,out var rect)){var b=screen.Bounds;covered=rect.Left<=b.Left&&rect.Top<=b.Top&&rect.Right>=b.Right&&rect.Bottom>=b.Bottom;}if(covered&&DateTime.UtcNow>forcedUntil)Conceal(true);}
    internal void PointerTick(Native.Point mouse){if(closed)return;Fullscreen();var b=screen.Bounds;bool edge=mouse.X>=b.Left&&mouse.X<b.Right&&mouse.Y>=b.Bottom-3&&mouse.Y<b.Bottom;if(covered&&DateTime.UtcNow>forcedUntil)return;
        if(edge){away=DateTime.MinValue;Reveal(false);return;}bool inside=false;if(IsVisible&&pill.IsVisible){var point=pill.PointToScreen(new Point(0,0));double scale=TaskbarNative.Scale(screen);inside=mouse.X>=point.X-8&&mouse.X<=point.X+pill.ActualWidth*scale+8&&mouse.Y>=point.Y-10&&mouse.Y<b.Bottom;}if(inside){away=DateTime.MinValue;return;}if(!IsVisible||DateTime.UtcNow<forcedUntil)return;if(away==DateTime.MinValue)away=DateTime.UtcNow;if((DateTime.UtcNow-away).TotalMilliseconds>450)Conceal(false);
    }
    internal void Reveal(bool keyboard){if(closed)return;hiding=false;if(keyboard)forcedUntil=DateTime.UtcNow.AddSeconds(3);if(IsVisible&&slide.Y==0&&pill.Opacity>=.99)return;int version=++motionVersion;double start=IsVisible?slide.Y:HostHeight;slide.BeginAnimation(TranslateTransform.YProperty,null);pill.BeginAnimation(UIElement.OpacityProperty,null);slide.Y=0;pill.Opacity=1;if(!IsVisible)Show();Topmost=true;PlacePhysical();if(Motion.On){var animation=new System.Windows.Media.Animation.DoubleAnimation(start,0,Motion.Duration(MotionKind.FastSpatial)){EasingFunction=Motion.Ease,FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop};animation.Completed+=(_,_)=>{if(version==motionVersion)slide.BeginAnimation(TranslateTransform.YProperty,null);};slide.BeginAnimation(TranslateTransform.YProperty,animation);pill.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(.3,1,Motion.Duration(MotionKind.FastEffects)){FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});}}
    internal void Conceal(bool immediate=false){if(!IsVisible||closed||hiding&&!immediate)return;hiding=true;int version=++motionVersion;double from=slide.Y;slide.BeginAnimation(TranslateTransform.YProperty,null);pill.BeginAnimation(UIElement.OpacityProperty,null);if(immediate||!Motion.On){Hide();slide.Y=0;return;}var animation=new System.Windows.Media.Animation.DoubleAnimation(from,HostHeight,Motion.Duration(MotionKind.FastEffects)){EasingFunction=Motion.Curve(MotionKind.FastEffects),FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop};animation.Completed+=(_,_)=>{if(version==motionVersion){Hide();slide.BeginAnimation(TranslateTransform.YProperty,null);slide.Y=0;}};slide.BeginAnimation(TranslateTransform.YProperty,animation);}
    IntPtr Hook(IntPtr w,int msg,IntPtr wp,IntPtr lp,ref bool handled){if((uint)msg==message&&message!=0){if(wp.ToInt32()==1)Dispatcher.BeginInvoke(DispatcherPriority.Background,(Action)Position);else if(wp.ToInt32()==2)Fullscreen();}if((uint)msg==explorerRestart){registered=false;last=default;Dispatcher.BeginInvoke((Action)(()=>{TaskbarNative.HideExplorer();if(owner.QtShell==null)Register();manager.Refresh();}));}return IntPtr.Zero;}
}
