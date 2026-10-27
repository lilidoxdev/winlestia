// C# 12 / SystemAudio.cs / Windows .NET 8. Same Core Audio factory as mixer and spectrum.
using System;
using NAudio.CoreAudioApi;
namespace PanneauxGlass;
internal sealed record AudioSnapshot(bool Available,double Volume,bool Muted,string Device,string Error="");
internal sealed class SystemAudio:IDisposable {
    MMDeviceEnumerator? devices;MMDevice? endpoint;string endpointId="";
    internal AudioSnapshot Read(){try{
        devices??=new MMDeviceEnumerator();var current=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
        if(endpoint==null||current.ID!=endpointId){ReleaseEndpoint();endpoint=current;endpointId=current.ID;}else current.Dispose();
        var volume=endpoint.AudioEndpointVolume;return new(true,Math.Clamp(volume.MasterVolumeLevelScalar*100,0,100),volume.Mute,endpoint.FriendlyName);
    }catch(Exception ex){ReleaseEndpoint();return new(false,0,false,"Aucune sortie audio",ex.Message);}}
    internal void SetVolume(double percent){if(endpoint==null)Read();if(endpoint==null)throw new InvalidOperationException("Aucune sortie audio disponible.");endpoint.AudioEndpointVolume.MasterVolumeLevelScalar=(float)Math.Clamp(percent/100,0,1);}
    internal void SetMute(bool value){if(endpoint==null)Read();if(endpoint==null)throw new InvalidOperationException("Aucune sortie audio disponible.");endpoint.AudioEndpointVolume.Mute=value;}
    void ReleaseEndpoint(){endpoint?.Dispose();endpoint=null;endpointId="";}
    public void Dispose(){ReleaseEndpoint();devices?.Dispose();devices=null;}
}
