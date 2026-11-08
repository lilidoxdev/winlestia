// C#12 / Doublures du pont Qt : système, presse-papiers, icônes et raccourcis factices. Chaque appel est journalisé ; rien ne touche
// l'OS (audio, Wi-Fi, bureaux virtuels, corbeille, fond d'écran, Explorateur, presse-papiers réel).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using PanneauxGlass;
using Windows.Devices.Radios;
namespace Winlestia.Tests;

internal sealed class StubSystem : IBridgeSystem
{
    readonly ConcurrentQueue<string> calls = new();
    internal string[] Calls => calls.ToArray();
    internal AudioSnapshot Audio = new(true, 42, false, "Haut-parleurs");
    internal string? Folder;
    internal bool WindowFound = true;
    internal (int code, string text) Wallpaper = (0, "");
    internal string? Shot = "C:\\capture.png";
    void Log(string line) => calls.Enqueue(line);
    static string N(double? v) => v?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    // Attend (au plus 5 s) qu'un appel détaché sur un thread du pool soit journalisé.
    internal bool WaitFor(string call)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (calls.Contains(call))
                return true;
            System.Threading.Thread.Sleep(5);
        }
        return false;
    }

    public AudioSnapshot ReadAudio()
    {
        Log("ReadAudio");
        return Audio;
    }
    public void SetVolume(double percent)
    {
        Log("SetVolume:" + N(percent));
        Audio = Audio with { Volume = percent };
    }
    public void SetMute(bool muted)
    {
        Log("SetMute:" + muted);
        Audio = Audio with { Muted = muted };
    }
    public Task SetMediaVolume(double value)
    {
        Log("SetMediaVolume:" + N(value));
        return Task.CompletedTask;
    }
    public Task SetApplication(string id, double? level, bool? muted)
    {
        Log("SetApplication:" + id + ":" + N(level) + ":" + (muted?.ToString() ?? "-"));
        return Task.CompletedTask;
    }
    public Task SetDevice(string id, double? level, bool? muted, bool select)
    {
        Log("SetDevice:" + id + ":" + N(level) + ":" + (muted?.ToString() ?? "-") + ":" + select);
        return Task.CompletedTask;
    }
    public Task<bool> MediaCommand(string action, double position)
    {
        Log("Media:" + action + ":" + N(position));
        return Task.FromResult(true);
    }
    public double[] SpectrumValues() => new[] { 0.25, 0.5 };

    public Task WifiConnect(string ssid, string? password)
    {
        Log("WifiConnect:" + ssid + ":" + password);
        return Task.CompletedTask;
    }
    public Task WifiForget(string ssid)
    {
        Log("WifiForget:" + ssid);
        return Task.CompletedTask;
    }
    public void OpenWifiPortal() => Log("WifiPortal");
    public void OpenWifiSettings(bool location) => Log("WifiSettings:" + location);
    public Task<bool> ToggleRadio(RadioKind kind)
    {
        Log("ToggleRadio:" + kind);
        return Task.FromResult(true);
    }
    public Task<ConnectionSnapshot> ReadConnections()
    {
        Log("ReadConnections");
        return Task.FromResult(new ConnectionSnapshot(new RadioSnapshot(true, true, "Wi-Fi"), new RadioSnapshot(true, false, "BT"), true,
            "Maison", false));
    }

    public void CloseDesktop(int index) => Log("CloseDesktop:" + index);
    public void NewDesktop() => Log("NewDesktop");
    public void SwitchDesktop(int target, int relative) => Log("SwitchDesktop:" + target + ":" + relative);

    public void EmptyRecycleBin() => Log("EmptyRecycleBin");
    public void OpenFilesHandler() => Log("OpenFilesHandler");
    public void SetClipboardText(string text) => Log("SetClipboardText:" + text);
    public string? PickFolder()
    {
        Log("PickFolder");
        return Folder;
    }
    public void Shutdown() => Log("Shutdown");
    public Task SetBrightnessAtCursor(double level)
    {
        Log("Brightness:" + N(level));
        return Task.CompletedTask;
    }
    public bool ActivateApplication(long hwnd)
    {
        Log("Activate:" + hwnd);
        return WindowFound;
    }
    public IEnumerable<object> PaletteWindows()
    {
        Log("PaletteWindows");
        return new object[] { new { hwnd = 1L, title = "Fenêtre", exe = "app" } };
    }

    public DictationSnapshot DictationState() => new("idle", 0.5, new[] { 0.1f }, "fr", "bonjour", "");
    public void BeginDictationCapture() => Log("DictationBegin");
    public void EndDictationCapture() => Log("DictationEnd");

    public void NotifyWallpaper() => Log("NotifyWallpaper");
    public (int code, string text, string[] animated) ApplyWallpaper(string path, int display, bool light)
    {
        Log("ApplyWallpaper:" + path + ":" + display + ":" + light);
        return (Wallpaper.code, Wallpaper.text, new[] { "anim.mp4" });
    }
    public object WallpaperPicker()
    {
        Log("WallpaperPicker");
        return new { displays = Array.Empty<object>(), animated = new[] { "anim.mp4" } };
    }
    public string? CaptureWallpaper(Native.Rect? target)
    {
        Log("CaptureWallpaper:" + (target is { } t ? t.Left + "," + t.Top + "," + t.Right + "," + t.Bottom : "plein"));
        return Shot;
    }
    public void SyncExplorerPalette(string accent, string background, string text)
        => Log("ExplorerSync:" + accent + ":" + background + ":" + text);
    public string RestoreExplorer()
    {
        Log("ExplorerRestore");
        return "Explorateur rétabli";
    }
}

internal sealed class StubClipboard : IClipboardService
{
    internal readonly List<ClipboardItem> Store = new();
    internal readonly List<string> Calls = new();
    public IReadOnlyList<ClipboardItem> Items => Store;
    public void TogglePin(ClipboardItem item)
    {
        Calls.Add("Pin:" + item.Id);
        item.Pinned = !item.Pinned;
    }
    public void CopyBack(ClipboardItem item) => Calls.Add("CopyBack:" + item.Id);
}

internal sealed class StubIcons : IIconSource
{
    internal readonly List<string> Calls = new();
    public void Load(Entry entry, Action<ImageSource> callback, string scope = "") => Calls.Add("Load:" + entry.Id + ":" + scope);
    public bool IsPlaceholder(ImageSource image) => true;
    public int FailureCount(Entry entry) => 0;
    public void ResetFailures() => Calls.Add("ResetFailures");
}

// Éditeur de raccourcis factice : journal des appels, résultat fixe.
internal sealed class StubShortcuts : IShortcutEditor
{
    internal readonly List<string> Calls = new();
    public object[] List() => Array.Empty<object>();
    public object Status() => new { nonce = 0 };
    public ShortcutResult Reset(string id)
    {
        Calls.Add("Reset:" + id);
        return new ShortcutResult(true, "", "");
    }
    public ShortcutResult Apply(string id, string? combo)
    {
        Calls.Add("Apply:" + id + ":" + combo);
        return new ShortcutResult(true, "", "");
    }
}
