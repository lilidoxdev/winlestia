// language: C# 12, file: AudioTools.cs, target: Windows 11 / .NET 8 WPF, audio endpoints
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
internal sealed record AudioDevice(string Id,string Name,bool Default,bool Muted);
[ComImport,Guid("f8679f50-850a-41cf-9c72-430f290290c8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig {
    [PreserveSig]int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr format);
    [PreserveSig]int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)]string id,bool isDefault,IntPtr format);
    [PreserveSig]int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)]string id);
    [PreserveSig]int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr endpoint,IntPtr mix);
    [PreserveSig]int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)]string id,bool isDefault,IntPtr a,IntPtr b);
    [PreserveSig]int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr period);
    [PreserveSig]int GetShareMode([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr mode);
    [PreserveSig]int SetShareMode([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr mode);
    [PreserveSig]int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)]string id,bool store,IntPtr key,IntPtr value);
    [PreserveSig]int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)]string id,bool store,IntPtr key,IntPtr value);
    [PreserveSig]int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,int role);
    [PreserveSig]int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)]string id,int visible);
}
internal static class AudioSwitch {
    internal static List<AudioDevice> Devices(int flow){
        using var devices=new NAudio.CoreAudioApi.MMDeviceEnumerator();var result=new List<AudioDevice>();string current="";
        try{using var device=devices.GetDefaultAudioEndpoint((NAudio.CoreAudioApi.DataFlow)flow,NAudio.CoreAudioApi.Role.Multimedia);current=device.ID;}catch{}
        foreach(var device in devices.EnumerateAudioEndPoints((NAudio.CoreAudioApi.DataFlow)flow,NAudio.CoreAudioApi.DeviceState.Active))using(device){result.Add(new(device.ID,device.FriendlyName,device.ID==current,flow==1&&device.AudioEndpointVolume.Mute));}
        return result.OrderByDescending(d=>d.Default).ThenBy(d=>d.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    internal static void SetDefault(string id){var config=(IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")))!;try{for(int role=0;role<=2;role++){int hr=config.SetDefaultEndpoint(id,role);if(hr!=0&&role==0)throw new COMException("Windows a refusé le changement de sortie.",hr);}}finally{Marshal.ReleaseComObject(config);}}
    internal static void SetMicMute(bool mute){using var devices=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var device=devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture,NAudio.CoreAudioApi.Role.Multimedia);device.AudioEndpointVolume.Mute=mute;}
    internal static bool MicMuted(){try{using var devices=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var device=devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture,NAudio.CoreAudioApi.Role.Multimedia);return device.AudioEndpointVolume.Mute;}catch{return false;}}
}

internal sealed class AudioWindow:GlassWindow {
    readonly Coordinator owner;readonly StackPanel body=new();readonly TextBlock status;
    internal AudioWindow(Coordinator coordinator):base("Panneaux Glass — son et micro"){
        owner=coordinator;Width=560;Height=Math.Min(640,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=32;ShowActivated=true;
        ContentHost.Margin=new Thickness(26,22,26,20);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Le bon son, tout de suite.",25,null,true));var sub=UI.Text("Change de casque, de micro ou coupe le micro sans passer par les réglages",11,UI.Dim);sub.TextWrapping=TextWrapping.Wrap;sub.Margin=new Thickness(0,6,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,16,0,10)};Grid.SetRow(scroll,1);ContentHost.Children.Add(scroll);
        status=UI.Text("",11,UI.Dim);status.TextWrapping=TextWrapping.Wrap;Grid.SetRow(status,2);ContentHost.Children.Add(status);
        Loaded+=(_,_)=>{ApplyGlass();Rebuild();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    void Rebuild(){
        body.Children.Clear();
        try{
            var outputs=AudioSwitch.Devices(0);var inputs=AudioSwitch.Devices(1);
            body.Children.Add(UI.Text("SORTIE",10,UI.Brush("#ABD2E4F1"),true));
            if(outputs.Count==0)body.Children.Add(Note("Aucune sortie active détectée."));
            foreach(var device in outputs)body.Children.Add(Row(device,"\uE7F5",()=>Apply(device,false)));
            body.Children.Add(UI.Text("MICRO",10,UI.Brush("#ABD2E4F1"),true));
            if(inputs.Count==0)body.Children.Add(Note("Aucun microphone actif détecté."));
            foreach(var device in inputs)body.Children.Add(Row(device,"\uE720",()=>Apply(device,true)));
            if(inputs.Count>0){var muted=inputs.FirstOrDefault(d=>d.Default)?.Muted==true;var toggle=UI.Button(muted?"Réactiver le micro":"Couper le micro",()=>{try{AudioSwitch.SetMicMute(!muted);Rebuild();owner.ShowToast(!muted?"Micro coupé":"Micro réactivé");}catch(Exception ex){status.Text=ex.Message;}});toggle.HorizontalAlignment=HorizontalAlignment.Left;toggle.Margin=new Thickness(0,6,0,4);body.Children.Add(toggle);}
            var settings=UI.Button("Ouvrir les réglages Windows",()=>owner.LaunchWindowsShortcut(new WindowsShortcut{Name="Son",Target="ms-settings:sound"}));settings.HorizontalAlignment=HorizontalAlignment.Left;settings.Margin=new Thickness(0,14,0,0);body.Children.Add(settings);
            status.Text="Le changement est immédiat pour toutes les applications.";
        }catch(Exception ex){status.Text="Réglage audio indisponible : "+ex.Message;body.Children.Add(Note("Windows a refusé l’accès. Ouvre les réglages Windows pour changer de sortie."));}
    }
    FrameworkElement Row(AudioDevice device,string glyph,Action pick){
        var button=UI.Button("",pick);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(12,10,12,10);button.Margin=new Thickness(0,4,0,0);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(30)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=UI.Glyph(glyph,16);icon.HorizontalAlignment=HorizontalAlignment.Left;grid.Children.Add(icon);
        var name=UI.Text(device.Name,13,device.Default?UI.White:UI.Dim);name.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(name,1);grid.Children.Add(name);
        if(device.Default){var check=UI.Glyph("\uE73E",12);check.Foreground=UI.Accent;Grid.SetColumn(check,2);grid.Children.Add(check);}
        else if(device.Muted){var muted=UI.Text("muet",10,UI.Dim);Grid.SetColumn(muted,2);grid.Children.Add(muted);}
        button.Content=grid;button.ToolTip=device.Default?"Sortie actuelle":"Choisir cette sortie";
        System.Windows.Automation.AutomationProperties.SetName(button,(device.Default?"Actuel : ":"Choisir ")+device.Name);
        return button;
    }
    TextBlock Note(string text){var note=UI.Text(text,12,UI.Dim);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(2,4,2,8);return note;}
    void Apply(AudioDevice device,bool input){
        try{AudioSwitch.SetDefault(device.Id);Rebuild();owner.ShowToast((input?"Micro : ":"Sortie : ")+device.Name);}
        catch(Exception ex){status.Text=ex.Message;}
    }
}
