// C# 12 / OsdWatcher.cs / Windows .NET 8. Event-driven (no polling) default-output volume/mute change notifications for the Qt OSD.
using System;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace PanneauxGlass;
internal sealed class OsdWatcher:IDisposable,IMMNotificationClient {
    readonly Dispatcher dispatcher;readonly Action<int,bool> changed;
    MMDeviceEnumerator? devices;MMDevice? endpoint;bool disposed;int lastVolume=-1;bool lastMuted;
    internal OsdWatcher(Dispatcher dispatcher,Action<int,bool> changed){this.dispatcher=dispatcher;this.changed=changed;
        try{devices=new MMDeviceEnumerator();devices.RegisterEndpointNotificationCallback(this);Attach();}catch(Exception ex){Store.Log("OSD: "+ex.Message);}}
    void Attach(){try{Detach();if(devices==null||disposed)return;endpoint=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
        var volume=endpoint.AudioEndpointVolume;lastVolume=(int)Math.Round(volume.MasterVolumeLevelScalar*100);lastMuted=volume.Mute;volume.OnVolumeNotification+=OnVolume;}
        catch(Exception ex){Detach();Store.Log("OSD: sortie audio indisponible: "+ex.Message);}}
    void Detach(){if(endpoint==null)return;try{endpoint.AudioEndpointVolume.OnVolumeNotification-=OnVolume;}catch{}try{endpoint.Dispose();}catch{}endpoint=null;}
    void OnVolume(AudioVolumeNotificationData data){int volume=(int)Math.Round(Math.Clamp(data.MasterVolume,0,1)*100);bool muted=data.Muted;
        _ = dispatcher.BeginInvoke((Action)(()=>{if(disposed||(volume==lastVolume&&muted==lastMuted))return;lastVolume=volume;lastMuted=muted;changed(volume,muted);}));}
    public void OnDefaultDeviceChanged(DataFlow flow,Role role,string defaultDeviceId){if(flow==DataFlow.Render&&role==Role.Multimedia)_ = dispatcher.BeginInvoke((Action)Attach);}
    public void OnDeviceStateChanged(string deviceId,DeviceState newState){}
    public void OnDeviceAdded(string pwstrDeviceId){}
    public void OnDeviceRemoved(string deviceId){}
    public void OnPropertyValueChanged(string pwstrDeviceId,PropertyKey key){}
    public void Dispose(){if(disposed)return;disposed=true;try{devices?.UnregisterEndpointNotificationCallback(this);}catch{}Detach();devices?.Dispose();devices=null;}
}
