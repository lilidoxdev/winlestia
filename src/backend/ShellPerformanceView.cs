// language: C# 12, file: ShellPerformanceView.cs, target: Windows 11 / .NET 8 WPF, demand-driven native performance page
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
internal sealed class ShellPerformanceView:Border,IDisposable {
    readonly PcTelemetry telemetry=new();readonly SemaphoreSlim lifecycle=new(1,1);readonly System.Collections.Generic.List<Border> plates=new();bool active,closed;int version;
    readonly TextBlock stamp=UI.Text("Préparation des mesures…",12,UI.Dim),cpu=UI.Text("—",35,null,true),cpuDetail=UI.Text("Processeur",12,UI.Dim),gpu=UI.Text("—",35,null,true),gpuDetail=UI.Text("Carte graphique",12,UI.Dim),ram=UI.Text("—",35,null,true),ramDetail=UI.Text("Mémoire",12,UI.Dim),network=UI.Text("—",24,null,true),networkDetail=UI.Text("Connexion réseau",12,UI.Dim),storage=UI.Text("—",15,null,true);
    readonly TelemetryGraph cpuGraph=new("#A77967"),gpuGraph=new("#8E819D"),ramGraph=new("#7C9581"),netGraph=new("#A77967",false);
    internal bool Sampling=>telemetry.Running;
    internal long Samples=>telemetry.Samples;
    internal ShellPerformanceView(Coordinator owner){var root=new StackPanel();var head=new DockPanel{Margin=new Thickness(3,0,8,18)};var more=UI.Button("Toutes les mesures   ›",()=>{owner.ShowShellPage("dashboard");owner.TogglePc();});DockPanel.SetDock(more,Dock.Right);head.Children.Add(more);head.Children.Add(stamp);root.Children.Add(head);
        var grid=new UniformGrid{Columns=3};grid.Children.Add(Card("PROCESSEUR",cpu,cpuDetail,cpuGraph));grid.Children.Add(Card("CARTE GRAPHIQUE",gpu,gpuDetail,gpuGraph));grid.Children.Add(Card("MÉMOIRE VIVE",ram,ramDetail,ramGraph));root.Children.Add(grid);
        var bottom=new UniformGrid{Columns=2};bottom.Children.Add(Card("RÉSEAU · DÉBIT EN DIRECT",network,networkDetail,netGraph));var disks=new StackPanel();disks.Children.Add(UI.Text("STOCKAGE",11,UI.Dim,true));storage.Margin=new Thickness(0,20,0,0);storage.TextWrapping=TextWrapping.Wrap;disks.Children.Add(storage);bottom.Children.Add(Plate(disks));root.Children.Add(bottom);var note=UI.Text("Les mesures s’arrêtent en quittant cette page. Une température absente est indiquée par —.",11,UI.Dim);note.Margin=new Thickness(4,12,4,0);note.TextWrapping=TextWrapping.Wrap;root.Children.Add(note);Child=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}
    Border Card(string title,TextBlock value,TextBlock detail,TelemetryGraph graph){var content=new StackPanel();content.Children.Add(UI.Text(title,11,UI.Dim,true));value.Margin=new Thickness(0,16,0,8);content.Children.Add(value);detail.TextWrapping=TextWrapping.Wrap;detail.MinHeight=35;content.Children.Add(detail);graph.Height=75;graph.Margin=new Thickness(0,17,0,0);content.Children.Add(graph);return Plate(content);}
    Border Plate(UIElement child){var plate=new Border{CornerRadius=new CornerRadius(22),Padding=new Thickness(22),Margin=new Thickness(0,0,12,12),Child=child};plates.Add(plate);RefreshTheme();return plate;}
    internal void RefreshTheme(){foreach(var plate in plates)plate.Background=new SolidColorBrush(Theme.Mix(Theme.Current.Surface,Theme.Current.Accent,Theme.Current.Light?.085:.12));}
    internal async void SetActive(bool value){if(active==value)return;active=value;int expected=++version;await lifecycle.WaitAsync();try{await telemetry.StopAsync();if(!active||closed||expected!=version){foreach(var graph in new[]{cpuGraph,gpuGraph,ramGraph,netGraph})graph.Stop();return;}stamp.Text="Préparation des capteurs…";telemetry.Start(reading=>Dispatcher.BeginInvoke(DispatcherPriority.Background,(Action)(()=>{if(active&&!closed&&expected==version)Update(reading);})),error:message=>Dispatcher.BeginInvoke((Action)(()=>{if(active&&!closed&&expected==version){stamp.Text="Mesures indisponibles";stamp.ToolTip=message;}})),quick:true);}catch(Exception ex){Store.Log("Page performances: "+ex.Message);}finally{lifecycle.Release();}}
    static string Percent(double? value)=>value.HasValue?Math.Round(value.Value)+" %":"—";
    static string Heat(double? value)=>value.HasValue?Math.Round(value.Value)+" °C":"Température —";
    static string Rate(double? value)=>value.HasValue?(value>=1048576?(value/1048576).Value.ToString("0.0")+" Mo/s":(value/1024).Value.ToString("0.0")+" Ko/s"):"—";
    void Update(PcReading value){stamp.Text="● En direct · "+value.At.ToLocalTime().ToString("HH:mm:ss");cpu.Text=Percent(value.Cpu);cpuDetail.Text=Heat(value.CpuTemperature)+" · "+value.CpuName;cpuGraph.Push(value.Cpu);var graphics=value.Gpus.FirstOrDefault();gpu.Text=Percent(graphics?.Load);gpuDetail.Text=Heat(graphics?.Temperature)+" · "+(graphics?.Name??"Capteur indisponible");gpuGraph.Push(graphics?.Load);double used=value.MemoryTotal>0?(value.MemoryTotal-value.MemoryAvailable)/(double)value.MemoryTotal*100:0;ram.Text=Percent(value.MemoryTotal>0?used:null);ramDetail.Text=((value.MemoryTotal-value.MemoryAvailable)/1073741824d).ToString("0.0")+" / "+(value.MemoryTotal/1073741824d).ToString("0.0")+" Go utilisés";ramGraph.Push(value.MemoryTotal>0?used:null);network.Text="↓ "+Rate(value.Download);networkDetail.Text="↑ "+Rate(value.Upload)+" · "+value.Network;netGraph.Push(value.Download,value.Upload);storage.Text=string.Join("\n\n",value.Drives.Select(d=>d.Name+"   "+(d.Free/1073741824d).ToString("0")+" Go libres / "+(d.Total/1073741824d).ToString("0")+" Go"));}
    public void Dispose(){closed=true;SetActive(false);telemetry.Dispose();}
}
