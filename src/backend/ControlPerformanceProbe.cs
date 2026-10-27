// language: C# 12, file: ControlPerformanceProbe.cs, target: Windows 11 / .NET 8 WPF, home/control transition measurement
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
internal static class ControlPerformanceProbe {
    internal static void Run(Coordinator coordinator){coordinator.Pinned=true;coordinator.Open();coordinator.ToggleControls();var process=Process.GetCurrentProcess();var clock=new Stopwatch();var frames=new List<double>();TimeSpan last=TimeSpan.Zero;double until=0,cpu=0;int step=-1;
        EventHandler render=(_,e)=>{if(!clock.IsRunning||clock.Elapsed.TotalMilliseconds>until||e is not RenderingEventArgs frame)return;if(last!=TimeSpan.Zero){double delta=(frame.RenderingTime-last).TotalMilliseconds;if(delta>0&&delta<150)frames.Add(delta);}last=frame.RenderingTime;};CompositionTarget.Rendering+=render;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=(_,_)=>{if(step<0){coordinator.ToggleControls();step=0;timer.Interval=TimeSpan.FromMilliseconds(1100);return;}if(step==0){process.Refresh();cpu=process.TotalProcessorTime.TotalMilliseconds;clock.Start();}
            if(step<12){last=TimeSpan.Zero;until=clock.Elapsed.TotalMilliseconds+360;coordinator.ToggleControls();step++;return;}timer.Stop();clock.Stop();CompositionTarget.Rendering-=render;process.Refresh();frames.Sort();double Percentile(double percentile)=>frames.Count==0?0:frames[Math.Clamp((int)Math.Ceiling(frames.Count*percentile)-1,0,frames.Count-1)];
            File.WriteAllText(Path.Combine(Store.Root,"motion-controls-5.3.json"),JsonSerializer.Serialize(new{At=DateTimeOffset.Now,Cycles=6,Frames=frames.Count,ElapsedMs=clock.Elapsed.TotalMilliseconds,CpuMs=process.TotalProcessorTime.TotalMilliseconds-cpu,TotalCpuPercent=(process.TotalProcessorTime.TotalMilliseconds-cpu)/clock.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,WorkingSetMB=process.WorkingSet64/1048576d,PrivateMB=process.PrivateMemorySize64/1048576d,FrameP50Ms=Percentile(.5),FrameP95Ms=Percentile(.95),FrameP99Ms=Percentile(.99),Over25Ms=frames.Count(f=>f>25),Note="Intervalles WPF pendant les 360 ms suivant chaque transition ; ne mesure pas les présentations DWM ni les FPS d’un jeu."},Store.Json));System.Windows.Application.Current.Shutdown();};timer.Start();
    }
}
