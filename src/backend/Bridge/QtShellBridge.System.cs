// language: C# 12, file: QtShellBridge.System.cs, target: Windows 11 / .NET 8, implémentation de production des ports du pont
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    // Délègue aux services du pont (champs lus à l'appel : ils sont affectés par le constructeur de production).
    sealed class RealSystem : IAudioPort, IWifiPort, IDesktopPort, IWallpaperPort, IDisplayPort, IShellPort
    {
        readonly QtShellBridge b;
        internal RealSystem(QtShellBridge bridge) => b = bridge;

        public AudioSnapshot ReadAudio() => b.audio.Read();
        public void SetVolume(double percent) => b.audio.SetVolume(percent);
        public void SetMute(bool muted) => b.audio.SetMute(muted);
        public Task SetMediaVolume(double value) => b.mixer.SetMediaVolume(value);
        public Task SetApplication(string id, double? level, bool? muted) => b.mixer.SetApplication(id, level, muted);
        public Task SetDevice(string id, double? level, bool? muted, bool select) => b.mixer.SetDevice(id, level, muted, select);
        public Task<bool> MediaCommand(string action, double position) => b.media.CommandAsync(action, position);
        public double[] SpectrumValues() => b.spectrum.Values;

        public Task WifiConnect(string ssid, string? password) => b.wifi.ConnectAsync(ssid, password);
        public Task WifiForget(string ssid) => b.wifi.ForgetAsync(ssid);
        public void OpenWifiPortal() => WifiHost.OpenPortal();
        public void OpenWifiSettings(bool location) => WifiHost.OpenSettings(location);
        public Task<bool> ToggleRadio(RadioKind kind) => b.connections.ToggleAsync(kind);
        public Task<ConnectionSnapshot> ReadConnections() => b.connections.ReadAsync();

        public void CloseDesktop(int index) => VirtualDesktops.Close(index);
        public void NewDesktop() => VirtualDesktops.CreateNew();
        public void SwitchDesktop(int target, int relative) => VirtualDesktops.SwitchTo(target, relative);

        public void EmptyRecycleBin() => RecycleActions.Empty();
        public void OpenFilesHandler() => FilesDefaultHandler.Open();
        public void SetClipboardText(string text) => System.Windows.Clipboard.SetText(text);
        public void Shutdown() => System.Windows.Application.Current.Shutdown();
        public IEnumerable<PaletteWindow> PaletteWindows() => QtShellBridge.PaletteWindows();

        public string? PickFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choisir le dossier à personnaliser",
                UseDescriptionForTitle = true
            };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        public Task<bool> SetDisplay(int id, double level) => MonitorBrightness.SetDisplayAsync(id, level);
        public IReadOnlyList<DisplayBrightness> Displays() => MonitorBrightness.Displays();
        public void StartProcess(System.Diagnostics.ProcessStartInfo info) => System.Diagnostics.Process.Start(info);

        public async Task SetBrightnessAtCursor(double level)
        {
            Native.GetCursorPos(out var point);
            await MonitorBrightness.SetAsync(point, level);
        }

        public bool ActivateApplication(long hwnd)
        {
            var app = TaskbarNative.Applications().FirstOrDefault(w => w.Handle.ToInt64() == hwnd);
            if (app == null)
                return false;
            TaskbarNative.Activate(app);
            return true;
        }

        public DictationSnapshot DictationState() => b.dictation.Snapshot();
        public void BeginDictationCapture() => b.dictation.BeginCapture();
        public void EndDictationCapture() => b.dictation.EndCapture();

        public void NotifyWallpaper() => b.wallpaper.Notify(100, true);
        public (int code, string text, string[] animated) ApplyWallpaper(string path, int display, bool light)
        {
            var done = WallpaperApply.ApplyKnown(path, display, light);
            return (done.code, done.text, WallpaperApply.Animated());
        }
        public WallpaperPickerView WallpaperPicker() => new(
            WallpaperApply.List().Select(d => new WallpaperDisplayView(d.id, d.name, d.width, d.height, d.primary, d.path)).ToArray(),
            WallpaperApply.Animated());
        public string? CaptureWallpaper(Native.Rect? target) => WallpaperMedia.CaptureHost(target);
        public void SyncExplorerPalette(string accent, string background, string text) => b.explorer.Sync(accent, background, text);
        public string RestoreExplorer() => b.explorer.Restore();
    }
}
