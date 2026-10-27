// language: C# 12, file: GlobalShortcut.cs, target: Windows 11 / .NET 8 WPF, two independent atomic RegisterHotKey bindings
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PanneauxGlass;
public sealed class ShortcutPreferences {
    public uint Modifiers {get;set;}=2;public uint Key {get;set;}=0x70;public string View {get;set;}="controls";
    public void Normalize(){if(!GlobalShortcut.Valid(Modifiers,Key)){Modifiers=2;Key=0x70;}if(!new[]{"home","controls","pc","workspace","last"}.Contains(View))View="controls";}
}
// Four user bindings share one message window: the launcher (3/4), the palette (5/6), pin (7/8) and capture (9/10).
// Each replacement reserves the new combination before releasing the previous one.
internal sealed class GlobalShortcut:IDisposable {
    readonly IntPtr window;int customId,paletteId,pinId,captureId;uint modifiers,key,paletteModifiers,paletteKey,pinModifiers,pinKey,captureModifiers,captureKey;bool legacy,rescue;
    internal string Error {get;private set;}="";internal string PaletteError {get;private set;}="";internal string PinError {get;private set;}="";internal string CaptureError {get;private set;}="";
    internal GlobalShortcut(IntPtr hwnd,ShortcutPreferences preferences):this(hwnd,preferences,new(){Modifiers=2,Key=0x71}){}
    internal GlobalShortcut(IntPtr hwnd,ShortcutPreferences preferences,ShortcutPreferences? palette=null,ShortcutPreferences? pin=null,ShortcutPreferences? capture=null){
        window=hwnd;legacy=Native.RegisterHotKey(window,1,0x4003,0x20);rescue=Native.RegisterHotKey(window,2,0x4003,0x08);
        TrySet(preferences.Modifiers,preferences.Key,out _);if(palette!=null)TrySetPalette(palette.Modifiers,palette.Key,out _);if(pin!=null)TrySetPin(pin.Modifiers,pin.Key,out _);if(capture!=null)TrySetCapture(capture.Modifiers,capture.Key,out _);
    }
    internal bool Matches(int id)=>id==1&&legacy||id==customId&&customId!=0;
    internal bool MatchesPalette(int id)=>id==paletteId&&paletteId!=0;
    internal bool MatchesPin(int id)=>id==pinId&&pinId!=0;
    internal bool MatchesCapture(int id)=>id==captureId&&captureId!=0;
    internal bool TrySetCapture(uint nextModifiers,uint nextKey,out string error){error="";
        if(!Valid(nextModifiers,nextKey)){error="Choisis Ctrl ou Alt et une touche de fonction.";return false;}
        if((nextModifiers==modifiers&&nextKey==key&&customId!=0)||(nextModifiers==paletteModifiers&&nextKey==paletteKey&&paletteId!=0)||(nextModifiers==pinModifiers&&nextKey==pinKey&&pinId!=0)){error="Cette combinaison est déjà utilisée par Panneaux.";return false;}
        if(captureId!=0&&nextModifiers==captureModifiers&&nextKey==captureKey){CaptureError="";return true;}
        int candidate=captureId==9?10:9;if(!Native.RegisterHotKey(window,candidate,nextModifiers|0x4000,nextKey)){int code=Marshal.GetLastWin32Error();error=code==1409?"Cette combinaison est déjà utilisée par une autre application.":"Windows refuse cette combinaison ("+code+").";CaptureError=error;return false;}
        if(captureId!=0)Native.UnregisterHotKey(window,captureId);captureId=candidate;captureModifiers=nextModifiers;captureKey=nextKey;CaptureError="";return true;
    }
    internal bool TrySetPin(uint nextModifiers,uint nextKey,out string error){error="";
        if(!Valid(nextModifiers,nextKey)){error="Choisis Ctrl ou Alt et une touche de fonction.";return false;}
        if((nextModifiers==modifiers&&nextKey==key&&customId!=0)||(nextModifiers==paletteModifiers&&nextKey==paletteKey&&paletteId!=0)){error="Cette combinaison est déjà utilisée par Panneaux.";return false;}
        if((nextModifiers==captureModifiers&&nextKey==captureKey&&captureId!=0)){error="Cette combinaison lance déjà la capture.";return false;}
        if(pinId!=0&&nextModifiers==pinModifiers&&nextKey==pinKey){PinError="";return true;}
        int candidate=pinId==7?8:7;if(!Native.RegisterHotKey(window,candidate,nextModifiers|0x4000,nextKey)){int code=Marshal.GetLastWin32Error();error=code==1409?"Cette combinaison est déjà utilisée par une autre application.":"Windows refuse cette combinaison ("+code+").";PinError=error;return false;}
        if(pinId!=0)Native.UnregisterHotKey(window,pinId);pinId=candidate;pinModifiers=nextModifiers;pinKey=nextKey;PinError="";return true;
    }
    internal bool TrySet(uint nextModifiers,uint nextKey,out string error){error="";
        if(!Valid(nextModifiers,nextKey)){error="Choisis Ctrl ou Alt et une touche. Ctrl + Alt + Retour arrière est réservé au secours.";return false;}
        if(nextModifiers==paletteModifiers&&nextKey==paletteKey&&paletteId!=0){error="Ce raccourci ouvre déjà la commande rapide. Choisis une autre combinaison.";return false;}
        if(nextModifiers==modifiers&&nextKey==key&&(customId!=0||nextModifiers==3&&nextKey==0x20&&legacy)){Error="";return true;}
        if(nextModifiers==3&&nextKey==0x20&&legacy){if(customId!=0)Native.UnregisterHotKey(window,customId);customId=0;modifiers=nextModifiers;key=nextKey;Error="";return true;}
        int candidate=customId==3?4:3;if(!Native.RegisterHotKey(window,candidate,nextModifiers|0x4000,nextKey)){int code=Marshal.GetLastWin32Error();error=code==1409?"Ce raccourci est déjà utilisé. Choisis une autre combinaison ; le précédent reste actif.":"Windows refuse ce raccourci ("+code+"). Le précédent reste actif.";Error=error;return false;}
        if(customId!=0)Native.UnregisterHotKey(window,customId);customId=candidate;modifiers=nextModifiers;key=nextKey;Error="";return true;
    }
    internal bool TrySetPalette(uint nextModifiers,uint nextKey,out string error){error="";
        if(!Valid(nextModifiers,nextKey)){error="Choisis Ctrl ou Alt et une touche. Ctrl + Alt + Retour arrière est réservé au secours.";return false;}
        if(nextModifiers==modifiers&&nextKey==key&&customId!=0){error="Cette combinaison ouvre déjà ton espace. Choisis une autre touche.";return false;}
        if(paletteId!=0&&nextModifiers==paletteModifiers&&nextKey==paletteKey){PaletteError="";return true;}
        int candidate=paletteId==5?6:5;if(!Native.RegisterHotKey(window,candidate,nextModifiers|0x4000,nextKey)){int code=Marshal.GetLastWin32Error();error=code==1409?"Ce raccourci est déjà utilisé. Choisis une autre combinaison ; le précédent reste actif.":"Windows refuse ce raccourci ("+code+"). Le précédent reste actif.";PaletteError=error;return false;}
        if(paletteId!=0)Native.UnregisterHotKey(window,paletteId);paletteId=candidate;paletteModifiers=nextModifiers;paletteKey=nextKey;PaletteError="";return true;
    }
    internal static bool Valid(uint modifiers,uint key)=>(modifiers&~7u)==0&&(modifiers&3)!=0&&(key is >=0x70 and <=0x87&&key!=0x7B||key is >=0x41 and <=0x5A||key is >=0x30 and <=0x39||key==0x20)&&!(modifiers==3&&key==0x08);
    internal static IEnumerable<uint> Keys=>Enumerable.Range(0x70,24).Where(k=>k!=0x7B).Concat(Enumerable.Range(0x41,26)).Concat(Enumerable.Range(0x30,10)).Append(0x20).Select(k=>(uint)k);
    internal static string KeyName(uint key)=>key is >=0x70 and <=0x87?"F"+(key-0x6F):key==0x20?"Espace":((char)key).ToString();
    internal static string Label(uint modifiers,uint key)=>((modifiers&2)!=0?"Ctrl + ":"")+((modifiers&1)!=0?"Alt + ":"")+((modifiers&4)!=0?"Maj + ":"")+KeyName(key);
    public void Dispose(){if(customId!=0)Native.UnregisterHotKey(window,customId);if(paletteId!=0)Native.UnregisterHotKey(window,paletteId);if(pinId!=0)Native.UnregisterHotKey(window,pinId);if(captureId!=0)Native.UnregisterHotKey(window,captureId);if(legacy)Native.UnregisterHotKey(window,1);if(rescue)Native.UnregisterHotKey(window,2);}
}
