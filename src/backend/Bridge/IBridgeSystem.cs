// language: C# 12, file: IBridgeSystem.cs, target: Windows 11 / .NET 8, appels système que les actions du pont déclenchent
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Radios;
namespace PanneauxGlass;

// Second côté de la couture du pont : tout ce qui touche l'OS (audio, Wi-Fi, bureaux virtuels, corbeille, fond d'écran, Explorateur).
// L'implémentation de production (QtShellBridge.System.cs) délègue aux services réels ; les tests fournissent un journal.
internal interface IBridgeSystem
{
    AudioSnapshot ReadAudio();
    void SetVolume(double percent);
    void SetMute(bool muted);
    Task SetMediaVolume(double value);
    Task SetApplication(string id, double? level, bool? muted);
    Task SetDevice(string id, double? level, bool? muted, bool select);
    Task<bool> MediaCommand(string action, double position);
    double[] SpectrumValues();

    Task WifiConnect(string ssid, string? password);
    Task WifiForget(string ssid);
    void OpenWifiPortal();
    void OpenWifiSettings(bool location);
    Task<bool> ToggleRadio(RadioKind kind);
    Task<ConnectionSnapshot> ReadConnections();

    void CloseDesktop(int index);
    void NewDesktop();
    void SwitchDesktop(int target, int relative);

    void EmptyRecycleBin();
    void OpenFilesHandler();
    void SetClipboardText(string text);
    string? PickFolder();
    void Shutdown();
    Task SetBrightnessAtCursor(double level);
    bool ActivateApplication(long hwnd);
    IEnumerable<object> PaletteWindows();

    DictationSnapshot DictationState();
    void BeginDictationCapture();
    void EndDictationCapture();

    void NotifyWallpaper();
    (int code, string text, string[] animated) ApplyWallpaper(string path, int display, bool light);
    object WallpaperPicker();
    string? CaptureWallpaper(Native.Rect? target);
    void SyncExplorerPalette(string accent, string background, string text);
    string RestoreExplorer();
}
