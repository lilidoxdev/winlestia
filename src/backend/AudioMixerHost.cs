// C# 12 / AudioMixerHost.cs / Windows .NET 8. All mixer COM calls live on one background STA.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
namespace PanneauxGlass;
internal sealed class AudioMixerHost:IDisposable {
    readonly AutoResetEvent wake=new(false);readonly ConcurrentQueue<(Action<AudioMixer> action,TaskCompletionSource done)> commands=new();readonly Thread thread;
    volatile bool enabled,disposed;
    internal volatile MixerApplication[] Applications=Array.Empty<MixerApplication>();
    internal volatile MixerDevice[] Devices=Array.Empty<MixerDevice>();
    internal string MediaApp="";internal double MediaPeak;
    internal Task SetMediaVolume(double value)=>Enqueue(m=>m.SetMediaVolume(value));
    internal bool Running=>enabled;
    internal volatile bool NativeRunning,Ready;
    internal double LastReadMilliseconds {get;private set;}
    internal AudioMixerHost(){thread=new Thread(Loop){IsBackground=true,Name="Panneaux Audio"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
    internal void SetActive(bool value){if(enabled==value)return;enabled=value;Ready=false;if(!value){Applications=Array.Empty<MixerApplication>();Devices=Array.Empty<MixerDevice>();}wake.Set();}
    internal Task SetApplication(string id,double? level,bool? muted)=>Enqueue(m=>m.SetApplication(id,level,muted));
    internal Task SetDevice(string id,double? level,bool? muted,bool select)=>Enqueue(m=>m.SetDevice(id,level,muted,select));
    Task Enqueue(Action<AudioMixer> action){var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);if(disposed){done.SetException(new ObjectDisposedException(nameof(AudioMixerHost)));return done.Task;}commands.Enqueue((action,done));wake.Set();return done.Task;}
    void Publish(AudioMixer mixer){if(!enabled)return;MediaPeak=mixer.MediaPeak;Applications=mixer.Applications;Devices=mixer.Devices;Ready=true;}
    void Loop(){using var mixer=new AudioMixer();while(!disposed){
        try{
            while(commands.TryDequeue(out var command))try{if(!enabled)throw new InvalidOperationException("Ouvre le mixeur pour régler une application.");if(!mixer.Running)mixer.Refresh();command.action(mixer);Publish(mixer);command.done.SetResult();}catch(Exception ex){command.done.SetException(ex);}
            if(enabled){var watch=System.Diagnostics.Stopwatch.StartNew();mixer.MediaApp=MediaApp;mixer.Refresh();NativeRunning=true;LastReadMilliseconds=watch.Elapsed.TotalMilliseconds;Publish(mixer);}else{mixer.Stop();NativeRunning=false;}
        }catch(Exception ex){Store.Log("Mixeur audio: "+ex.Message);mixer.Stop();}
        wake.WaitOne(enabled?250:Timeout.Infinite);
    }while(commands.TryDequeue(out var command))command.done.TrySetCanceled();}
    public void Dispose(){disposed=true;wake.Set();if(thread.Join(3000))wake.Dispose();}
}
