// language: C# 12, file: PerformanceProbe.cs, target: Windows 11 / .NET 8 WPF, repeatable local motion benchmark
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
internal static class PerformanceProbe {
    internal static void Run(Coordinator coordinator,string filename,bool full=false){
        coordinator.Pinned=true;if(full)coordinator.ShowWorkspace();else coordinator.Open();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};
        var process=Process.GetCurrentProcess();var clock=new Stopwatch();var frames=new List<double>();TimeSpan last=TimeSpan.Zero;double cpu=0;int step=0;bool recording=false;
        EventHandler rendering=(_,e)=>{if(!recording||e is not RenderingEventArgs r)return;if(last!=TimeSpan.Zero){double ms=(r.RenderingTime-last).TotalMilliseconds;if(ms>0&&ms<150)frames.Add(ms);}last=r.RenderingTime;};
        CompositionTarget.Rendering+=rendering;
        timer.Tick+=(_,_)=>{if(step==0){process.Refresh();cpu=process.TotalProcessorTime.TotalMilliseconds;clock.Start();recording=true;timer.Interval=TimeSpan.FromMilliseconds(1100);}
            if(step<12){last=TimeSpan.Zero;if(step%2==0)coordinator.Collapse();else{if(full)coordinator.ShowWorkspace();else coordinator.Open();}step++;return;}
            timer.Stop();recording=false;clock.Stop();process.Refresh();CompositionTarget.Rendering-=rendering;frames.Sort();double Percentile(double p)=>frames.Count==0?0:frames[Math.Min(frames.Count-1,(int)Math.Ceiling(frames.Count*p)-1)];
            var report=new{At=DateTimeOffset.Now,View=full?"Four panels":"Home",RenderTier=RenderCapability.Tier>>16,Cycles=6,ElapsedMs=clock.Elapsed.TotalMilliseconds,CpuMs=process.TotalProcessorTime.TotalMilliseconds-cpu,OneCorePercent=(process.TotalProcessorTime.TotalMilliseconds-cpu)/clock.Elapsed.TotalMilliseconds*100,LogicalProcessors=Environment.ProcessorCount,WorkingSetMB=process.WorkingSet64/1048576d,PrivateMB=process.PrivateMemorySize64/1048576d,Frames=frames.Count,FrameP50Ms=Percentile(.5),FrameP95Ms=Percentile(.95),FrameP99Ms=Percentile(.99),Over25Ms=frames.Count(x=>x>25),Note="WPF composition intervals, warm cache; does not measure DWM presentation or game FPS."};
            File.WriteAllText(Path.Combine(Store.Root,filename),JsonSerializer.Serialize(report,Store.Json));System.Windows.Application.Current.Shutdown();};timer.Start();
    }
}
