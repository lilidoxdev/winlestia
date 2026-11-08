// C#12 / Banc d'essai du pont Qt : un QtShellBridge réel (table de dispatch, handlers) bâti sans constructeur, donc sans pipe, sans
// processus Qt ni service du bureau. Le propriétaire est factice ; Store pointe sur un dossier temporaire (aucune donnée utilisateur).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using PanneauxGlass;
namespace Winlestia.Tests;

// Raccourcis globaux factices : enregistre les appels, accepte ou refuse selon `Accept`.
internal sealed class StubKeys : IShortcutBindings
{
    internal readonly List<string> Calls = new();
    internal bool Accept = true;
    public GlobalShortcut Global => throw new NotSupportedException();
    public string ShortcutStatus => "";
    public string PaletteStatus => "";
    public string PinStatus => "";
    public string WallpaperStatus => "";
    public string CaptureStatus => "";
    public string PaletteLabel => "Ctrl+Palette";
    public string PinShortcutLabel => "";
    public string WallpaperLabel => "Ctrl+Fond";
    public string CaptureShortcutLabel => "";
    bool Take(string name, uint m, uint k, out string error)
    {
        Calls.Add(name + ":" + m + ":" + k);
        error = Accept ? "" : "déjà pris";
        return Accept;
    }
    public bool SetShortcut(uint modifiers, uint key, string view, out string error) => Take("home/" + view, modifiers, key, out error);
    public bool SetPaletteShortcut(uint modifiers, uint key, out string error) => Take("palette", modifiers, key, out error);
    public bool SetPinShortcut(uint modifiers, uint key, out string error) => Take("pin", modifiers, key, out error);
    public bool SetCaptureShortcut(uint modifiers, uint key, out string error) => Take("capture", modifiers, key, out error);
    public bool SetWallpaperShortcut(uint modifiers, uint key, out string error) => Take("wallpaper", modifiers, key, out error);
    public HotkeyAction Resolve(int id) => HotkeyAction.None;
    public void Dispose() { }
}

// Propriétaire factice : chaque appel est journalisé sous la forme « Nom:argument ».
internal sealed class StubOwner : IBridgeOwner
{
    internal readonly List<string> Calls = new();
    internal readonly StubKeys KeysStub = new();
    internal bool Taskbar;
    public IShortcutBindings Keys => KeysStub;
    public ShortcutManager Shortcuts { get; set; } = null!;
    public ClipboardService QtClipboard => throw new NotSupportedException();
    public IconLoader Icons => throw new NotSupportedException();
    public bool TaskbarEnabled => Taskbar;
    public void SetTheme(string id) => Calls.Add("SetTheme:" + id);
    public void SetTaskbar(bool enabled) => Calls.Add("SetTaskbar:" + enabled);
    public void SuspendTaskbar() => Calls.Add("SuspendTaskbar");
    public void RestoreDesktopIcons() => Calls.Add("RestoreDesktopIcons");
    public void ApplyClipboardSettings() => Calls.Add("ApplyClipboardSettings");
    public void RefreshCatalogue() => Calls.Add("RefreshCatalogue");
    public void RefreshCatalogueIfStale() => Calls.Add("RefreshCatalogueIfStale");
    public void Launch(Entry entry) => Calls.Add("Launch:" + entry.Id);
    public void LaunchTarget(string target) => Calls.Add("LaunchTarget:" + target);
    public void Reveal(Entry entry) => Calls.Add("Reveal:" + entry.Id);
    public void Edit(Entry entry) => Calls.Add("Edit:" + entry.Id);
    public void RunQuickTab(WindowsShortcut tab) => Calls.Add("RunQuickTab:" + tab.Id);
    public void WebSearch(string value) => Calls.Add("WebSearch:" + value);
    public void AddAccess(string board) => Calls.Add("AddAccess:" + board);
    public void AddFolder(string board) => Calls.Add("AddFolder:" + board);
    public void AddWebsite() => Calls.Add("AddWebsite");
    public void OpenWorkspaces() => Calls.Add("OpenWorkspaces");
    public void OpenTools() => Calls.Add("OpenTools");
    public void OpenRuler() => Calls.Add("OpenRuler");
    public void OpenNotes() => Calls.Add("OpenNotes");
    public void OpenMemo() => Calls.Add("OpenMemo");
    public void OpenLargeFiles() => Calls.Add("OpenLargeFiles");
    public void OpenHelp() => Calls.Add("OpenHelp");
    public void OpenFocus() => Calls.Add("OpenFocus");
    public void OpenColorPicker() => Calls.Add("OpenColorPicker");
    public void OpenCapture() => Calls.Add("OpenCapture");
    public void OpenAudio() => Calls.Add("OpenAudio");
    public void EditQuickTabs() => Calls.Add("EditQuickTabs");
    public void ToggleNotifications() => Calls.Add("ToggleNotifications");
    public void ToggleAlwaysOnTop() => Calls.Add("ToggleAlwaysOnTop");
    public void TaskManager() => Calls.Add("TaskManager");
    public void PinImage(System.Windows.Media.Imaging.BitmapImage image) => Calls.Add("PinImage");
}

internal sealed class BridgeRig : IDisposable
{
    internal readonly StubOwner Owner = new();
    internal readonly QtShellBridge Bridge;
    internal readonly TempDir Dir = new();
    readonly Preferences state = Store.State;
    readonly List<Entry> entries = Store.Entries;
    readonly HashSet<string> missing = Store.Missing;
    readonly string? env = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");

    internal BridgeRig()
    {
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", Dir.Path);
        Store.State = new Preferences();
        Store.Entries = new List<Entry>();
        Store.Missing = new HashSet<string>();
        // Sans constructeur : ni pipe, ni Process Qt, ni hooks, ni initialiseurs de champs (registre, threads audio, Explorateur).
        Bridge = (QtShellBridge)RuntimeHelpers.GetUninitializedObject(typeof(QtShellBridge));
        var dispatcher = Dispatcher.CurrentDispatcher;
        Set("owner", Owner);
        Set("options", RunOptions.Default);
        Set("dispatcher", dispatcher);
        Set("cancellation", new CancellationTokenSource());
        Set("wake", new EventWaitHandle(false, EventResetMode.AutoReset));
        Set("events", new Queue<(string name, long epoch)>());
        Set("icons", new Dictionary<string, string>());
        Set("requested", new HashSet<string>());
        Set("dockEntries", new Dictionary<string, Entry>());
        Set("tiler", new WindowTiler());
        Set("frame", new ScreenFrameReservations());
        Set("fileSearch", new FileSearch());
        Set("bars", new AudioBarsHost());
        Set("social", new SocialService(dispatcher, () => { }));
        Set("bookmarks", new BookmarksService(dispatcher, () => { }));
    }

    void Set(string field, object value)
        => typeof(QtShellBridge).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Bridge, value);

    internal Preferences Prefs => Store.State;

    internal Entry AddEntry(string id, string name = "App", string section = "Applications", string target = "x.exe")
    {
        var e = new Entry { Id = id, Name = name, Section = section, Target = target };
        Store.Entries.Add(e);
        return e;
    }

    // Même découpage que Handle : commande, args, id, puis dispatch réel ; renvoie la réponse brute, le notice et les évènements.
    internal (string? Raw, BridgeCall Call, string[] Events) Run(string command, string argsJson = "{}")
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement.Clone();
        string id = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
        var call = new BridgeCall(args, id);
        string? raw = Bridge.RunAction(command, call).GetAwaiter().GetResult();
        return (raw, call, Bridge.DrainEvents());
    }

    // Exécute les BeginInvoke du pont en attente (dialogues, rafraîchissements différés).
    internal void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        Store.State = state;
        Store.Entries = entries;
        Store.Missing = missing;
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", env);
        Dir.Dispose();
    }
}
