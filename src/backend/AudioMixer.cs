// C# 12 / AudioMixer.cs / Windows .NET 8. Shared NAudio Core Audio ownership avoids duplicate COM coclass identities.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace PanneauxGlass;
internal sealed record MixerApplication(string id,string name,string path,int pid,double volume,bool muted,bool playing);
internal sealed record MixerDevice(string id,string name,bool selected,double volume,bool muted,int flow);
internal sealed class AudioMixer:IDisposable {
    sealed record Session(string Id,string Name,string Path,int Pid,AudioSessionControl Control);
    readonly List<Session> sessions=new();readonly List<MMDevice> endpoints=new();DateTime nextScan,nextDeviceScan;
    internal string MediaApp=""; internal double MediaPeak {get;private set;}
    internal bool Running {get;private set;}
    internal MixerApplication[] Applications {get;private set;}=Array.Empty<MixerApplication>();
    internal MixerDevice[] Devices {get;private set;}=Array.Empty<MixerDevice>();
    internal void Refresh(){
        Running=true;if(DateTime.UtcNow>=nextScan){Scan();nextScan=DateTime.UtcNow.AddSeconds(2);}
        MediaPeak=0;var result=new List<MixerApplication>();
        foreach(var group in sessions.GroupBy(s=>s.Id)){
            double volume=0;bool muted=true,playing=false;int count=0;
            foreach(var s in group)try{var state=s.Control.State;if(state==AudioSessionState.AudioSessionStateExpired)continue;var gain=s.Control.SimpleAudioVolume;if(MatchesMedia(s.Path)&&!gain.Mute)MediaPeak=Math.Max(MediaPeak,s.Control.AudioMeterInformation.MasterPeakValue);volume=Math.Max(volume,gain.Volume*100);muted&=gain.Mute;playing|=state==AudioSessionState.AudioSessionStateActive;count++;}catch(System.Runtime.InteropServices.COMException){}
            var first=group.First();if(count>0)result.Add(new(first.Id,first.Name,first.Path,first.Pid,Math.Round(volume),muted,playing));
        }
        Applications=result.OrderBy(a=>a.name,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.id).ToArray();
        if(DateTime.UtcNow>=nextDeviceScan){Devices=ReadDevices();nextDeviceScan=DateTime.UtcNow.AddSeconds(1);}
    }
    void Scan(){
        ClearSessions();using var enumerator=new MMDeviceEnumerator();
        foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){
            endpoints.Add(device);
            try{var list=device.AudioSessionManager.Sessions;
                for(int i=0;i<list.Count;i++){
                    var control=list[i];bool keep=false;
                    try{
                        if(control.State==AudioSessionState.AudioSessionStateExpired)continue;int pid=(int)control.GetProcessID;
                        string name="Sons système",path="";
                        if(pid!=0){try{using var process=Process.GetProcessById(pid);path=process.MainModule?.FileName??"";name=string.IsNullOrEmpty(path)?process.ProcessName:FileVersionInfo.GetVersionInfo(path).FileDescription??process.ProcessName;}catch{name="Application "+pid;}}
                        string key=pid==0?"system":!string.IsNullOrEmpty(path)?path.ToLowerInvariant():"pid:"+pid;
                        string id="audio:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20];
                        sessions.Add(new(id,name,path,pid,control));keep=true;
                    }catch(System.Runtime.InteropServices.COMException){}finally{if(!keep)control.Dispose();}
                }
            }catch(System.Runtime.InteropServices.COMException){}
        }
    }
    static MixerDevice[] ReadDevices(){
        using var enumerator=new MMDeviceEnumerator();var result=new List<MixerDevice>();
        foreach(int flow in new[]{0,1}){
            string current="";try{using var device=enumerator.GetDefaultAudioEndpoint((DataFlow)flow,Role.Multimedia);current=device.ID;}catch{}
            foreach(var device in enumerator.EnumerateAudioEndPoints((DataFlow)flow,DeviceState.Active))using(device)try{var gain=device.AudioEndpointVolume;result.Add(new(device.ID,device.FriendlyName,current==device.ID,Math.Round(gain.MasterVolumeLevelScalar*100),gain.Mute,flow));}catch(System.Runtime.InteropServices.COMException){}
        }
        return result.OrderByDescending(d=>d.selected).ThenBy(d=>d.name).ToArray();
    }
    bool MatchesMedia(string path)=>!string.IsNullOrEmpty(MediaApp)&&System.IO.Path.GetFileNameWithoutExtension(path).Equals(MediaApp switch{"Microsoft Edge"=>"msedge","Google Chrome"=>"chrome",_=>MediaApp},StringComparison.OrdinalIgnoreCase);
    internal void SetMediaVolume(double value){Refresh();var ids=sessions.Where(s=>MatchesMedia(s.Path)).Select(s=>s.Id).Distinct().ToArray();if(ids.Length==0)throw new InvalidOperationException("Aucune session audio pour ce lecteur.");foreach(var id in ids)SetApplication(id,value,null);}
    internal void SetApplication(string id,double? level,bool? muted){
        int updated=0;foreach(var s in sessions.Where(s=>s.Id==id))try{var gain=s.Control.SimpleAudioVolume;if(level.HasValue)gain.Volume=(float)Math.Clamp(level.Value/100,0,1);if(muted.HasValue)gain.Mute=muted.Value;updated++;}catch(System.Runtime.InteropServices.COMException){}
        if(updated==0)throw new InvalidOperationException("Cette application n’a plus de session audio. Relance sa lecture.");Refresh();
    }
    internal void SetDevice(string id,double? level,bool? muted,bool select){
        if(!Devices.Any(d=>d.id==id))throw new InvalidOperationException("Ce périphérique n’est plus connecté.");
        if(select)AudioSwitch.SetDefault(id);else{using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(id);var gain=device.AudioEndpointVolume;if(level.HasValue)gain.MasterVolumeLevelScalar=(float)Math.Clamp(level.Value/100,0,1);if(muted.HasValue)gain.Mute=muted.Value;}
        nextDeviceScan=DateTime.MinValue;Refresh();
    }
    void ClearSessions(){foreach(var session in sessions)session.Control.Dispose();sessions.Clear();foreach(var device in endpoints)device.Dispose();endpoints.Clear();}
    internal void Stop(){if(!Running)return;ClearSessions();Applications=Array.Empty<MixerApplication>();Devices=Array.Empty<MixerDevice>();Running=false;nextScan=nextDeviceScan=DateTime.MinValue;}
    public void Dispose()=>Stop();
}
