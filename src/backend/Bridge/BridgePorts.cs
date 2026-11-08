// language: C# 12, file: BridgePorts.cs, target: Windows 11 / .NET 8, appels système que les actions du pont déclenchent
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Radios;
namespace PanneauxGlass;

// Second côté de la couture du pont : tout ce qui touche l'OS, découpé en ports cohérents pour que chaque test ne doive doubler
// que ce qu'il exerce. L'implémentation de production (QtShellBridge.System.cs) délègue aux services réels ; les tests fournissent
// un journal. Les noms en minuscules des vues ci-dessous sont ceux du JSON envoyé au frontend : ne pas les renommer.
internal sealed record PaletteWindow(long hwnd, string title, string exe);
internal sealed record WallpaperDisplayView(int id, string name, int width, int height, bool primary, string path);
internal sealed record WallpaperPickerView(WallpaperDisplayView[] displays, string[] animated);

// Volume, mixeur, lecture multimédia, spectre et dictée (capture du micro).
internal interface IAudioPort
{
    AudioSnapshot ReadAudio();
    void SetVolume(double percent);
    void SetMute(bool muted);
    Task SetMediaVolume(double value);
    Task SetApplication(string id, double? level, bool? muted);
    Task SetDevice(string id, double? level, bool? muted, bool select);
    Task<bool> MediaCommand(string action, double position);
    double[] SpectrumValues();

    DictationSnapshot DictationState();
    void BeginDictationCapture();
    void EndDictationCapture();
}

internal interface IWifiPort
{
    Task WifiConnect(string ssid, string? password);
    Task WifiForget(string ssid);
    void OpenWifiPortal();
    void OpenWifiSettings(bool location);
    Task<bool> ToggleRadio(RadioKind kind);
    Task<ConnectionSnapshot> ReadConnections();
}

// Bureaux virtuels et fenêtres ouvertes.
internal interface IDesktopPort
{
    void CloseDesktop(int index);
    void NewDesktop();
    void SwitchDesktop(int target, int relative);
    bool ActivateApplication(long hwnd);
    IEnumerable<PaletteWindow> PaletteWindows();
}

internal interface IWallpaperPort
{
    void NotifyWallpaper();
    (int code, string text, string[] animated) ApplyWallpaper(string path, int display, bool light);
    WallpaperPickerView WallpaperPicker();
    string? CaptureWallpaper(Native.Rect? target);
}

// Luminosité des écrans (DDC/CI).
internal interface IDisplayPort
{
    Task SetBrightnessAtCursor(double level);
    Task<bool> SetDisplay(int id, double level);
    IReadOnlyList<DisplayBrightness> Displays();
}

// Le reste du shell : corbeille, boîtes de dialogue, presse-papiers, arrêt, Explorateur, lancement de processus.
internal interface IShellPort
{
    void EmptyRecycleBin();
    void OpenFilesHandler();
    void SetClipboardText(string text);
    string? PickFolder();
    void Shutdown();
    void SyncExplorerPalette(string accent, string background, string text);
    string RestoreExplorer();
    void StartProcess(ProcessStartInfo info);
}
