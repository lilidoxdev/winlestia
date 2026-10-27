// C# 12 / AudioMixerVerification.cs / Windows .NET 8. Writes only our silent test session.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NAudio.Wave;
namespace PanneauxGlass;
internal static class AudioMixerVerification {
    sealed class Silence:IWaveProvider {
        public WaveFormat WaveFormat {get;}=new(48000,16,2);
        public int Read(byte[] buffer,int offset,int count){Array.Clear(buffer,offset,count);return count;}
    }
    internal static int Run(){
        var checks=new List<object>();bool passed=true;
        void Check(string name,bool ok){checks.Add(new{name,pass=ok});passed&=ok;}
        using var mixer=new AudioMixer();
        try{
            using var system=new SystemAudio();Check("Master output and mixer share compatible Core Audio ownership",system.Read().Available);
            using var spectrum=new WindowsSpectrum();spectrum.Start();Thread.Sleep(100);spectrum.Stop();
            mixer.Refresh();Check("Enumerates native output endpoints",mixer.Devices.Any(d=>d.flow==0));
            var before=mixer.Applications.Where(a=>a.pid!=Environment.ProcessId).ToDictionary(a=>a.id,a=>(a.volume,a.muted));
            using var playback=new WaveOutEvent();playback.Init(new Silence());playback.Play();Thread.Sleep(400);
            mixer.Stop();mixer.Refresh();var own=mixer.Applications.FirstOrDefault(a=>a.pid==Environment.ProcessId);
            Check("Discovers dedicated silent session",own!=null);
            if(own!=null)try{
                mixer.MediaApp=Path.GetFileNameWithoutExtension(Environment.ProcessPath??"");mixer.SetMediaVolume(29);
                Check("Selected-media gain targets own fixture",mixer.Applications.First(a=>a.id==own.id).volume==29);
                mixer.MediaApp="WinlestiaMissingMediaFixture";mixer.Refresh();Check("Unmatched media has no global fallback",mixer.MediaPeak==0);
                bool missingMedia=false;try{mixer.SetMediaVolume(40);}catch(InvalidOperationException){missingMedia=true;}Check("Unmatched media gain rejected",missingMedia);
                mixer.SetApplication(own.id,37,null);Check("Per-application gain set to 37%",mixer.Applications.First(a=>a.id==own.id).volume==37);
                mixer.SetApplication(own.id,null,true);Check("Per-application mute",mixer.Applications.First(a=>a.id==own.id).muted);
                mixer.SetApplication(own.id,null,false);Check("Per-application unmute",!mixer.Applications.First(a=>a.id==own.id).muted);
                mixer.SetApplication(own.id,140,null);Check("Volume is clamped to 100%",mixer.Applications.First(a=>a.id==own.id).volume==100);
                Check("Other applications untouched",mixer.Applications.Where(a=>before.ContainsKey(a.id)).All(a=>before[a.id]==(a.volume,a.muted)));
            }finally{mixer.SetApplication(own.id,own.volume,own.muted);}
            playback.Stop();
            bool rejected=false;try{mixer.SetApplication("missing",50,null);}catch(InvalidOperationException){rejected=true;}Check("Closed or invalid session rejected",rejected);
            Check("Device values are finite and bounded",mixer.Devices.All(d=>double.IsFinite(d.volume)&&d.volume>=0&&d.volume<=100));
            mixer.Stop();Check("Hidden mixer releases sessions",!mixer.Running&&mixer.Applications.Length==0&&mixer.Devices.Length==0);
            using var host=new AudioMixerHost();var watch=System.Diagnostics.Stopwatch.StartNew();host.SetActive(true);double activation=watch.Elapsed.TotalMilliseconds;
            Check("Opening audio does not wait for native enumeration",activation<20);
            for(int i=0;i<100&&!host.Ready;i++)Thread.Sleep(50);
            Check("Background audio worker returns live devices",host.Ready&&host.Devices.Length>0);
            host.SetActive(false);for(int i=0;i<60&&host.NativeRunning;i++)Thread.Sleep(50);
            Check("Background audio worker stops when closed",!host.NativeRunning);
        }catch(Exception ex){Check(ex.ToString(),false);}
        File.WriteAllText(Path.Combine(Store.Root,"audio-mixer-verification.json"),JsonSerializer.Serialize(new{allPassed=passed,checks},new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
}
